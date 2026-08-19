using System;
using System.Collections.Generic;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers breakpoint pauses and the end of a run.
/// </summary>
public class RunProjectionLifecycleTests
{
    [Fact]
    public void ABreakpointMarksTheStepItPaused()
    {
        RunGraph graph = RunProjection.ApplyBreakpointHit(Seed(), Breakpoint(stepId: 1));

        Assert.False(graph.Stages[0].Steps[0].IsWaitingAtBreakpoint);
        Assert.True(graph.Stages[0].Steps[1].IsWaitingAtBreakpoint);
    }

    [Fact]
    public void ResumingClearsThePause()
    {
        // Resuming is reported as ordinary execution rather than its own signal, so the step's next
        // transition is what releases it.
        RunGraph graph = RunProjection.ApplyBreakpointHit(Seed(), Breakpoint(stepId: 0));
        Assert.True(graph.Stages[0].Steps[0].IsWaitingAtBreakpoint);

        graph = RunProjection.ApplyTransition(graph, Running(stepId: 0));

        Assert.False(graph.Stages[0].Steps[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public void ABreakpointForAnUnknownStepIsIgnored()
    {
        RunGraph seeded = Seed();

        Assert.Equal(seeded, RunProjection.ApplyBreakpointHit(seeded, Breakpoint(stepId: 99)));
        Assert.Equal(seeded, RunProjection.ApplyBreakpointHit(seeded, Breakpoint(stepId: 0, stage: "Nonexistent")));
    }

    [Fact]
    public void ReplayingABreakpointDoesNotChangeTheGraph()
    {
        RunGraph once = RunProjection.ApplyBreakpointHit(Seed(), Breakpoint(stepId: 0));
        RunGraph twice = RunProjection.ApplyBreakpointHit(once, Breakpoint(stepId: 0));

        Assert.Same(once, twice);
    }

    [Fact]
    public void FinishingMarksTheRunAsHavingStopped()
    {
        RunGraph graph = RunProjection.ApplyRunFinished(Seed(), Finished());

        Assert.True(graph.IsFinished);
    }

    [Fact]
    public void FinishingDoesNotOverwriteTheRunsOutcome()
    {
        // The outcome arrived earlier as its own transition. Finish records only that the stream
        // ended, so a failed run must not read as merely "finished".
        RunGraph graph = Seed();
        graph = RunProjection.ApplyTransition(graph, new PipeEntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Run,
            State = DebugLifecycleState.Error
        });

        graph = RunProjection.ApplyRunFinished(graph, Finished());

        Assert.True(graph.IsFinished);
        Assert.Equal(DebugLifecycleState.Error, graph.Lifecycle);
    }

    [Fact]
    public void FinishingReleasesAnyStepStillShownAsPaused()
    {
        // Nothing is waiting on it once the producer has gone, and leaving it paused would show a
        // completed run with a step apparently still waiting for input.
        RunGraph graph = RunProjection.ApplyBreakpointHit(Seed(), Breakpoint(stepId: 0));

        graph = RunProjection.ApplyRunFinished(graph, Finished());

        Assert.False(graph.Stages[0].Steps[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public void ARunThatNeverFinishesStaysUnfinished()
    {
        // This is what distinguishes an aborted run from a completed one: the producer disappeared,
        // so the finish signal never arrived.
        RunGraph graph = RunProjection.ApplyTransition(Seed(), Running(stepId: 0));

        Assert.False(graph.IsFinished);
    }

    [Fact]
    public void ReplayingTheFinishSignalIsANoOp()
    {
        RunGraph once = RunProjection.ApplyRunFinished(Seed(), Finished());
        RunGraph twice = RunProjection.ApplyRunFinished(once, Finished());

        Assert.Same(once, twice);
    }

    private static PipeBreakpointHitRequestSignal Breakpoint(int stepId, string stage = "Main") => new()
    {
        SessionId = "session-1",
        Stage = stage,
        StepId = stepId
    };

    private static PipeTimelineRunFinishedSignal Finished() => new() { SessionId = "session-1" };

    private static PipeEntityTransitionSignal Running(int stepId) => new()
    {
        SessionId = "session-1",
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = stepId,
        State = DebugLifecycleState.Running,
        OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
    };

    private static RunGraph Seed() => RunProjection.ApplyInit(new PipeInitTimelineRunSignal
    {
        SessionId = "session-1",
        Name = "Run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step("A"), Step("B")] }],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    });

    private static DebugStepState Step(string name) => new()
    {
        Name = name,
        Description = name,
        DoesReturn = false,
        Phase = StepExecutionPhase.Act,
        Parallelization = StepParallelizationMode.Parallelizable
    };
}
