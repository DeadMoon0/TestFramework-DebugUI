using System;
using System.Collections.Generic;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers projecting lifecycle transitions onto the run graph.
/// </summary>
public class RunProjectionTransitionTests
{
    [Fact]
    public void ARunTransitionUpdatesTheRunsOwnState()
    {
        RunGraph graph = RunProjection.ApplyTransition(Seed(), Transition(DebugEntityKind.Run, null, null, DebugLifecycleState.Running));

        Assert.Equal(DebugLifecycleState.Running, graph.Lifecycle);
    }

    [Fact]
    public void AStageTransitionUpdatesOnlyThatStage()
    {
        RunGraph graph = RunProjection.ApplyTransition(Seed(), Transition(DebugEntityKind.Stage, "Main", null, DebugLifecycleState.Running));

        Assert.Equal(DebugLifecycleState.Running, graph.Stages[0].Lifecycle);
        Assert.Equal(DebugLifecycleState.Initialized, graph.Stages[1].Lifecycle);
    }

    [Fact]
    public void StepsAreAddressedByStageAndIndex()
    {
        // Steps run in parallel, so a transition has to land on the step it names rather than on
        // whatever ran most recently.
        RunGraph graph = RunProjection.ApplyTransition(Seed(), Transition(DebugEntityKind.Step, "Main", 1, DebugLifecycleState.Running));

        Assert.Equal(DebugLifecycleState.Initialized, graph.Stages[0].Steps[0].Lifecycle);
        Assert.Equal(DebugLifecycleState.Running, graph.Stages[0].Steps[1].Lifecycle);
    }

    [Fact]
    public void EnteringRunningOpensAnAttempt()
    {
        RunGraph graph = RunProjection.ApplyTransition(Seed(), Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running));

        AttemptNode attempt = Assert.Single(graph.Stages[0].Steps[0].Attempts);
        Assert.Equal(1, attempt.Attempt);
    }

    [Fact]
    public void ARetryOpensASecondAttempt()
    {
        RunGraph graph = Seed();
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, at: 1));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.WaitingForRetry, outcome: DebugLifecycleState.Error, at: 2));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, at: 3));

        Assert.Equal(2, graph.Stages[0].Steps[0].Attempts.Count);
        Assert.Equal([1, 2], graph.Stages[0].Steps[0].Attempts.ConvertAll(a => a.Attempt));
    }

    [Fact]
    public void AWaitingStepKeepsTheOutcomeOfTheAttemptThatFailed()
    {
        // The distinction the protocol draws: Lifecycle says WaitingForRetry while Outcome still
        // says Error, so the UI can show "failed, retrying" rather than losing the reason.
        RunGraph graph = Seed();
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, at: 1));
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.WaitingForRetry, outcome: DebugLifecycleState.Error, at: 2));

        StepNode step = graph.Stages[0].Steps[0];
        Assert.Equal(DebugLifecycleState.WaitingForRetry, step.Lifecycle);
        Assert.Equal(DebugLifecycleState.Error, step.Outcome);
    }

    [Fact]
    public void AFailureLandsOnTheAttemptItEnded()
    {
        RunGraph graph = Seed();
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, at: 1));

        DebugFailureDetail failure = Failure("boom", attempt: 1);
        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Error, outcome: DebugLifecycleState.Error, at: 2, failure: failure));

        AttemptNode attempt = Assert.Single(graph.Stages[0].Steps[0].Attempts);
        Assert.Equal("boom", attempt.Failure!.Message);
    }

    [Fact]
    public void ReplayingTheSameTransitionDoesNotInventAttempts()
    {
        // The guard that keeps a replayed journal honest: without it, replaying a step that ran
        // three times would show six attempts.
        RunGraph graph = Seed();
        PipeEntityTransitionSignal running = Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, at: 1);

        graph = RunProjection.ApplyTransition(graph, running);
        graph = RunProjection.ApplyTransition(graph, running);
        graph = RunProjection.ApplyTransition(graph, running);

        Assert.Single(graph.Stages[0].Steps[0].Attempts);
    }

    [Fact]
    public void ATransitionForAnUnknownEntityIsIgnored()
    {
        // What a late attach or a truncated journal looks like. It must not take the projection down.
        RunGraph seeded = Seed();

        Assert.Equal(seeded, RunProjection.ApplyTransition(seeded, Transition(DebugEntityKind.Stage, "Nonexistent", null, DebugLifecycleState.Running)));
        Assert.Equal(seeded, RunProjection.ApplyTransition(seeded, Transition(DebugEntityKind.Step, "Main", 99, DebugLifecycleState.Running)));
        Assert.Equal(seeded, RunProjection.ApplyTransition(seeded, Transition(DebugEntityKind.Step, null, 0, DebugLifecycleState.Running)));
    }

    [Fact]
    public void AFailureWithNoOpenAttemptIsStillKept()
    {
        RunGraph graph = Seed();
        DebugFailureDetail failure = Failure("late", attempt: 2);

        graph = RunProjection.ApplyTransition(graph, Transition(DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Error, outcome: DebugLifecycleState.Error, failure: failure));

        AttemptNode attempt = Assert.Single(graph.Stages[0].Steps[0].Attempts);
        Assert.Equal("late", attempt.Failure!.Message);
        Assert.Equal(2, attempt.Attempt);
    }

    /// <summary>
    /// Builds a failure the way a consumer must: Core's Capture is internal, because capturing an
    /// exception is a producer concern. The record itself is public, which is the part a consumer
    /// needs.
    /// </summary>
    private static DebugFailureDetail Failure(string message, int attempt) => new()
    {
        ExceptionType = typeof(InvalidOperationException).FullName!,
        Message = message,
        Attempt = attempt
    };

    private static PipeEntityTransitionSignal Transition(
        DebugEntityKind kind,
        string? stage,
        int? stepId,
        DebugLifecycleState state,
        DebugLifecycleState? outcome = null,
        int at = 0,
        DebugFailureDetail? failure = null) => new()
        {
            SessionId = "session-1",
            EntityKind = kind,
            Stage = stage,
            StepId = stepId,
            State = state,
            OutcomeState = outcome,
            Failure = failure,
            OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at)
        };

    private static RunGraph Seed() => RunProjection.ApplyInit(new PipeInitTimelineRunSignal
    {
        SessionId = "session-1",
        Name = "Run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages =
            [
                new DebugStageState { Name = "Main", Description = "main", Steps = [Step("A"), Step("B")] },
                new DebugStageState { Name = "Cleanup", Description = "cleanup", Steps = [Step("C")] }
            ],
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>()
        }
    });

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
