using Newtonsoft.Json.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.PipeAdapter;
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

    [Fact]
    public async Task PipeSignals_AreReducedIntoCanonicalStructuredState()
    {
        using PipeTestScope scope = PipeTestScope.Create();

        MainState mainState = new MainState();
        using ReducerPipeHost host = new(mainState);
        host.Begin();
        await host.WaitUntilReadyAsync().WaitAsync(TimeSpan.FromSeconds(5));

        string sessionId = Guid.NewGuid().ToString("N");
        RunDebuggerPiped debugger = new();

        await debugger.SignalInitTimelineRunAsync(sessionId, "Structured Run", "project.csproj", CreateRunStructure());
        await debugger.SignalEntityTransitionAsync(sessionId, DebugEntityKind.Stage, "Main", null, DebugLifecycleState.Running, DebugLifecycleState.Initialized);
        await debugger.SignalEntityTransitionAsync(sessionId, DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Running, DebugLifecycleState.Initialized);
        await debugger.SignalValueUpdateAsync(sessionId, "user", DebugValueKind.Variable, "Main", 0, CreateEnvelope(DebugValueKind.Variable, "System.String", "Ada", new JObject { ["value"] = "Ada" }));
        await debugger.SignalValueUpdateAsync(sessionId, "report", DebugValueKind.Artifact, "Main", 0, CreateEnvelope(DebugValueKind.Artifact, "Artifact", "report.json", new JObject { ["reference"] = "artifact://report.json" }));
        await debugger.SignalLogEntryAsync(sessionId, new DebugLogEntry
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Level = DebugLogLevel.Information,
            EventName = "StructuredLog",
            Message = "User snapshot written",
            Lines = ["User snapshot written"],
            Stage = "Main",
            StepId = 0,
            Iteration = 1
        });
        await debugger.SignalAssertionAsync(sessionId, new DebugAssertionEntry
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            TargetKind = DebugAssertionTargetKind.Variable,
            Target = "user",
            AssertionName = "Be",
            AssertionDisplay = "Be(\"Ada\")",
            Succeeded = true,
            Expected = "Ada",
            Actual = "Ada"
        });
        await debugger.SignalEntityTransitionAsync(sessionId, DebugEntityKind.Step, "Main", 0, DebugLifecycleState.Complete, DebugLifecycleState.Running);
        await debugger.SignalEntityTransitionAsync(sessionId, DebugEntityKind.Stage, "Main", null, DebugLifecycleState.Complete, DebugLifecycleState.Running);
        await debugger.SignalTimelineRunFinishedAsync(sessionId);

        StateTestHelpers.Eventually(() => mainState.ActiveRun?.IsFinished == true, "Expected the piped run to finish.");
        StateTestHelpers.Eventually(
            () => mainState.PipeConnection.Status == PipeConnectionStatus.Disconnected && !mainState.PipeConnection.IsConnected,
            "Expected the pipe connection to observe the disconnect after the run finished.");

        RunState runState = Assert.IsType<RunState>(mainState.ActiveRun);
        StageNodeState stageState = runState.Stages["Main"];
        StepNodeState stepState = stageState.Steps["0"];
        StepAttemptState attemptState = stepState.Attempts["1"];
        AssertionEntryState assertionState = runState.Assertions["0"];

        Assert.Equal(sessionId, runState.SessionId);
        Assert.Equal(DebugLifecycleState.Complete, stageState.LifecycleState);
        Assert.Equal(DebugLifecycleState.Complete, stepState.LifecycleState);
        Assert.Equal("Ada", runState.Variables["user"].Envelope.DisplayText);
        Assert.Equal("report.json", runState.Artifacts["report"].Envelope.DisplayText);
        Assert.Equal("Ada", stepState.Outputs["Variable:user"].DisplayText);
        Assert.Equal("report.json", stepState.Outputs["Artifact:report"].DisplayText);
        Assert.Contains("User snapshot written", DebugRunStateQueries.GetDebugOut(attemptState));
        Assert.Equal("Be", assertionState.AssertionName);
        Assert.True(assertionState.Succeeded);
        Assert.Equal("Ada", assertionState.Actual);
        Assert.Equal(sessionId, mainState.PipeConnection.LastSessionId);
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
        => CreateEnvelope(DebugValueKind.Variable, "System.String", displayText, new JObject { ["value"] = displayText });

    private static DebugValueEnvelope CreateEnvelope(DebugValueKind kind, string typeName, string displayText, JObject core)
    {
        return new DebugValueEnvelope
        {
            Kind = kind,
            TypeName = typeName,
            DisplayText = displayText,
            SchemaKey = $"schema:{typeName}",
            Core = core
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

        internal override Task OnEntityTransitionAsync(EntityTransitionSignal signal) => reducer.ApplyEntityTransitionAsync(signal);
        internal override Task OnInitTimelineRunAsync(InitTimelineRunSignal signal) => reducer.ApplyInitTimelineRunAsync(signal);
        internal override Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal) => reducer.ApplyTimelineRunFinishedAsync(signal);
        internal override Task OnValueUpdateAsync(ValueUpdateSignal signal) => reducer.ApplyValueUpdateAsync(signal);
        internal override Task OnLogEntryAsync(LogEntrySignal signal) => reducer.ApplyLogEntryAsync(signal);
        internal override Task OnAssertionAsync(AssertionSignal signal) => reducer.ApplyAssertionAsync(signal);
        internal override Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal) => reducer.ApplyBreakpointHitRequestAsync(signal);
    }
}