using Newtonsoft.Json.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Tests.Support;

namespace TestFramework.DebugUI.Tests;

[Collection("WpfHost")]
public sealed class PipeReducerIntegrationTests
{
    public PipeReducerIntegrationTests()
    {
        StateTestHelpers.EnsureDispatcherInitialized();
    }

    [Fact]
    public async Task ParallelPipeSignals_AreMappedWithoutStateCorruption()
    {
        using PipeTestScope scope = PipeTestScope.Create();

        MainState mainState = new MainState();
        using ReducerPipeHost host = new ReducerPipeHost(mainState);
        host.Begin();
        await host.WaitUntilReadyAsync().WaitAsync(TimeSpan.FromSeconds(5));

        string sessionId = Guid.NewGuid().ToString("N");
        RunDebuggerPiped debugger = new RunDebuggerPiped();

        await debugger.SignalInitTimelineRunAsync(sessionId, "Parallel Run", "project.csproj", CreateRunStructure());
        await debugger.SignalEntityTransitionAsync(sessionId, DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, DebugLifecycleState.Initialized);

        Task[] parallelSignals = Enumerable.Range(0, 24)
            .Select(index => debugger.SignalValueUpdateAsync(
                sessionId,
                $"value-{index}",
                DebugValueKind.Variable,
                "Main",
                0,
                CreateEnvelope($"payload-{index}")))
            .Concat(Enumerable.Range(0, 12).Select(index => debugger.SignalLogEntryAsync(
                sessionId,
                new DebugLogEntry
                {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    Level = DebugLogLevel.Information,
                    EventName = "ParallelLog",
                    Message = $"log-{index}",
                    Lines = [$"log-{index}"],
                    Stage = "Main",
                    StepId = 0,
                    Iteration = 1
                })))
            .ToArray();

        await Task.WhenAll(parallelSignals);
        await debugger.SignalTimelineRunFinishedAsync(sessionId);

        StateTestHelpers.Eventually(() => mainState.ActiveRun?.IsFinished == true, "Expected the piped run to finish.");
        StateTestHelpers.Eventually(
            () => mainState.PipeConnection.Status == PipeConnectionStatus.Disconnected && !mainState.PipeConnection.IsConnected,
            "Expected the pipe connection to observe the disconnect after the run finished.");

        RunState runState = Assert.IsType<RunState>(mainState.ActiveRun);
        StepNodeState stepState = runState.Stages["Main"].Steps["0"];
        StepAttemptState attemptState = stepState.Attempts["1"];

        Assert.Equal(PipeConnectionStatus.Disconnected, mainState.PipeConnection.Status);
        Assert.False(mainState.PipeConnection.IsConnected);
        Assert.Equal(sessionId, mainState.PipeConnection.LastSessionId);
        Assert.Equal(24, runState.Variables.Count);
        Assert.Equal(24, stepState.Outputs.Count);
        Assert.Equal(1, stepState.AttemptCount);
        Assert.True(attemptState.LogEntries.Count >= 37);
        Assert.Equal(24, runState.Variables.Values.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Piped Debugger Attached", mainState.PipeConnection.DebugInfo);
        Assert.Contains("Piped Debugger Dettached (Run completed.)", mainState.PipeConnection.DebugInfo);
    }

    private static TimelineRunStructure CreateRunStructure()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<TestFramework.Core.Variables.VariableIdentifier, TestFramework.Core.Debugger.VariableState>(),
            Artifacts = new Dictionary<TestFramework.Core.Artifacts.ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Main stage",
                    Steps =
                    [
                        new DebugStepState
                        {
                            Name = "Step 1",
                            Description = "Parallel step",
                            DoesReturn = true,
                            ErrorHandlingOptions = new ErrorHandlingOptions(),
                            ExecutionOptions = new ExecutionOptions(),
                            IOContract = new StepIOContract(),
                            Phase = StepExecutionPhase.Act,
                            LabelOptions = new LabelOptions(),
                            RetryOptions = new RetryOptions(),
                            TimeOutOptions = new TimeOutOptions()
                        }
                    ]
                }
            ]
        };
    }

    private static DebugValueEnvelope CreateEnvelope(string displayText)
    {
        return new DebugValueEnvelope
        {
            Kind = DebugValueKind.Variable,
            TypeName = "System.String",
            DisplayText = displayText,
            SchemaKey = "variable/string",
            Core = new JObject { ["value"] = displayText }
        };
    }

    private sealed class ReducerPipeHost : RunDebuggerHostPiped
    {
        private readonly DebugRunStateReducer reducer;

        public ReducerPipeHost(MainState state)
        {
            reducer = new DebugRunStateReducer(state);
        }

        protected override Task OnPipeServerReadyAsync()
        {
            return reducer.ApplyPipeServerReadyAsync(PipeStreamController.GetPipeName());
        }

        protected override Task OnPipeConnectionAttachedAsync()
        {
            return reducer.ApplyPipeConnectionAttachedAsync(PipeStreamController.GetPipeName());
        }

        protected override Task OnPipeConnectionDetachedAsync(string reason)
        {
            return reducer.ApplyPipeConnectionDetachedAsync(reason);
        }

        public override Task OnEntityTransitionAsync(EntityTransitionSignal signal) => reducer.ApplyEntityTransitionAsync(signal);
        public override Task OnInitTimelineRunAsync(InitTimelineRunSignal signal) => reducer.ApplyInitTimelineRunAsync(signal);
        public override Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal) => reducer.ApplyTimelineRunFinishedAsync(signal);
        public override Task OnValueUpdateAsync(ValueUpdateSignal signal) => reducer.ApplyValueUpdateAsync(signal);
        public override Task OnLogEntryAsync(LogEntrySignal signal) => reducer.ApplyLogEntryAsync(signal);
        public override Task OnAssertionAsync(AssertionSignal signal) => reducer.ApplyAssertionAsync(signal);
        public override Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal) => reducer.ApplyBreakpointHitRequestAsync(signal);
    }
}