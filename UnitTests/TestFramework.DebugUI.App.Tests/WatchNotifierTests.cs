using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reactive.Concurrency;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Settings;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// A finished run is announced as it stands once the checks its test made afterwards have arrived.
/// </summary>
public sealed class WatchNotifierTests
{
    [Fact]
    public void ARunWhoseChecksFollowItsFinishIsAnnouncedAsPassed()
    {
        string sessionId = "watch-" + Guid.NewGuid().ToString("N");
        List<RunFinishedNotice> notices = [];

        Wpf.Run(() =>
        {
            using StateStore<MainState> store = MainStore.Create().Build();
            HistoricalScheduler clock = new();
            using WatchNotifier notifier = new(store, clock) { IsHidden = true, Settings = new WatchSettings { NotifyOnFinish = true } };
            notifier.Finished += notices.Add;

            store.Dispatch(MainActions.IngestBatch, FinishedRun(sessionId));
            store.Dispatch(MainActions.IngestBatch, [LateCheck(sessionId, seq: 6)]);

            clock.AdvanceBy(WatchNotifier.SettleTime);
        });

        RunFinishedNotice notice = Assert.Single(notices);
        Assert.Equal(RunHealth.Passed, notice.Health);
    }

    [Fact]
    public void NothingIsAnnouncedBeforeTheSettleTimeIsOver()
    {
        string sessionId = "watch-" + Guid.NewGuid().ToString("N");
        List<RunFinishedNotice> notices = [];

        Wpf.Run(() =>
        {
            using StateStore<MainState> store = MainStore.Create().Build();
            HistoricalScheduler clock = new();
            using WatchNotifier notifier = new(store, clock) { IsHidden = true, Settings = new WatchSettings { NotifyOnFinish = true } };
            notifier.Finished += notices.Add;

            store.Dispatch(MainActions.IngestBatch, FinishedRun(sessionId));

            clock.AdvanceBy(WatchNotifier.SettleTime - TimeSpan.FromMilliseconds(1));
        });

        Assert.Empty(notices);
    }

    [Fact]
    public void ARunThatProvedNothingIsStillAnnouncedAsUnproven()
    {
        string sessionId = "watch-" + Guid.NewGuid().ToString("N");
        List<RunFinishedNotice> notices = [];

        Wpf.Run(() =>
        {
            using StateStore<MainState> store = MainStore.Create().Build();
            HistoricalScheduler clock = new();
            using WatchNotifier notifier = new(store, clock) { IsHidden = true, Settings = new WatchSettings { NotifyOnFinish = true } };
            notifier.Finished += notices.Add;

            store.Dispatch(MainActions.IngestBatch, FinishedRun(sessionId));

            clock.AdvanceBy(WatchNotifier.SettleTime);
        });

        RunFinishedNotice notice = Assert.Single(notices);
        Assert.Equal(RunHealth.Unproven, notice.Health);
    }

    private static ImmutableList<DebugEnvelope> FinishedRun(string sessionId) =>
    [
        DebugEnvelopeCodec.Wrap(new PipeInitTimelineRunSignal
        {
            SessionId = sessionId,
            Name = "watched",
            ProjectPath = "project.csproj",
            RunStructure = new TimelineRunStructure
            {
                Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step("check")] }],
                Variables = new Dictionary<VariableIdentifier, DebugValue>(),
                Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
            }
        }, 1),
        Transition(sessionId, DebugEntityKind.Run, null, null, DebugLifecycleState.Running, 2),
        Transition(sessionId, DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Complete, 3),
        Transition(sessionId, DebugEntityKind.Run, null, null, DebugLifecycleState.Complete, 4),
        DebugEnvelopeCodec.Wrap(new PipeTimelineRunFinishedSignal { SessionId = sessionId }, 5),
    ];

    private static DebugEnvelope Transition(string sessionId, DebugEntityKind kind, string? stage, int? stepId, DebugLifecycleState state, long seq)
        => DebugEnvelopeCodec.Wrap(new PipeEntityTransitionSignal
        {
            SessionId = sessionId,
            EntityKind = kind,
            Stage = stage,
            StepId = stepId,
            State = state
        }, seq);

    private static DebugEnvelope LateCheck(string sessionId, long seq) => DebugEnvelopeCodec.Wrap(new PipeAssertionSignal
    {
        SessionId = sessionId,
        Entry = new DebugAssertionEntry
        {
            TargetKind = DebugAssertionTargetKind.Step,
            Target = "check",
            AssertionName = "HaveCompleted",
            Arguments = [],
            Succeeded = true,
            Actual = new DebugValueDescription { Summary = "Complete", Shape = DebugValueShape.Scalar }
        }
    }, seq);

    private static DebugStepState Step(string name) => new()
    {
        Name = name,
        Description = name,
        DoesReturn = false,
        Phase = StepExecutionPhase.Act,
        Parallelization = StepParallelizationMode.Parallelizable
    };
}
