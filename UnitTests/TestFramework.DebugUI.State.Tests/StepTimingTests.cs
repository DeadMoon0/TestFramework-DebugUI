using System;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers how long a step is reported to have taken.
/// </summary>
/// <remarks>
/// The question a reader brings to a slow run is which step is costing it the time. That is only
/// answerable if a retried step reports what it actually cost, and if a step still running reports
/// nothing rather than a number that changes while being read.
/// </remarks>
public class StepTimingTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

    [Fact]
    public void AStepThatRanAndFinishedReportsTheTimeBetween()
    {
        RunGraph graph = Started();
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Complete, Start.AddMilliseconds(250), DebugLifecycleState.Running));

        Assert.Equal(TimeSpan.FromMilliseconds(250), Step(graph).Duration);
    }

    [Fact]
    public void AStepStillRunningReportsNoDurationAtAll()
    {
        // A number that grows while being looked at invites comparing two readings taken at
        // different moments, which is worse than no number.
        Assert.Null(Step(Started()).Duration);
    }

    [Fact]
    public void ARetriedStepReportsWhatItCostTheRunRatherThanItsWinningAttempt()
    {
        // The whole point. The run waited through every attempt, and reporting only the last one
        // would say a step that took ten seconds took two hundred milliseconds.
        RunGraph graph = Started();

        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Error, Start.AddSeconds(1), DebugLifecycleState.Running));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.WaitingForRetry, Start.AddSeconds(1)));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Running, Start.AddSeconds(4)));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Complete, Start.AddSeconds(5), DebugLifecycleState.Running));

        Assert.Equal(TimeSpan.FromSeconds(5), Step(graph).Duration);
    }

    [Fact]
    public void WaitingForARetryIsNotAnEnding()
    {
        // A step between attempts is still costing the run. Settling the measurement there would
        // report a fraction of the truth and then never correct it.
        RunGraph graph = Started();

        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Error, Start.AddSeconds(1), DebugLifecycleState.Running));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.WaitingForRetry, Start.AddSeconds(1)));

        // Errored then waiting: it has an ending time from the failed attempt, and picks up again.
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Running, Start.AddSeconds(3)));

        Assert.Equal(Start, Step(graph).StartedAtUtc);
    }

    [Fact]
    public void AFailedStepIsTimedLikeAnyOther()
    {
        // The slow step in a failing run is usually the interesting one.
        RunGraph graph = Started();
        graph = RunProjection.ApplyTransition(graph, Transition(DebugLifecycleState.Error, Start.AddMilliseconds(80), DebugLifecycleState.Running));

        Assert.Equal(TimeSpan.FromMilliseconds(80), Step(graph).Duration);
    }

    [Fact]
    public void ASkippedStepThatNeverRanHasNoDuration()
    {
        RunGraph graph = RunProjection.ApplyTransition(Structure(), Transition(DebugLifecycleState.Skipped, Start));

        Assert.Null(Step(graph).Duration);
    }

    private static RunGraph Started() => RunProjection.ApplyTransition(Structure(), Transition(DebugLifecycleState.Running, Start));

    private static StepNode Step(RunGraph graph) => graph.Stages.Single().Steps.Single();

    private static PipeEntityTransitionSignal Transition(DebugLifecycleState state, DateTimeOffset at, DebugLifecycleState? previous = null) => new()
    {
        SessionId = "session-1",
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = 0,
        State = state,
        PreviousState = previous,
        OccurredAtUtc = at
    };

    private static RunGraph Structure() => RunProjection.ApplyInit(new PipeInitTimelineRunSignal
    {
        SessionId = "session-1",
        Name = "timeline",
        ProjectPath = "project",
        RunStructure = new TimelineRunStructure
        {
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = string.Empty,
                    Steps =
                    [
                        new DebugStepState
                        {
                            Name = "Work",
                            Description = string.Empty,
                            Phase = StepExecutionPhase.Act,
                            Parallelization = StepParallelizationMode.Parallelizable,
                            DoesReturn = false
                        }
                    ]
                }
            ],
            Variables = new System.Collections.Generic.Dictionary<TestFramework.Core.Variables.VariableIdentifier, DebugValue>(),
            Artifacts = new System.Collections.Generic.Dictionary<TestFramework.Core.Artifacts.ArtifactIdentifier, DebugValue>()
        }
    });
}
