using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers applying debug events to the UI state through the store.
/// </summary>
public class MainReducerTests : IDisposable
{
    private readonly List<IDisposable> stores = [];

    public void Dispose()
    {
        foreach (IDisposable store in stores) store.Dispose();
        GC.SuppressFinalize(this);
    }

    private StateStore<MainState> CreateStore()
    {
        StateStore<MainState> store = StateStore<MainState>.Create().AddReducer(new MainReducer()).Build();
        stores.Add(store);
        return store;
    }

    [Fact]
    public void TheFirstRunSeenIsSelectedAutomatically()
    {
        // Showing the only run beats showing an empty board and asking the user to pick.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First")));

        Assert.Equal("s1", store.GetValue(state => state.SelectedSessionId));
        Assert.Single(store.GetValue(state => state.Runs));
    }

    [Fact]
    public void EverySessionIsTrackedButOnlyTheSelectedOneIsProjected()
    {
        // The property that keeps twenty parallel runs affordable: each contributes a summary, but
        // only one drags a graph through the per-dispatch clone.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Init("s2", "Second")));
        store.Dispatch(RunActions.IngestBatch, Batch(StepRunning("s2")));

        Assert.Equal(2, store.GetValue(state => state.Runs.Count));
        Assert.Equal("s1", store.GetValue(state => state.SelectedSessionId));

        // The transition belonged to the unselected run, so the rendered board is untouched.
        Assert.All(
            store.GetValue(state => state.ActiveRun).Stages.SelectMany(stage => stage.Steps),
            step => Assert.Equal(DebugLifecycleState.Initialized, step.Lifecycle));
    }

    [Fact]
    public void EventsForTheSelectedRunReachTheBoard()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), StepRunning("s1")));

        StepNode step = store.GetValue(state => state.ActiveRun).Stages[0].Steps[0];
        Assert.Equal(DebugLifecycleState.Running, step.Lifecycle);
    }

    [Fact]
    public void RunsAreListedNewestFirst()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First")));
        store.Dispatch(RunActions.IngestBatch, Batch(Init("s2", "Second")));

        Assert.Equal(["s2", "s1"], store.GetValue(state => state.Runs).Select(run => run.SessionId));
    }

    [Fact]
    public void ABreakpointBadgesTheRunThatIsWaiting()
    {
        // Tracked on the summary so the picker can show which of several parallel runs wants
        // attention without projecting them all.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Init("s2", "Second")));
        store.Dispatch(RunActions.IngestBatch, Batch(Breakpoint("s2")));

        Assert.True(store.GetValue(state => state.Runs).Single(run => run.SessionId == "s2").IsWaitingAtBreakpoint);
        Assert.False(store.GetValue(state => state.Runs).Single(run => run.SessionId == "s1").IsWaitingAtBreakpoint);
    }

    [Fact]
    public void ResumingClearsTheWaitingBadge()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Breakpoint("s1")));
        Assert.True(store.GetValue(state => state.Runs)[0].IsWaitingAtBreakpoint);

        store.Dispatch(RunActions.IngestBatch, Batch(StepRunning("s1")));

        Assert.False(store.GetValue(state => state.Runs)[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public void FinishingMarksTheRunAsNoLongerLive()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Finished("s1")));

        RunSummary run = store.GetValue(state => state.Runs)[0];
        Assert.True(run.IsFinished);
        Assert.False(run.IsLive);
    }

    [Fact]
    public void SelectingADifferentRunClearsTheBoard()
    {
        // The new run's graph is rebuilt by whoever owns its source. Leaving the old contents up
        // would show one run's steps under another run's name.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), StepRunning("s1"), Init("s2", "Second")));
        Assert.NotEmpty(store.GetValue(state => state.ActiveRun).Stages);

        store.Dispatch(RunActions.SelectRun, "s2");

        Assert.Equal("s2", store.GetValue(state => state.SelectedSessionId));
        Assert.Empty(store.GetValue(state => state.ActiveRun).Stages);
    }

    [Fact]
    public void AMalformedEnvelopeDoesNotLoseTheRunOnScreen()
    {
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), StepRunning("s1")));

        DebugEnvelope corrupt = new()
        {
            V = DebugProtocol.Version,
            SessionId = "s1",
            Seq = 99,
            AtUtc = DateTimeOffset.UnixEpoch,
            Kind = PipeSignalKind.EntityTransition,
            Payload = new Newtonsoft.Json.Linq.JObject { ["nonsense"] = true }
        };

        store.Dispatch(RunActions.IngestBatch, ImmutableList.Create(corrupt));

        Assert.NotEmpty(store.GetValue(state => state.ActiveRun).Stages);
    }

    [Fact]
    public void TheFeedIsBounded()
    {
        // The previous UI appended to an ever-growing string for the life of the process.
        StateStore<MainState> store = CreateStore();

        for (int i = 0; i < 600; i++)
            store.Dispatch(RunActions.AppendFeedEntry, new FeedEntry { Title = $"entry {i}" });

        Assert.Equal(500, store.GetValue(state => state.Shell.Feed.Count));
        Assert.Equal("entry 599", store.GetValue(state => state.Shell.Feed[^1]).Title);
    }

    [Fact]
    public void UnreadCountTracksTheFeedUntilCleared()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.AppendFeedEntry, new FeedEntry { Title = "one" });
        store.Dispatch(RunActions.AppendFeedEntry, new FeedEntry { Title = "two" });
        Assert.Equal(2, store.GetValue(state => state.Shell.UnreadFeedCount));

        store.Dispatch(RunActions.ClearUnreadFeed);
        Assert.Equal(0, store.GetValue(state => state.Shell.UnreadFeedCount));
    }

    [Fact]
    public void AnEmptyBatchIsANoOp()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, ImmutableList<DebugEnvelope>.Empty);

        Assert.Empty(store.GetValue(state => state.Runs));
    }

    private static ImmutableList<DebugEnvelope> Batch(params DebugEnvelope[] envelopes) => [.. envelopes];

    private static DebugEnvelope Wrap(IPipeSignal signal) => DebugEnvelopeCodec.Wrap(signal, 1);

    private static DebugEnvelope Init(string sessionId, string name) => Wrap(new PipeInitTimelineRunSignal
    {
        SessionId = sessionId,
        Name = name,
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step("A")] }],
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>()
        }
    });

    private static DebugEnvelope StepRunning(string sessionId) => Wrap(new PipeEntityTransitionSignal
    {
        SessionId = sessionId,
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = 0,
        State = DebugLifecycleState.Running,
        OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
    });

    private static DebugEnvelope Breakpoint(string sessionId) => Wrap(new PipeBreakpointHitRequestSignal
    {
        SessionId = sessionId,
        Stage = "Main",
        StepId = 0
    });

    private static DebugEnvelope Finished(string sessionId) => Wrap(new PipeTimelineRunFinishedSignal { SessionId = sessionId });

    private static DebugStepState Step(string name) => new()
    {
        Name = name,
        Description = name,
        DoesReturn = false,
        ErrorHandlingOptions = new ErrorHandlingOptions(),
        ExecutionOptions = new ExecutionOptions(),
        IOContract = new StepIOContract(),
        Phase = StepExecutionPhase.Act,
        LabelOptions = new LabelOptions(),
        RetryOptions = new RetryOptions(),
        TimeOutOptions = new TimeOutOptions()
    };
}
