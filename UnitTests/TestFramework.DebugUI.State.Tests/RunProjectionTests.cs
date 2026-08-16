using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers projecting a run's structure snapshot into the graph the board renders.
/// </summary>
public class RunProjectionTests
{
    [Fact]
    public void TheWholeBoardIsLaidOutBeforeAnythingRuns()
    {
        // Core sends the full structure up front, so the board can show every stage and step as
        // pending rather than growing a row at a time as events arrive.
        RunGraph graph = RunProjection.ApplyInit(CreateInit());

        Assert.Equal(2, graph.Stages.Count);
        Assert.Equal("Main Stage", graph.Stages[0].Name);
        Assert.Equal(2, graph.Stages[0].Steps.Count);
        Assert.Single(graph.Stages[1].Steps);
    }

    [Fact]
    public void StepsAreIdentifiedByIndexWithinTheirStage()
    {
        // Every later signal addresses a step by (stage, index). Names repeat freely, so the index
        // is the only thing that identifies it.
        RunGraph graph = RunProjection.ApplyInit(CreateInit());

        Assert.Equal([0, 1], graph.Stages[0].Steps.Select(step => step.StepId));
        Assert.Equal([0], graph.Stages[1].Steps.Select(step => step.StepId));
    }

    [Fact]
    public void EveryStepStartsPending()
    {
        RunGraph graph = RunProjection.ApplyInit(CreateInit());

        Assert.All(graph.Stages.SelectMany(stage => stage.Steps), step =>
        {
            Assert.Equal(DebugLifecycleState.Initialized, step.Lifecycle);
            Assert.Null(step.Outcome);
            Assert.Empty(step.Attempts);
        });
    }

    [Fact]
    public void DeclaredIOBecomesTheGraphsEdges()
    {
        // The declared contract, not observed values — which is what lets the board draw
        // connections before the run has produced anything.
        RunGraph graph = RunProjection.ApplyInit(CreateInit());

        StepNode consumer = graph.Stages[0].Steps[1];
        Assert.Equal(["orderId"], consumer.Inputs);
        Assert.Equal(["receipt"], consumer.Outputs);
    }

    [Fact]
    public void SeededValuesAreProjectedWithTheirRendererKey()
    {
        RunGraph graph = RunProjection.ApplyInit(CreateInit());

        ValueNode variable = graph.Variables["orderId"];
        Assert.Equal("42", variable.DisplayText);
        Assert.Equal("tf.variable:System.Int32", variable.SchemaKey);

        // The schema key, not the CLR type, is what selects the artifact's icon and inspector.
        ArtifactNode artifact = graph.Artifacts["receipt"];
        Assert.Equal("tf.artifact.sql.row", artifact.SchemaKey);
    }

    [Fact]
    public void ProjectingTheSameSignalTwiceYieldsTheSameGraph()
    {
        // Idempotence matters because a replayed journal and a reconnecting transport both redeliver
        // the init signal, and neither may double the board.
        PipeInitTimelineRunSignal signal = CreateInit();

        RunGraph first = RunProjection.ApplyInit(signal);
        RunGraph second = RunProjection.ApplyInit(signal);

        Assert.Equal(first.Stages.Count, second.Stages.Count);
        Assert.Equal(
            first.Stages.SelectMany(s => s.Steps).Select(s => s.StepId),
            second.Stages.SelectMany(s => s.Steps).Select(s => s.StepId));
        Assert.Equal(first.Variables.Count, second.Variables.Count);
    }

    [Fact]
    public void AStepWithoutADeclaredContractProjectsWithNoEdges()
    {
        RunGraph graph = RunProjection.ApplyInit(CreateInit());

        Assert.Empty(graph.Stages[1].Steps[0].Inputs);
        Assert.Empty(graph.Stages[1].Steps[0].Outputs);
    }

    private static PipeInitTimelineRunSignal CreateInit()
    {
        StepIOContract producerContract = new();
        producerContract.Outputs.Add(new StepIOEntry("orderId", StepIOKind.Variable));

        StepIOContract consumerContract = new();
        consumerContract.Inputs.Add(new StepIOEntry("orderId", StepIOKind.Variable));
        consumerContract.Outputs.Add(new StepIOEntry("receipt", StepIOKind.Artifact));

        return new PipeInitTimelineRunSignal
        {
            SessionId = "session-1",
            Name = "Sample Run",
            ProjectPath = "project.csproj",
            RunStructure = new TimelineRunStructure
            {
                Stages =
                [
                    new DebugStageState
                    {
                        Name = "Main Stage",
                        Description = "main",
                        Steps =
                        [
                            CreateStep("Produce", producerContract),
                            CreateStep("Consume", consumerContract)
                        ]
                    },
                    new DebugStageState
                    {
                        Name = "Cleanup Stage",
                        Description = "cleanup",
                        Steps = [CreateStep("Tidy", new StepIOContract())]
                    }
                ],
                Variables = new Dictionary<VariableIdentifier, VariableState>
                {
                    [new VariableIdentifier("orderId")] = new VariableState
                    {
                        Key = "orderId",
                        Envelope = new DebugValueEnvelope
                        {
                            Kind = DebugValueKind.Variable,
                            TypeName = "System.Int32",
                            DisplayText = "42",
                            SchemaKey = "tf.variable:System.Int32"
                        }
                    }
                },
                Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>
                {
                    [new ArtifactIdentifier("receipt")] = new TestFramework.Core.Debugger.ArtifactState
                    {
                        Key = "receipt",
                        Envelope = new DebugValueEnvelope
                        {
                            Kind = DebugValueKind.Artifact,
                            TypeName = "SqlRow",
                            DisplayText = "receipt row",
                            SchemaKey = "tf.artifact.sql.row"
                        }
                    }
                }
            }
        };
    }

    private static DebugStepState CreateStep(string name, StepIOContract contract) => new()
    {
        Name = name,
        Description = name,
        DoesReturn = false,
        ErrorHandlingOptions = new ErrorHandlingOptions(),
        ExecutionOptions = new ExecutionOptions(),
        IOContract = contract,
        Phase = StepExecutionPhase.Act,
        LabelOptions = new LabelOptions(),
        RetryOptions = new RetryOptions(),
        TimeOutOptions = new TimeOutOptions()
    };
}
