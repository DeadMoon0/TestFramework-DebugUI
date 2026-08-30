using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers coalescing incoming envelopes into batched dispatches.
/// </summary>
public class RunIngestServiceTests : IDisposable
{
    private readonly List<IDisposable> disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in disposables) disposable.Dispose();
        GC.SuppressFinalize(this);
    }

    private (StateStore<MainState> Store, RunIngestService Ingest, Counter Counter) Create(TimeSpan? window = null)
    {
        StateStore<MainState> store = MainStore.Create().Build();
        RunIngestService ingest = new(store, window ?? TimeSpan.FromMilliseconds(30));

        Counter counter = new();
        store.Bind(state => state.Runs.All).Subscribe(_ => counter.Increment());

        disposables.Add(ingest);
        disposables.Add(store);

        return (store, ingest, counter);
    }

    [Fact]
    public void ABurstBecomesOneDispatch()
    {
        // The reason batching exists: a log-heavy run emits thousands of events per second, and
        // each dispatch clones the state and notifies every binding.
        (StateStore<MainState> store, RunIngestService ingest, Counter counter) = Create();

        counter.Reset();

        ingest.Accept(Init("s1"));
        for (int i = 0; i < 200; i++)
            ingest.Accept(StepRunning("s1", at: i + 1));

        ingest.Flush();

        Assert.Equal(1, counter.Count);
        Assert.Single(store.GetValue(state => state.Runs.All));
    }

    [Fact]
    public void ABreakpointIsDeliveredWithoutWaitingForTheWindow()
    {
        // A paused run is waiting for a human. Making them wait an extra window to find out is the
        // one case where latency matters more than throughput.
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromSeconds(30));

        ingest.Accept(Init("s1"));
        ingest.Accept(Breakpoint("s1"));

        // No flush, and a window far longer than this test would wait.
        Assert.True(store.GetValue(state => state.Runs.All)[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public void ABreakpointCarriesTheEventsThatLedToItAlong()
    {
        // Flushing only the breakpoint would show a pause before the events explaining it.
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromSeconds(30));

        ingest.Accept(Init("s1"));
        ingest.Accept(StepRunning("s1", at: 1));
        ingest.Accept(Breakpoint("s1"));

        Assert.Equal(DebugLifecycleState.Running, store.GetValue(state => state.Board.ActiveRun).Stages[0].Steps[0].Lifecycle);
        Assert.True(store.GetValue(state => state.Board.ActiveRun).Stages[0].Steps[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public async Task TheWindowFlushesOnItsOwn()
    {
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromMilliseconds(20));

        ingest.Accept(Init("s1"));

        // No explicit flush: the timer is what has to deliver this.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (store.GetValue(state => state.Runs.All.Count) == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Single(store.GetValue(state => state.Runs.All));
    }

    [Fact]
    public void FlushingAnEmptyBufferDispatchesNothing()
    {
        (_, RunIngestService ingest, Counter counter) = Create();

        counter.Reset();
        ingest.Flush();
        ingest.Flush();

        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void DisposingDeliversWhatWasAlreadyAccepted()
    {
        // Events accepted have been reported as received; dropping them on shutdown would lose the
        // tail of the last run.
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromSeconds(30));

        ingest.Accept(Init("s1"));
        ingest.Dispose();

        Assert.Single(store.GetValue(state => state.Runs.All));
    }

    [Fact]
    public async Task FlushesDoNotOverlap()
    {
        // Three threads reach Flush: the timer, the pipe reader on a breakpoint, and the UI thread
        // selecting a run. Taking a batch and dispatching it has to be indivisible, or a thread
        // holding an older batch loses the race to a newer one and the reducer folds the transitions
        // backwards — a step already reported complete goes back to running.
        //
        // This pins the serialisation as this class's own contract. The store also serialises its
        // dispatches today, so the test passes without the ordering lock as well; what the lock adds
        // is that the window between taking a batch and dispatching it cannot be overtaken, which
        // needs a seam between those two acts to observe and is deliberately not one.
        StateStore<MainState> store = MainStore.Create().Build();
        RunIngestService ingest = new(store, TimeSpan.FromSeconds(30));
        disposables.Add(ingest);
        disposables.Add(store);

        ingest.Accept(Init("s1"));
        ingest.Flush();

        using ManualResetEventSlim dispatching = new(false);
        using ManualResetEventSlim release = new(false);
        int armed = 0;
        int dispatches = 0;

        // Binding notifies once with the value it already has, which is not a dispatch.
        store.Bind(state => state.Board.ActiveRun).Subscribe(_ =>
        {
            if (Volatile.Read(ref armed) == 0 || Interlocked.Increment(ref dispatches) != 1)
                return;

            // Hold the first dispatch open so the second flush has every chance to overtake it.
            dispatching.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        });

        Volatile.Write(ref armed, 1);

        Task first = Task.Run(() =>
        {
            ingest.Accept(StepRunning("s1", at: 1));
            ingest.Flush();
        });

        Assert.True(dispatching.Wait(TimeSpan.FromSeconds(5)), "the first dispatch never started");

        Task second = Task.Run(() =>
        {
            ingest.Accept(StepComplete("s1", at: 2));
            ingest.Flush();
        });

        // The second flush must not slip past the first while it is still dispatching.
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.False(second.IsCompleted, "a flush overtook one already dispatching");

        release.Set();
        await first;
        await second;

        Assert.Equal(DebugLifecycleState.Complete, store.GetValue(state => state.Board.ActiveRun).Stages[0].Steps[0].Lifecycle);
    }

    [Fact]
    public async Task ASubscriberThatFlushesDoesNotDeadlock()
    {
        // The ordering lock is held across the dispatch, so a subscriber reacting to one batch by
        // settling the state re-enters it on its own thread.
        StateStore<MainState> store = MainStore.Create().Build();
        RunIngestService ingest = new(store, TimeSpan.FromSeconds(30));
        disposables.Add(ingest);
        disposables.Add(store);

        store.Bind(state => state.Runs.All).Subscribe(_ => ingest.Flush());

        Task accepted = Task.Run(() =>
        {
            ingest.Accept(Init("s1"));
            ingest.Flush();
        });

        await Task.WhenAny(accepted, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(accepted.IsCompleted, "a subscriber flushing deadlocked the ingest path");
        Assert.Single(store.GetValue(state => state.Runs.All));
    }

    private sealed class Counter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);

        public void Reset() => Volatile.Write(ref count, 0);
    }

    private static DebugEnvelope Init(string sessionId) => DebugEnvelopeCodec.Wrap(new PipeInitTimelineRunSignal
    {
        SessionId = sessionId,
        Name = "Run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step()] }],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    }, 1);

    private static DebugEnvelope StepRunning(string sessionId, int at) => DebugEnvelopeCodec.Wrap(new PipeEntityTransitionSignal
    {
        SessionId = sessionId,
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = 0,
        State = DebugLifecycleState.Running,
        OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at)
    }, 2);

    private static DebugEnvelope StepComplete(string sessionId, int at) => DebugEnvelopeCodec.Wrap(new PipeEntityTransitionSignal
    {
        SessionId = sessionId,
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = 0,
        PreviousState = DebugLifecycleState.Running,
        State = DebugLifecycleState.Complete,
        OutcomeState = DebugLifecycleState.Complete,
        OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at)
    }, 4);

    private static DebugEnvelope Breakpoint(string sessionId) => DebugEnvelopeCodec.Wrap(new PipeBreakpointHitRequestSignal
    {
        SessionId = sessionId,
        Stage = "Main",
        StepId = 0
    }, 3);

    private static DebugStepState Step() => new()
    {
        Name = "A",
        Description = "A",
        DoesReturn = false,
        Phase = StepExecutionPhase.Act,
        Parallelization = StepParallelizationMode.Parallelizable
    };
}
