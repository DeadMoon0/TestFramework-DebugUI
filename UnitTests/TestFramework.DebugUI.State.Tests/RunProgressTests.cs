using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the running count kept for every session, which is what the home page lists.
/// </summary>
public class RunProgressTests : IDisposable
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
    public void ARunKnowsHowManyStepsItHasBeforeItRunsAny()
    {
        // Taken from the structure the run announces, so the home page can say "0 of 2" from the
        // start rather than a total that grows as the run goes.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First")));

        RunProgress progress = Run(store, "s1").Progress!;
        Assert.Equal(2, progress.Steps);
        Assert.Equal(2, progress.Outstanding);
    }

    [Fact]
    public void AnUnselectedRunIsStillCounted()
    {
        // The point of the whole exercise: the home page shows every run's status, and only one run
        // is ever projected into a board.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Init("s2", "Second")));
        store.Dispatch(RunActions.IngestBatch, Batch(
            Settled("s2", 0, DebugLifecycleState.Complete),
            Assertion("s2", succeeded: true)));

        Assert.Equal("s1", store.GetValue(state => state.SelectedSessionId));

        RunProgress progress = Run(store, "s2").Progress!;
        Assert.Equal(1, progress.Complete);
        Assert.Equal(1, progress.AssertionsPassed);
    }

    [Fact]
    public void AStepThatFailedAndThenPassedIsCountedOnce()
    {
        // Counters incremented per transition would report a retried step as both failed and
        // complete, and the run as having more steps than it has.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Error),
            Settled("s1", 0, DebugLifecycleState.Complete)));

        RunProgress progress = Run(store, "s1").Progress!;
        Assert.Equal(1, progress.Complete);
        Assert.Equal(0, progress.Failed);
        Assert.Equal(1, progress.Outstanding);
    }

    [Fact]
    public void StepsInDifferentStagesShareIndicesWithoutSharingCounts()
    {
        // A step id is an index within its stage, so two stages both have a step 0.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Complete),
            Settled("s1", 0, DebugLifecycleState.Complete, stage: "Cleanup")));

        Assert.Equal(2, Run(store, "s1").Progress!.Complete);
    }

    [Fact]
    public void AStepInFlightSettlesNothing()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Running),
            Settled("s1", 0, DebugLifecycleState.WaitingForRetry)));

        RunProgress progress = Run(store, "s1").Progress!;
        Assert.Equal(0, progress.Complete);
        Assert.Equal(0, progress.Failed);
        Assert.Equal(2, progress.Outstanding);
    }

    [Fact]
    public void ARunThatPassedEverythingItAssertedReadsAsPassed()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Complete),
            Assertion("s1", succeeded: true),
            Finished("s1")));

        Assert.Equal(RunHealth.Passed, Run(store, "s1").Health);
    }

    [Fact]
    public void ARunThatAssertedNothingReadsAsUnproven()
    {
        // Not as passed. Nothing threw is the most flattering reading of the evidence, not the
        // honest one, and a home page full of green for runs that checked nothing is a lie.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Complete),
            Finished("s1")));

        Assert.Equal(RunHealth.Unproven, Run(store, "s1").Health);
    }

    [Fact]
    public void AFailingAssertionFailsTheRunEvenWhenEveryStepPassed()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Complete),
            Assertion("s1", succeeded: false),
            Finished("s1")));

        Assert.Equal(RunHealth.Failed, Run(store, "s1").Health);
    }

    [Fact]
    public void ARunStillGoingReadsAsRunning()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First")));

        Assert.Equal(RunHealth.Running, Run(store, "s1").Health);
    }

    [Fact]
    public void ARunHeldAtABreakpointSaysSoRatherThanLookingBusy()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Breakpoint("s1")));

        Assert.Equal(RunHealth.Waiting, Run(store, "s1").Health);
    }

    [Fact]
    public void ARunWhoseHostDiedReadsAsAborted()
    {
        // It reported steps and then stopped without finishing, which is what a killed test host
        // looks like from here.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.AddRecordedRuns, ImmutableList.Create(new RunSummary
        {
            SessionId = "dead",
            Name = "Killed",
            IsLive = false,
            IsFinished = false,
            Progress = RunProgress.Empty with { Steps = 4 }
        }));

        Assert.Equal(RunHealth.Aborted, Run(store, "dead").Health);
    }

    [Fact]
    public void ARecordedRunNobodyOpenedClaimsNothing()
    {
        // A run listed from its sidecar has no counts until it is replayed. Reporting it as passing
        // because no failure was seen would be inventing a result out of not having looked.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.AddRecordedRuns, ImmutableList.Create(new RunSummary
        {
            SessionId = "old",
            Name = "Recorded",
            IsLive = false,
            IsFinished = true
        }));

        RunSummary run = Run(store, "old");
        Assert.Null(run.Progress);
        Assert.Equal(RunHealth.Unknown, run.Health);
    }

    [Fact]
    public void OpeningARecordedRunGivesItTheCountsItWasListedWithout()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.AddRecordedRuns, ImmutableList.Create(new RunSummary
        {
            SessionId = "s1",
            Name = "Recorded",
            IsLive = false,
            IsFinished = true
        }));

        // Selecting replays the journal, which arrives as the same envelopes the pipe carries.
        store.Dispatch(RunActions.SelectRun, "s1");
        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "Recorded"),
            Settled("s1", 0, DebugLifecycleState.Complete),
            Assertion("s1", succeeded: true),
            Finished("s1")));

        Assert.Equal(RunHealth.Passed, Run(store, "s1").Health);
    }

    [Fact]
    public void TheRunningCountAgreesWithTheBoardItOpens()
    {
        // Two paths count the same events: this one from the envelopes, the tally from the graph.
        // If they drift, a home card disagrees with the board behind it, and neither is obviously
        // the wrong one.
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(
            Init("s1", "First"),
            Settled("s1", 0, DebugLifecycleState.Complete),
            Settled("s1", 1, DebugLifecycleState.Error),
            Assertion("s1", succeeded: true),
            Assertion("s1", succeeded: false),
            Finished("s1")));

        RunProgress progress = Run(store, "s1").Progress!;
        RunTally tally = RunTally.Of(store.GetValue(state => state.ActiveRun));

        Assert.Equal(tally.Steps, progress.Steps);
        Assert.Equal(tally.Complete, progress.Complete);
        Assert.Equal(tally.Failed, progress.Failed);
        Assert.Equal(tally.Skipped, progress.Skipped);
        Assert.Equal(tally.Outstanding, progress.Outstanding);
        Assert.Equal(tally.AssertionsPassed, progress.AssertionsPassed);
        Assert.Equal(tally.AssertionsFailed, progress.AssertionsFailed);
        Assert.Equal(tally.IsValid, progress.IsValid);
    }

    [Fact]
    public void FinishingRecordsHowLongTheRunTook()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(RunActions.IngestBatch, Batch(Init("s1", "First"), Finished("s1")));

        Assert.NotNull(Run(store, "s1").FinishedAtUtc);
        Assert.NotNull(Run(store, "s1").Duration);
    }

    private static RunSummary Run(StateStore<MainState> store, string sessionId)
        => store.GetValue(state => state.Runs).Single(run => run.SessionId == sessionId);

    private static ImmutableList<DebugEnvelope> Batch(params DebugEnvelope[] envelopes) => [.. envelopes];

    private static DebugEnvelope Wrap(IPipeSignal signal) => DebugEnvelopeCodec.Wrap(signal, 1);

    private static DebugEnvelope Init(string sessionId, string name) => Wrap(new PipeInitTimelineRunSignal
    {
        SessionId = sessionId,
        Name = name,
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages =
            [
                new DebugStageState { Name = "Main", Description = "main", Steps = [Step("A"), Step("B")] }
            ],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    });

    private static DebugEnvelope Settled(string sessionId, int stepId, DebugLifecycleState state, string stage = "Main")
        => Wrap(new PipeEntityTransitionSignal
        {
            SessionId = sessionId,
            EntityKind = DebugEntityKind.Step,
            Stage = stage,
            StepId = stepId,
            State = state,
            OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
        });

    private static DebugEnvelope Assertion(string sessionId, bool succeeded) => Wrap(new PipeAssertionSignal
    {
        SessionId = sessionId,
        Entry = new DebugAssertionEntry
        {
            OccurredAtUtc = DateTimeOffset.UnixEpoch,
            AssertionName = "IsTrue",
            Succeeded = succeeded
        }
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
