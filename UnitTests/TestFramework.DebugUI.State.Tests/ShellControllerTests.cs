using System;
using System.Collections.Generic;
using System.IO;
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
/// Covers what the window does when it is not drawing.
/// </summary>
/// <remarks>
/// The controller holds no WPF type precisely so this can exist: listing runs, switching between
/// them, and rebuilding a board from whatever record of a run survives are the behaviours most
/// likely to break, and none of them would ever be covered if they lived in a window.
/// </remarks>
[Collection(TimelineRunCollection.Name)]
public sealed class ShellControllerTests(JournalFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private string RunsDirectory => Path.Combine(fixture.Root, "runs");

    [Fact]
    public async Task RecordedRunsAreListedWithoutBeingOpened()
    {
        await RunTimelineAsync("listed");

        using Harness harness = new(RunsDirectory);
        harness.Controller.RefreshRecordedRuns();

        Assert.NotEmpty(harness.Store.GetValue(state => state.Runs));
        Assert.All(harness.Store.GetValue(state => state.Runs), run => Assert.False(run.IsLive));
    }

    [Fact]
    public async Task SelectingARecordedRunRebuildsItsBoard()
    {
        // The headline capability, driven the way the window drives it.
        await RunTimelineAsync("reopened");

        using Harness harness = new(RunsDirectory);
        harness.Controller.RefreshRecordedRuns();

        Assert.Contains("reopened", StepsOfEveryRun(harness));
    }

    [Fact]
    public async Task SwitchingBetweenRunsShowsEachOneWhole()
    {
        // Only the selected run is projected, so switching means replaying. A board still carrying
        // part of the run before it would be worse than useless.
        await RunTimelineAsync("first-run");
        await RunTimelineAsync("second-run");

        using Harness harness = new(RunsDirectory);
        harness.Controller.RefreshRecordedRuns();

        List<string[]> boards = [];

        foreach (string sessionId in SessionIds(harness))
        {
            harness.Controller.SelectRun(sessionId);
            boards.Add(StepsOfSelectedRun(harness));
        }

        // Each board carries its own run's step and nobody else's, which is the property that
        // matters: selection replays from scratch rather than adding to what was there.
        Assert.Contains(boards, steps => steps.Contains("first-run"));
        Assert.Contains(boards, steps => steps.Contains("second-run"));
        Assert.DoesNotContain(boards, steps => steps.Contains("first-run") && steps.Contains("second-run"));
    }

    [Fact]
    public async Task ARunListedTwiceIsShownOnce()
    {
        // Refreshing is something the user can do at will, and a run that appeared twice per press
        // would make the picker useless within seconds.
        await RunTimelineAsync("once");

        using Harness harness = new(RunsDirectory);

        harness.Controller.RefreshRecordedRuns();
        int afterFirst = harness.Store.GetValue(state => state.Runs).Count;

        harness.Controller.RefreshRecordedRuns();
        harness.Controller.RefreshRecordedRuns();

        Assert.Equal(afterFirst, harness.Store.GetValue(state => state.Runs).Count);
    }

    [Fact]
    public void ARunWithNoRecordSaysSoRatherThanShowingNothing()
    {
        // An empty board reads as "this run did nothing", which is a different and much more
        // alarming statement than "there is no record of it".
        using Harness harness = new(RunsDirectory);

        harness.Controller.SelectRun("never-existed");

        Assert.Contains(
            harness.Store.GetValue(state => state.Shell.Feed),
            entry => entry.Severity != FeedSeverity.Info && entry.SessionId == "never-existed");
    }

    [Fact]
    public async Task ALiveRunIsShownFromWhatItSentWhileItWasNotSelected()
    {
        // Every session is ingested but only one is projected, so an unselected run's events are
        // held until someone asks for it. Without that, opening a run that was already in flight
        // would show only what it did after being clicked.
        using PipeScope scope = new();

        // A directory with nothing in it, so there is no journal to fall back on: a board rebuilt
        // here can only have come from the events held in memory.
        string empty = Path.Combine(Path.GetTempPath(), "tf-shell-tests", Guid.NewGuid().ToString("N"));

        using Harness harness = new(empty, scope.PipeName);
        harness.Controller.Start();

        await RunTimelineAsync("watched");
        await RunTimelineAsync("ignored");

        await WaitForAsync(() => harness.Store.GetValue(state => state.Runs).Count >= 2, "Both runs should have attached.");

        Assert.Contains("watched", StepsOfEveryRun(harness));
        Assert.Contains("ignored", StepsOfEveryRun(harness));
    }

    [Fact]
    public async Task AStepAskingAboutABreakpointIsToldWhichTestItBelongsTo()
    {
        // A breakpoint is keyed by test, and this is where the test comes from: read off the run's own
        // announcement on the reader thread. Taking it from the store instead would miss the first step of
        // every run, because the store is a coalesced dispatch behind the pipe.
        using PipeScope scope = new();

        List<BreakpointQuestion> asked = [];

        using Harness harness = new(RunsDirectory, scope.PipeName);
        harness.Controller.PauseAtBreakpoint = question =>
        {
            lock (asked)
                asked.Add(question);

            return false;
        };

        harness.Controller.Start();

        await RunTimelineAsync("named");

        await WaitForAsync(
            () => { lock (asked) { return asked.Count > 0; } },
            "Every step asks before it runs, so at least one question should have arrived.");

        lock (asked)
            Assert.All(asked, question => Assert.False(string.IsNullOrWhiteSpace(question.Test)));
    }

    [Fact]
    public async Task AStepThatFailsIsReportedSoTheRunCanBeHeldBeforeTeardown()
    {
        // A run is only ever asked whether to pause before a step, so break-on-failure works by arming a
        // stop when this fires. Without it the failure is only visible in the store, which is too late and
        // only for the selected run.
        using PipeScope scope = new();

        List<StepFailureNotice> broken = [];

        using Harness harness = new(RunsDirectory, scope.PipeName);
        harness.Controller.StepEndedBadly = notice =>
        {
            lock (broken)
                broken.Add(notice);
        };

        harness.Controller.Start();

        await RunFailingTimelineAsync("broken");

        await WaitForAsync(
            () => { lock (broken) { return broken.Count > 0; } },
            "The failing step should have been reported.");

        lock (broken)
        {
            StepFailureNotice notice = broken[0];

            Assert.False(string.IsNullOrWhiteSpace(notice.SessionId));
            Assert.False(string.IsNullOrWhiteSpace(notice.Stage));
        }
    }

    [Fact]
    public async Task AStepThatPassesIsNotReportedAsBroken()
    {
        using PipeScope scope = new();

        List<StepFailureNotice> broken = [];

        using Harness harness = new(RunsDirectory, scope.PipeName);
        harness.Controller.StepEndedBadly = notice =>
        {
            lock (broken)
                broken.Add(notice);
        };

        harness.Controller.Start();

        await RunTimelineAsync("fine");

        await WaitForAsync(
            () => harness.Store.GetValue(state => state.Runs).Count > 0,
            "The run should have attached.");

        lock (broken)
            Assert.Empty(broken);
    }

    [Fact]
    public void SelectingAStepIsRememberedAndClearedWhenTheRunChanges()
    {
        using Harness harness = new(RunsDirectory);

        harness.Controller.SelectStep("Main", 3);
        Assert.Equal(3, harness.Store.GetValue(state => state.SelectedStep)?.StepId);

        harness.Controller.SelectRun("another");

        // A stage and an index mean nothing in a different run, so keeping them would open the
        // detail panel on whatever happened to sit at that index.
        Assert.Null(harness.Store.GetValue(state => state.SelectedStep));
    }

    [Fact]
    public async Task ContinuingWithNothingSelectedIsNotAnError()
    {
        using Harness harness = new(RunsDirectory);

        Assert.False(await harness.Controller.ContinueSelectedRunAsync());
    }

    /// <summary>
    /// Every step name reachable by opening each run in turn.
    /// </summary>
    /// <remarks>
    /// Runs are identified by what is on their board rather than by name, because a run's name is
    /// the test method that produced it — so every run from one test method shares it, and picking
    /// "the run called X" would pick an arbitrary one.
    /// </remarks>
    private static HashSet<string> StepsOfEveryRun(Harness harness)
    {
        HashSet<string> steps = new(StringComparer.Ordinal);

        foreach (string sessionId in SessionIds(harness))
        {
            harness.Controller.SelectRun(sessionId);
            steps.UnionWith(StepsOfSelectedRun(harness));
        }

        return steps;
    }

    private static string[] SessionIds(Harness harness)
        => [.. harness.Store.GetValue(state => state.Runs).Select(run => run.SessionId)];

    private static string[] StepsOfSelectedRun(Harness harness)
        =>
        [
            .. harness.Store.GetValue(state => state.ActiveRun).Stages
                .SelectMany(stage => stage.Steps)
                .Select(step => step.DisplayName)
        ];

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

    private static async Task RunTimelineAsync(string label)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new NoopStep())
            .Name(label)
            .Build();

        await timeline.SetupRun().RunAsync();
    }

    /// <summary>
    /// Runs a timeline whose step throws, and lets the failure be the outcome rather than an error here.
    /// </summary>
    private static async Task RunFailingTimelineAsync(string label)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ThrowingStep())
            .Name(label)
            .Build();

        try
        {
            await timeline.SetupRun().RunAsync();
        }
        catch (Exception)
        {
            // The point of the run is that it breaks. Whether the failure also surfaces to the caller is
            // Core's business and not what is under test.
        }
    }

    private sealed class ThrowingStep : Step<EmptyStepResultContext>
    {
        public override string Name => "throws";
        public override string Description => "Fails on purpose.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the step broke");

        public override Step<EmptyStepResultContext> Clone() => new ThrowingStep().WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    private sealed class Harness : IDisposable
    {
        internal Harness(string runsDirectory, string? pipeName = null)
        {
            Store = StateStore<MainState>.Create().AddReducer(new MainReducer()).Build();

            // A pipe name of this test's own, so a controller never attaches to a real UI's pipe or
            // to another test's.
            Controller = new ShellController(
                Store,
                runsDirectory,
                pipeName ?? "TestFrameworkShellTests_" + Guid.NewGuid().ToString("N"),
                TimeSpan.FromMilliseconds(10));
        }

        internal StateStore<MainState> Store { get; }

        internal ShellController Controller { get; }

        public void Dispose()
        {
            Controller.Dispose();
            Store.Dispose();
        }
    }

    private sealed class PipeScope : IDisposable
    {
        internal string PipeName { get; } = "TestFrameworkShellTests_" + Guid.NewGuid().ToString("N");

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
}
