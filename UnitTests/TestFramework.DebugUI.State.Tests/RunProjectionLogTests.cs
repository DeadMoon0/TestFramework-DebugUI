using System;
using System.Collections.Generic;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers routing log entries onto the attempt that produced them.
/// </summary>
public class RunProjectionLogTests
{
    [Fact]
    public void ALineLandsOnTheOpenAttempt()
    {
        RunGraph graph = SeedWithAttempt();

        graph = RunProjection.ApplyLogEntry(graph, Log("hello", iteration: 1));

        LogNode line = Assert.Single(graph.Stages[0].Steps[0].Attempts[0].Logs);
        Assert.Equal("hello", line.Render());
    }

    [Fact]
    public void EachAttemptKeepsItsOwnLines()
    {
        // A step that failed twice before passing has three sets of lines. Merging them would make
        // a retry read as one long confusing execution.
        RunGraph graph = SeedWithAttempt();
        graph = RunProjection.ApplyLogEntry(graph, Log("first try", iteration: 1));
        graph = RunProjection.ApplyTransition(graph, Running(at: 2));
        graph = RunProjection.ApplyLogEntry(graph, Log("second try", iteration: 2));

        StepNode step = graph.Stages[0].Steps[0];
        Assert.Equal(2, step.Attempts.Count);
        Assert.Equal("first try", Assert.Single(step.Attempts[0].Logs).Render());
        Assert.Equal("second try", Assert.Single(step.Attempts[1].Logs).Render());
    }

    [Fact]
    public void AnEntryWithoutAnIterationGoesToTheCurrentAttempt()
    {
        RunGraph graph = SeedWithAttempt();
        graph = RunProjection.ApplyTransition(graph, Running(at: 2));

        graph = RunProjection.ApplyLogEntry(graph, Log("unscoped", iteration: null));

        Assert.Empty(graph.Stages[0].Steps[0].Attempts[0].Logs);
        Assert.Single(graph.Stages[0].Steps[0].Attempts[1].Logs);
    }

    [Fact]
    public void RepeatedTextIsKeptBecauseRunsLegitimatelyRepeatThemselves()
    {
        // A poll loop reporting "still waiting" produces the same text many times, and every one of
        // those lines is real. Only an immediate redelivery of the identical entry is dropped.
        RunGraph graph = SeedWithAttempt();
        graph = RunProjection.ApplyLogEntry(graph, Log("still waiting", iteration: 1, at: 1));
        graph = RunProjection.ApplyLogEntry(graph, Log("still waiting", iteration: 1, at: 2));
        graph = RunProjection.ApplyLogEntry(graph, Log("still waiting", iteration: 1, at: 3));

        Assert.Equal(3, graph.Stages[0].Steps[0].Attempts[0].Logs.Count);
    }

    [Fact]
    public void ReplayingTheSameEntryDoesNotDuplicateIt()
    {
        RunGraph graph = SeedWithAttempt();
        PipeLogEntrySignal entry = Log("once", iteration: 1, at: 1);

        graph = RunProjection.ApplyLogEntry(graph, entry);
        graph = RunProjection.ApplyLogEntry(graph, entry);

        Assert.Single(graph.Stages[0].Steps[0].Attempts[0].Logs);
    }

    [Fact]
    public void AnEntryThatNamesNoStepIsDropped()
    {
        // There is nowhere correct to put it, and inventing a home would misattribute it.
        RunGraph graph = SeedWithAttempt();

        RunGraph unchanged = RunProjection.ApplyLogEntry(graph, Log("orphan", iteration: 1, stage: null, stepId: null));

        Assert.Equal(graph, unchanged);
    }

    [Fact]
    public void AnEntryForAStepWithNoAttemptIsDropped()
    {
        // Logs belong to an attempt, so one arriving before the step reported starting has no home.
        RunGraph graph = Seed();

        Assert.Equal(graph, RunProjection.ApplyLogEntry(graph, Log("early", iteration: 1)));
    }

    [Fact]
    public void AnEntryForAnUnknownIterationIsDropped()
    {
        RunGraph graph = SeedWithAttempt();

        Assert.Equal(graph, RunProjection.ApplyLogEntry(graph, Log("future", iteration: 9)));
    }

    [Fact]
    public void SeverityIsCarriedThrough()
    {
        RunGraph graph = SeedWithAttempt();

        graph = RunProjection.ApplyLogEntry(graph, Log("bad", iteration: 1, level: DebugLogLevel.Error));

        Assert.Equal(DebugLogLevel.Error, graph.Stages[0].Steps[0].Attempts[0].Logs[0].Level);
    }

    private static PipeLogEntrySignal Log(
        string message,
        int? iteration,
        string? stage = "Main",
        int? stepId = 0,
        int at = 0,
        DebugLogLevel level = DebugLogLevel.Information) => new()
        {
            SessionId = "session-1",
            Entry = new DebugLogEntry
            {
                OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at),
                Level = level,
                Template = message,
                Stage = stage,
                StepId = stepId,
                Iteration = iteration
            }
        };

    private static PipeEntityTransitionSignal Running(int at) => new()
    {
        SessionId = "session-1",
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = 0,
        State = DebugLifecycleState.Running,
        OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at)
    };

    private static RunGraph SeedWithAttempt() => RunProjection.ApplyTransition(Seed(), Running(at: 1));

    private static RunGraph Seed() => RunProjection.ApplyInit(new PipeInitTimelineRunSignal
    {
        SessionId = "session-1",
        Name = "Run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step("A")] }],
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
