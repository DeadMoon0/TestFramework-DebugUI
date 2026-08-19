using System;
using System.Collections.Generic;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Variables;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the policies a step declares reaching the state tree.
/// </summary>
/// <remarks>
/// New with protocol 4. Every run has always announced how many times a step may retry and how long it may
/// take; until the plan stated them as facts, neither number was on the wire, so this window could not have
/// shown them however it was written.
/// </remarks>
public class StepPolicyTests
{
    [Fact]
    public void AStepsRetryCountAndTimeoutReachTheStateTree()
    {
        RunGraph graph = RunProjection.ApplyInit(Init(new DebugStepState
        {
            Name = "Fetch",
            Description = string.Empty,
            Phase = StepExecutionPhase.Act,
            DoesReturn = true,
            Parallelization = StepParallelizationMode.DoNotParallelize,
            MaxRetries = 2,
            TimeOut = TimeSpan.FromMinutes(10),
            IgnoredExceptions = ["HttpRequestException"]
        }));

        StepPolicy policy = graph.Stages[0].Steps[0].Policy;

        Assert.Equal(2, policy.MaxRetries);
        Assert.Equal(TimeSpan.FromMinutes(10), policy.TimeOut);
        Assert.True(policy.RunsAlone);
        Assert.Equal("HttpRequestException", Assert.Single(policy.IgnoredExceptions));
        Assert.True(policy.IsStated);
    }

    [Fact]
    public void APolicyPinnedToAVariableArrivesAsThatVariablesName()
    {
        RunGraph graph = RunProjection.ApplyInit(Init(new DebugStepState
        {
            Name = "Fetch",
            Description = string.Empty,
            Phase = StepExecutionPhase.Act,
            DoesReturn = false,
            Parallelization = StepParallelizationMode.Parallelizable,
            MaxRetriesVariable = "retryBudget"
        }));

        StepPolicy policy = graph.Stages[0].Steps[0].Policy;

        Assert.Null(policy.MaxRetries);
        Assert.Equal("retryBudget", policy.MaxRetriesVariable);
        Assert.True(policy.IsStated);
    }

    [Fact]
    public void AStepThatDeclaredNothingHasNothingToState()
    {
        // Which is what keeps the panel from carrying a section reading "no retries, no timeout" for every step
        // in the run.
        RunGraph graph = RunProjection.ApplyInit(Init(new DebugStepState
        {
            Name = "Set Variable",
            Description = string.Empty,
            Phase = StepExecutionPhase.Prepare,
            DoesReturn = false,
            Parallelization = StepParallelizationMode.Parallelizable
        }));

        Assert.False(graph.Stages[0].Steps[0].Policy.IsStated);
    }

    [Fact]
    public void ADeclaredContractKeepsItsTypeAndWhetherItIsRequired()
    {
        // Both arrived with protocol 4 and both were dropped in projection, so the panel could only ever
        // list the keys — which says what a step reads and not what it expects to find there.
        RunGraph graph = RunProjection.ApplyInit(Init(new DebugStepState
        {
            Name = "Fetch",
            Description = string.Empty,
            Phase = StepExecutionPhase.Act,
            DoesReturn = false,
            Parallelization = StepParallelizationMode.Parallelizable,
            Inputs =
            [
                new DebugStepIo { Key = "orderId", Kind = StepIOKind.Variable, DeclaredType = "Int32" },
                new DebugStepIo { Key = "trace", Kind = StepIOKind.Variable, Required = false }
            ]
        }));

        StepNode step = graph.Stages[0].Steps[0];

        Assert.Equal("Int32", step.Inputs[0].DeclaredType);
        Assert.True(step.Inputs[0].Required);

        Assert.Null(step.Inputs[1].DeclaredType);
        Assert.False(step.Inputs[1].Required);
    }

    [Fact]
    public void AnEntryTheRunSaidNothingAboutIsRequired()
    {
        // Which is the protocol's own default, so the two sides cannot disagree about what silence meant.
        RunGraph graph = RunProjection.ApplyInit(Init(new DebugStepState
        {
            Name = "Fetch",
            Description = string.Empty,
            Phase = StepExecutionPhase.Act,
            DoesReturn = false,
            Parallelization = StepParallelizationMode.Parallelizable,
            Outputs = [new DebugStepIo { Key = "order", Kind = StepIOKind.Artifact }]
        }));

        Assert.True(graph.Stages[0].Steps[0].Outputs[0].Required);
    }

    private static PipeInitTimelineRunSignal Init(DebugStepState step) => new()
    {
        SessionId = "session-1",
        Name = "Run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = string.Empty, Steps = [step] }],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    };
}
