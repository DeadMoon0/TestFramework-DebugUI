using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers watching live runs over the debug pipe.
/// </summary>
/// <remarks>
/// These drive real timelines against a real pipe rather than a stubbed transport. That is
/// deliberate: the interesting failures in this layer are all about two processes agreeing —
/// framing, the pipe name, who answers a breakpoint — and a stub agrees with itself by
/// construction. Each test gets its own pipe name so it cannot collide with a UI the developer has
/// open, or with the next test.
/// </remarks>
[Collection(TimelineRunCollection.Name)]
public sealed class PipeRunEventSourceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ALiveRunIsProjectedAsItHappens()
    {
        // The headline capability for a live run: it appears on the board without the journal, the
        // disk, or any second code path being involved.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        await RunTimelineAsync("live");
        watcher.Ingest.Flush();

        Assert.Single(watcher.Store.GetValue(state => state.Runs));
        Assert.True(watcher.Store.GetValue(state => state.ActiveRun).IsFinished);

        StepNode step = Steps(watcher).Single(candidate => candidate.DisplayName == "live");
        Assert.Equal(DebugLifecycleState.Complete, step.Lifecycle);
    }

    [Fact]
    public async Task RunsInParallelAllAttach()
    {
        // The single-instance server this replaces accepted one connection: a second concurrent run
        // simply lost its debugger, with nothing anywhere saying so.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        await Task.WhenAll(
            RunTimelineAsync("first"),
            RunTimelineAsync("second"),
            RunTimelineAsync("third"));

        watcher.Ingest.Flush();

        Assert.Equal(3, watcher.Store.GetValue(state => state.Runs).Count);
        Assert.Equal(3, watcher.Store.GetValue(state => state.Runs).Select(run => run.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task ARunWithNoBreakpointPolicyIsNeverHeld()
    {
        // Every step asks permission before it runs, so "pause by default" would stall each step
        // until the producer's ten-minute wait expired. This is the guard for that.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        Task run = RunTimelineAsync("unheld");

        Assert.True(await CompletesAsync(run, Patience), "The run never finished, so a step was held with no policy asking for it.");
    }

    [Fact]
    public async Task AskingToContinueIsNotRecordedAsAPause()
    {
        // A breakpoint request is a question, not an event. Forwarding every one would mark each
        // step as paused for as long as it took the next transition to clear it.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        await RunTimelineAsync("quiet");
        watcher.Ingest.Flush();

        Assert.DoesNotContain(watcher.Kinds, kind => kind == PipeSignalKind.BreakpointHitRequest);
        Assert.DoesNotContain(Steps(watcher), step => step.IsWaitingAtBreakpoint);
    }

    [Fact]
    public async Task AHeldRunWaitsUntilItIsReleased()
    {
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        int asked = 0;
        watcher.Source.PauseAtBreakpoint = _ => Interlocked.Increment(ref asked) == 1;

        Task run = RunTimelineAsync("held");

        string sessionId = await WaitForPausedSessionAsync(watcher);
        Assert.False(run.IsCompleted, "The run continued past a breakpoint that was supposed to hold it.");

        Assert.True(await watcher.Source.ContinueAsync(sessionId));
        Assert.True(await CompletesAsync(run, Patience), "The run did not resume after being released.");
    }

    [Fact]
    public async Task ReleasingOneHeldRunLeavesTheOtherHeld()
    {
        // Two runs pausing at once is ordinary for a parallel suite. The host this replaces threw
        // outright — "Only one active breakpoint is supported at a time" — so the second run's pause
        // took down the connection instead of waiting its turn.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        ConcurrentDictionary<string, bool> heldOnce = new(StringComparer.Ordinal);
        watcher.Source.PauseAtBreakpoint = request => heldOnce.TryAdd(request.SessionId, true);

        Task first = RunTimelineAsync("one");
        Task second = RunTimelineAsync("two");

        await WaitForAsync(() => watcher.PausedSessions.Count == 2, "Both runs should have paused.");

        string released = watcher.PausedSessions.First();
        Assert.True(await watcher.Source.ContinueAsync(released));

        Task releasedRun = await Task.WhenAny(first, second);
        Assert.True(await CompletesAsync(releasedRun, Patience), "The released run did not resume.");

        Task stillHeld = ReferenceEquals(releasedRun, first) ? second : first;
        Assert.False(stillHeld.IsCompleted, "Releasing one run also released the other.");

        string remaining = watcher.PausedSessions.Single(session => !string.Equals(session, released, StringComparison.Ordinal));
        Assert.True(await watcher.Source.ContinueAsync(remaining));
        Assert.True(await CompletesAsync(stillHeld, Patience), "The second run did not resume when it was released.");
    }

    [Fact]
    public async Task ContinuingARunThatIsNotHeldReportsThat()
    {
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        Assert.False(await watcher.Source.ContinueAsync("no-such-session"));
    }

    [Fact]
    public async Task CancellingTellsTheRunToStop()
    {
        // The run stops itself and unwinds through cleanup. Killing the host would be easier here
        // and would strand exactly the resources cleanup exists to release.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        CancellationRecorder waiting = new();

        Timeline timeline = Timeline.Create()
            .Trigger(new WaitForCancellationStep(waiting))
            .Name("cancelled")
            .Build();

        Task run = timeline.SetupRun().RunAsync();

        Assert.True(waiting.Started.Wait(Patience), "The step never started, so there was nothing to cancel.");

        string sessionId = await WaitForAttachedSessionAsync(watcher);
        Assert.True(await watcher.Source.CancelAsync(sessionId, "The test asked for it."));

        Assert.True(await CompletesAsync(run, Patience), "The run did not finish after being cancelled.");

        // Waited for rather than asserted outright. The runner abandons a step's task the moment the
        // run's token trips, so the run reports finished while the step is still unwinding — reading
        // the flag the instant the run returns is a race the transport has nothing to do with.
        await WaitForAsync(() => waiting.ObservedCancellation, "The step never observed the run's cancellation token.");
    }

    [Fact]
    public async Task ARunThatDisappearsIsReportedRatherThanLeftHanging()
    {
        // What a killed test host looks like from here. The run is on screen and simply stops, which
        // is otherwise indistinguishable from one that is merely slow.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        using (NamedPipeClientStream client = new(".", scope.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await client.ConnectAsync((int)Patience.TotalMilliseconds);
            byte[] frame = DebugEnvelopeCodec.EncodeFrame(DebugEnvelopeCodec.Wrap(Init("abandoned"), 1));
            await client.WriteAsync(frame, 0, frame.Length);
            await client.FlushAsync();

            await WaitForAsync(() => watcher.Source.AttachedRunCount == 1, "The run never attached.");
        }

        await WaitForAsync(
            () => watcher.Notices.Any(entry => entry.Severity != FeedSeverity.Info && entry.SessionId == "abandoned"),
            "A run that vanished mid-flight was not reported.");
    }

    [Fact]
    public async Task AConsumerThatThrowsDoesNotTakeDownTheRun()
    {
        // A bug in the consumer must not cost the run its connection. The run had nothing to do
        // with it, and losing the stream would hide everything it did next.
        using PipeScope scope = new();
        using Watcher watcher = new(scope.PipeName);

        int seen = 0;
        watcher.Source.EnvelopeReceived += _ =>
        {
            if (Interlocked.Increment(ref seen) == 1)
                throw new InvalidOperationException("Simulated consumer failure.");
        };

        await RunTimelineAsync("survives");
        watcher.Ingest.Flush();

        Assert.True(seen > 1, "The connection stopped after the consumer threw.");
        Assert.True(watcher.Store.GetValue(state => state.ActiveRun).IsFinished);
        Assert.Contains(watcher.Notices, entry => entry.Severity == FeedSeverity.Error);
    }

    private static IEnumerable<StepNode> Steps(Watcher watcher)
        => watcher.Store.GetValue(state => state.ActiveRun).Stages.SelectMany(stage => stage.Steps);

    private static async Task<string> WaitForAttachedSessionAsync(Watcher watcher)
    {
        await WaitForAsync(() => watcher.Source.AttachedRunCount > 0, "No run attached to the pipe.");
        return watcher.Source.AttachedSessionIds.First();
    }

    private static async Task<string> WaitForPausedSessionAsync(Watcher watcher)
    {
        await WaitForAsync(() => watcher.PausedSessions.Count > 0, "No run paused at a breakpoint.");
        return watcher.PausedSessions.First();
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(15);
        }

        Assert.Fail(because);
    }

    private static async Task<bool> CompletesAsync(Task task, TimeSpan timeout)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(finished, task))
            return false;

        // Observed rather than swallowed: a run that failed should fail the test with its own
        // exception, not with a timeout further down.
        await task;
        return true;
    }

    private static Task RunTimelineAsync(string label)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new NoopStep())
            .Name(label)
            .Build();

        return timeline.SetupRun().RunAsync();
    }

    private static PipeInitTimelineRunSignal Init(string sessionId) => new()
    {
        SessionId = sessionId,
        Name = "Abandoned run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    };

    /// <summary>
    /// Points the framework's transport at a pipe of this test's own.
    /// </summary>
    /// <remarks>
    /// The mode is pinned on rather than left to the probe: the UI is definitionally running here,
    /// so a run should wait for it instead of racing it. It is switched off again on the way out so
    /// a later test that does not want a debugger does not pay the connect timeout looking for a
    /// server that has gone.
    /// </remarks>
    private sealed class PipeScope : IDisposable
    {
        internal string PipeName { get; } = "TestFrameworkDebugUITests_" + Guid.NewGuid().ToString("N");

        internal PipeScope()
        {
            Environment.SetEnvironmentVariable("TESTFRAMEWORK_DEBUG_PIPE_NAME", PipeName);
            TestFrameworkDebugging.PipeDebuggerEnabled = true;
        }

        public void Dispose()
        {
            TestFrameworkDebugging.PipeDebuggerEnabled = false;
            Environment.SetEnvironmentVariable("TESTFRAMEWORK_DEBUG_PIPE_NAME", null);
        }
    }

    /// <summary>A source, a store and the wiring between them, as the shell will assemble them.</summary>
    private sealed class Watcher : IDisposable
    {
        private readonly List<PipeSignalKind> kinds = [];
        private readonly List<FeedEntry> notices = [];
        private readonly ConcurrentDictionary<string, bool> paused = new(StringComparer.Ordinal);
        private readonly object gate = new();

        internal Watcher(string pipeName)
        {
            Store = StateStore<MainState>.Create().AddReducer(new MainReducer()).Build();
            Ingest = new RunIngestService(Store, TimeSpan.FromMilliseconds(10));
            Source = new PipeRunEventSource(pipeName);

            Source.EnvelopeReceived += Record;
            Source.EnvelopeReceived += Ingest.Accept;
            Source.Notice += notice =>
            {
                lock (gate) notices.Add(notice);
            };

            Source.Start();
        }

        internal StateStore<MainState> Store { get; }

        internal RunIngestService Ingest { get; }

        internal PipeRunEventSource Source { get; }

        internal IReadOnlyList<PipeSignalKind> Kinds
        {
            get { lock (gate) return [.. kinds]; }
        }

        internal IReadOnlyList<FeedEntry> Notices
        {
            get { lock (gate) return [.. notices]; }
        }

        /// <summary>The sessions the source reported as held.</summary>
        internal IReadOnlyList<string> PausedSessions => [.. paused.Keys];

        public void Dispose()
        {
            Source.Dispose();
            Ingest.Dispose();
            Store.Dispose();
        }

        private void Record(DebugEnvelope envelope)
        {
            lock (gate) kinds.Add(envelope.Kind);

            if (envelope.Kind == PipeSignalKind.BreakpointHitRequest)
                paused.TryAdd(envelope.SessionId, true);
        }
    }

    private sealed class NoopStep : Step<EmptyStepResultContext>
    {
        public override string Name => "noop";
        public override string Description => "Does nothing.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
            => Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);

        public override Step<EmptyStepResultContext> Clone() => new NoopStep().WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    /// <summary>
    /// What the step observed, kept outside the step because the runner executes a clone of it.
    /// </summary>
    private sealed class CancellationRecorder
    {
        internal ManualResetEventSlim Started { get; } = new(false);

        /// <summary>Written by the step's task, read by the test thread after the run returns.</summary>
        internal volatile bool ObservedCancellation;
    }

    /// <summary>Runs until the run is cancelled, then returns normally.</summary>
    /// <remarks>
    /// Returns rather than throwing, so the test distinguishes "the step was told to stop" from
    /// "the step failed" — cancelling a run is not supposed to look like a broken test.
    /// </remarks>
    private sealed class WaitForCancellationStep(CancellationRecorder recorder) : Step<EmptyStepResultContext>
    {
        public override string Name => "wait-for-cancellation";
        public override string Description => "Waits until the run is cancelled.";
        public override bool DoesReturn => false;

        public override async Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            recorder.Started.Set();

            DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(15, CancellationToken.None);

            recorder.ObservedCancellation = cancellationToken.IsCancellationRequested;
            return EmptyStepResultContext.Instance;
        }

        public override Step<EmptyStepResultContext> Clone() => new WaitForCancellationStep(recorder).WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }
}
