using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Lays out a run an actual timeline produced.
/// </summary>
/// <remarks>
/// The other tests build their graphs by hand, which covers the shapes but agrees with itself about
/// what a real one looks like. This takes the structure straight out of a run, so a layer that never
/// arrives, or an IO contract that projects to nothing, shows up here as a board that says the wrong
/// thing rather than as a board nobody notices is wrong.
/// </remarks>
public sealed class RealRunLayoutTests
{
    [Fact]
    public async Task ARealRunLaysOutWithItsStepsAndTheValuesBetweenThem()
    {
        StructureRecordingDebugger debugger = new();

        Timeline timeline = Timeline.Create()
            .Trigger(new ProducingStep())
            .Name("produce")
            .Trigger(new ConsumingStep())
            .Name("consume")
            .Build();

        await timeline.SetupRun(new DebuggerServiceProvider(debugger)).RunAsync();

        RunGraph graph = RunProjection.ApplyInit(new PipeInitTimelineRunSignal
        {
            SessionId = "session",
            Name = "run",
            ProjectPath = "project.csproj",
            RunStructure = debugger.Structure ?? throw new InvalidOperationException("The run reported no structure.")
        });

        LayoutResult board = RunBoardLayout.Compute(graph);

        LayoutNode producer = board.Nodes.Single(node => node.Kind == LayoutNodeKind.Step && node.Id.EndsWith(ProducerStepSuffix(graph), StringComparison.Ordinal));

        LayoutEdge consumption = board.Edges.Single(edge => edge.Kind == LayoutEdgeKind.Consumption && edge.Key == "answer");
        LayoutNode consumer = board.Nodes.Single(node => node.Id == consumption.ToNodeId);

        // The consumer sits below the step it reads from. If the execution layer never made it
        // through, the two would share a row and the pipe would assert an ordering the run never had.
        Assert.Equal(producer.Id, consumption.FromNodeId);
        Assert.True(consumer.Y > producer.Bottom, "A consumer should sit below the step it reads from.");
    }

    [Fact]
    public async Task TheStagesTheFrameworkAddsAreEachTheirOwnBand()
    {
        // A run has more stages than the author wrote, and every one of them has to be drawn
        // somewhere sensible rather than merged into the stage beside it.
        StructureRecordingDebugger debugger = new();

        Timeline timeline = Timeline.Create()
            .Trigger(new ProducingStep())
            .Name("produce")
            .Build();

        await timeline.SetupRun(new DebuggerServiceProvider(debugger)).RunAsync();

        RunGraph graph = RunProjection.ApplyInit(new PipeInitTimelineRunSignal
        {
            SessionId = "session",
            Name = "run",
            ProjectPath = "project.csproj",
            RunStructure = debugger.Structure ?? throw new InvalidOperationException("The run reported no structure.")
        });

        LayoutResult board = RunBoardLayout.Compute(graph);

        LayoutNode[] bands = [.. board.Nodes.Where(node => node.Kind == LayoutNodeKind.Stage)];

        Assert.NotEmpty(bands);
        Assert.Equal(bands.Length, bands.Select(band => band.StageName).Distinct(StringComparer.Ordinal).Count());

        foreach ((LayoutNode above, LayoutNode below) in bands.OrderBy(band => band.Y).Zip(bands.OrderBy(band => band.Y).Skip(1)))
            Assert.True(above.Bottom <= below.Y, $"Stage bands '{above.StageName}' and '{below.StageName}' overlap.");
    }

    private static string ProducerStepSuffix(RunGraph graph)
    {
        StageNode stage = graph.Stages.Single(candidate => candidate.Steps.Any(step => step.DisplayName == "produce"));
        StepNode step = stage.Steps.Single(candidate => candidate.DisplayName == "produce");

        return $"{stage.Name}/{step.StepId}";
    }

    private sealed class DebuggerServiceProvider(IRunDebugger debugger) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IRunDebugger) ? debugger : null;
    }

    private sealed class StructureRecordingDebugger : IRunDebugger
    {
        public bool IsCapturing => true;

        public TimelineRunStructure? Structure { get; private set; }

        public Task SignalInitTimelineRunAsync(string sessionId, string name, string projectPath, TimelineRunStructure runStructure, TestIdentity? identity = null)
        {
            Structure = runStructure;
            return Task.CompletedTask;
        }

        public Task SignalEntityTransitionAsync(string sessionId, DebugEntityKind entityKind, string? stage, int? stepId, DebugLifecycleState state, DebugLifecycleState? previousState = null, DebugLifecycleState? outcomeState = null, DebugFailureDetail? failure = null)
            => Task.CompletedTask;

        public Task SignalValueUpdateAsync(string sessionId, string name, DebugValueKind valueKind, string? stage, int? stepId, DebugValueEnvelope value)
            => Task.CompletedTask;

        public Task SignalLogEntryAsync(string sessionId, DebugLogEntry entry) => Task.CompletedTask;

        public Task SignalAssertionAsync(string sessionId, DebugAssertionEntry entry) => Task.CompletedTask;

        public Task SignalTimelineRunFinishedAsync(string sessionId) => Task.CompletedTask;

        public Task SignalAndWaitBreakpointHitAsync(string sessionId, string stage, int stepId) => Task.CompletedTask;
    }

    private sealed class ProducingStep : Step<EmptyStepResultContext>
    {
        public override string Name => "produce";
        public override string Description => "Declares an output.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            variableStore.SetVariable(new VariableIdentifier("answer"), 42);
            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }

        public override Step<EmptyStepResultContext> Clone() => new ProducingStep().WithClonedOptions(this);

        public override void DeclareIO(StepIOContract contract)
            => contract.Outputs.Add(new StepIOEntry("answer", StepIOKind.Variable));

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    private sealed class ConsumingStep : Step<EmptyStepResultContext>
    {
        public override string Name => "consume";
        public override string Description => "Declares an input.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
            => Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);

        public override Step<EmptyStepResultContext> Clone() => new ConsumingStep().WithClonedOptions(this);

        public override void DeclareIO(StepIOContract contract)
            => contract.Inputs.Add(new StepIOEntry("answer", StepIOKind.Variable));

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }
}
