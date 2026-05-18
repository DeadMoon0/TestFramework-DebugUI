using Newtonsoft.Json.Linq;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Tests.Support;

namespace TestFramework.DebugUI.Tests;

public class DebugRunStateReducerTests
{
    public DebugRunStateReducerTests()
    {
        StateTestHelpers.EnsureDispatcherInitialized();
    }

    [Fact]
    public async Task InitTimelineRun_CreatesRunStateWithInitialValues()
    {
        MainState mainState = new MainState();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyInitTimelineRunAsync(new InitTimelineRunSignal
        {
            SessionId = "session-1",
            Name = "Run",
            ProjectPath = "project.csproj",
            RunStructure = CreateRunStructure()
        });

        StateTestHelpers.Eventually(() => mainState.ActiveRun?.SessionId == "session-1", "Expected active run to be initialized.");
        Assert.NotNull(mainState.ActiveRun);
        Assert.Equal("Run", mainState.ActiveRun!.Name);
        Assert.True(mainState.ActiveRun.Stages.ContainsKey("Main"));
        Assert.True(mainState.ActiveRun.Variables.ContainsKey("input"));
        Assert.True(mainState.ActiveRun.Artifacts.ContainsKey("artifact"));
        Assert.Equal(DebugLifecycleState.Initialized, mainState.ActiveRun.Stages["Main"].Steps["0"].LifecycleState);
        Assert.Equal("Main stage", mainState.ActiveRun.Stages["Main"].Description);
        Assert.Equal("Step 1", mainState.ActiveRun.Stages["Main"].Steps["0"].Name);
        Assert.Equal(new[] { "layer-0" }, DebugRunStateQueries.GetExecutionLayerKeys(mainState.ActiveRun.Stages["Main"]));
        Assert.True(mainState.ActiveRun.Stages["Main"].ExecutionLayers.ContainsKey("layer-0"));
        Assert.Equal("layer-0", mainState.ActiveRun.Stages["Main"].Steps["0"].ExecutionLayerKey);
        Assert.True(mainState.ActiveRun.Stages["Main"].Steps["0"].Inputs.ContainsKey("Variable:input"));
        Assert.True(mainState.ActiveRun.Stages["Main"].Steps["0"].Inputs.ContainsKey("Artifact:artifact"));
        Assert.Empty(mainState.ActiveRun.Stages["Main"].Steps["0"].Attempts);
    }

    [Fact]
    public async Task StepRunning_PopulatesInputsAndTracksAttempts()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyValueUpdateAsync(new ValueUpdateSignal
        {
            SessionId = "session-1",
            Name = "input",
            ValueKind = DebugValueKind.Variable,
            Envelope = CreateEnvelope(DebugValueKind.Variable, "System.String", "updated", new JObject { ["value"] = "updated" })
        });

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Stage,
            Stage = "Main",
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });

        StepNodeState stepState = mainState.ActiveRun!.Stages["Main"].Steps["0"];
        Assert.Equal(1, stepState.AttemptCount);
        Assert.True(stepState.Attempts.ContainsKey("1"));
        Assert.Equal("Attempt 1", stepState.Attempts["1"].Name);
        Assert.Equal(DebugLifecycleState.Running, stepState.Attempts["1"].LifecycleState);
        Assert.True(stepState.Inputs["Variable:input"].HasValue);
        Assert.Equal("updated", stepState.Inputs["Variable:input"].DisplayText);
        Assert.True(mainState.ActiveRun.Stages["Main"].ExecutionLayers["layer-0"].IsActive);
    }

    [Fact]
    public async Task WaitingForRetry_PreservesLastAttemptOutcome()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.WaitingForRetry,
            PreviousState = DebugLifecycleState.Running,
            OutcomeState = DebugLifecycleState.Timeout
        });
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.WaitingForRetry
        });

        StepNodeState stepState = mainState.ActiveRun!.Stages["Main"].Steps["0"];
        Assert.Equal(StepState.NotRun, stepState.State);
        Assert.Equal(DebugLifecycleState.Running, stepState.LifecycleState);
        Assert.Equal(2, stepState.AttemptCount);
        Assert.True(stepState.Attempts.ContainsKey("1"));
        Assert.True(stepState.Attempts.ContainsKey("2"));
        Assert.Equal(DebugLifecycleState.Timeout, stepState.Attempts["1"].LifecycleState);
    }

    [Fact]
    public async Task ValueUpdate_UpdatesRunStateAndStepOutputs()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });
        await reducer.ApplyValueUpdateAsync(new ValueUpdateSignal
        {
            SessionId = "session-1",
            Name = "output",
            ValueKind = DebugValueKind.Variable,
            Stage = "Main",
            StepId = 0,
            Envelope = CreateEnvelope(DebugValueKind.Variable, "System.Int32", "42", new JObject { ["value"] = 42 })
        });

        Assert.True(mainState.ActiveRun!.Variables.ContainsKey("output"));
        Assert.True(mainState.ActiveRun.Stages["Main"].Steps["0"].Outputs.ContainsKey("Variable:output"));
        Assert.True(mainState.ActiveRun.Stages["Main"].Steps["0"].Outputs["Variable:output"].HasValue);
        Assert.Contains("Set Variable (output) = 42", DebugRunStateQueries.GetDebugOut(mainState.ActiveRun.Stages["Main"].Steps["0"].Attempts["1"]));
    }

    [Fact]
    public async Task BreakpointAndFinish_ArePersistedInState()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyBreakpointHitRequestAsync(new BreakpointHitRequestSignal
        {
            SessionId = "session-1",
            Stage = "Main",
            StepId = 0
        });
        await reducer.ApplyTimelineRunFinishedAsync(new TimelineRunFinishedSignal
        {
            SessionId = "session-1"
        });

        StepNodeState stepState = mainState.ActiveRun!.Stages["Main"].Steps["0"];
        Assert.True(stepState.IsWaitingAtBreakpoint);
        Assert.Equal(1, stepState.BreakpointHitCount);
        Assert.True(mainState.ActiveRun.IsFinished);
        Assert.NotNull(mainState.ActiveRun.FinishedAtUtc);
    }

    [Fact]
    public async Task LogEntry_PopulatesRunStageAndStepBindingState()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyLogEntryAsync(new LogEntrySignal
        {
            SessionId = "session-1",
            Entry = new DebugLogEntry
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Level = DebugLogLevel.Information,
                EventName = "InformationLogEvent",
                Message = "step output",
                Lines = ["step output"],
                IndentLevel = 1,
                Stage = "Main",
                StepId = 0,
                Iteration = 1
            }
        });

        StepNodeState stepState = mainState.ActiveRun!.Stages["Main"].Steps["0"];
        Assert.True(stepState.Attempts.ContainsKey("1"));
        Assert.Equal("step output", DebugRunStateQueries.GetDebugOut(stepState.Attempts["1"]));
        Assert.NotNull(DebugRunStateQueries.GetLatestLogEntry(stepState.Attempts["1"]));
        Assert.Equal("step output", DebugRunStateQueries.GetLatestLogEntry(stepState.Attempts["1"])!.Message);
        Assert.Single(stepState.Attempts["1"].LogEntries);
    }

    [Fact]
    public async Task ParallelLayer_StaysIncomplete_Until_All_StepsReachFinalLifecycleState()
    {
        MainState mainState = new MainState();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyInitTimelineRunAsync(new InitTimelineRunSignal
        {
            SessionId = "session-1",
            Name = "Run",
            ProjectPath = "project.csproj",
            RunStructure = CreateParallelRunStructure()
        });

        StageNodeState stageState = mainState.ActiveRun!.Stages["Main"];
        Assert.Equal(new[] { "layer-0" }, DebugRunStateQueries.GetExecutionLayerKeys(stageState));
        Assert.Equal(new[] { 0, 1 }, stageState.ExecutionLayers["layer-0"].StepIds);

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 1,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Complete,
            PreviousState = DebugLifecycleState.Running
        });

        Assert.True(stageState.ExecutionLayers["layer-0"].IsActive);
        Assert.False(stageState.ExecutionLayers["layer-0"].IsComplete);

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 1,
            State = DebugLifecycleState.Complete,
            PreviousState = DebugLifecycleState.Running
        });

        Assert.False(stageState.ExecutionLayers["layer-0"].IsActive);
        Assert.True(stageState.ExecutionLayers["layer-0"].IsComplete);
    }

    [Fact]
    public async Task InitTimelineRun_SplitsLayersAcrossPhaseBoundaries()
    {
        MainState mainState = new MainState();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyInitTimelineRunAsync(new InitTimelineRunSignal
        {
            SessionId = "session-1",
            Name = "Run",
            ProjectPath = "project.csproj",
            RunStructure = CreatePhaseStructuredRun()
        });

        StageNodeState stageState = mainState.ActiveRun!.Stages["Main"];
        Assert.Equal(new[] { "layer-0", "layer-1", "layer-2" }, DebugRunStateQueries.GetExecutionLayerKeys(stageState));
        Assert.Equal(new[] { 0, 1 }, stageState.ExecutionLayers["layer-0"].StepIds);
        Assert.Equal(new[] { 2 }, stageState.ExecutionLayers["layer-1"].StepIds);
        Assert.Equal(new[] { 3 }, stageState.ExecutionLayers["layer-2"].StepIds);
        Assert.Equal(StepExecutionPhase.Prepare, stageState.Steps["0"].Phase);
        Assert.Equal(StepExecutionPhase.Act, stageState.Steps["2"].Phase);
        Assert.Equal(StepExecutionPhase.Materialize, stageState.Steps["3"].Phase);
    }

    [Fact]
    public async Task SignalsForOtherSession_AreIgnored()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyValueUpdateAsync(new ValueUpdateSignal
        {
            SessionId = "session-2",
            Name = "ignored",
            ValueKind = DebugValueKind.Variable,
            Envelope = CreateEnvelope(DebugValueKind.Variable, "System.String", "ignored", new JObject { ["value"] = "ignored" })
        });

        Assert.False(mainState.ActiveRun!.Variables.ContainsKey("ignored"));
    }

    [Fact]
    public async Task AssertionSignal_IsStoredSeparatelyWithStructuredFields()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyAssertionAsync(new AssertionSignal
        {
            SessionId = "session-1",
            Entry = new DebugAssertionEntry
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                TargetKind = DebugAssertionTargetKind.Variable,
                Target = "'user'",
                AssertionName = "Be",
                AssertionDisplay = "Be(\"Ada\")",
                Succeeded = false,
                Expected = "Ada",
                Actual = "Grace",
                FailureReason = "expected Ada, was Grace"
            }
        });

        Assert.Single(mainState.ActiveRun!.Assertions);
        AssertionEntryState assertion = mainState.ActiveRun.Assertions["0"];
        Assert.Equal(DebugAssertionTargetKind.Variable, assertion.TargetKind);
        Assert.Equal("Be", assertion.AssertionName);
        Assert.Equal("Ada", assertion.Expected);
        Assert.Equal("Grace", assertion.Actual);
        Assert.Equal("expected Ada, was Grace", assertion.FailureReason);
        Assert.Empty(mainState.ActiveRun.Stages["Main"].Steps["0"].Attempts);
    }

    [Fact]
    public async Task QueryHelpers_ExposeOrderedCanonicalTree()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });

        RunState runState = mainState.ActiveRun!;
        StageNodeState stageState = Assert.Single(DebugRunStateQueries.GetOrderedStages(runState));
        StepNodeState stepState = Assert.Single(DebugRunStateQueries.GetOrderedSteps(stageState));
        StepAttemptState attemptState = Assert.Single(DebugRunStateQueries.GetOrderedAttempts(stepState));

        Assert.Equal("Main", stageState.Name);
        Assert.Equal(0, stepState.StepId);
        Assert.Equal(1, attemptState.AttemptNumber);
        Assert.True(DebugRunStateQueries.TryGetStep(runState, "Main", 0, out StepNodeState queriedStepState));
        Assert.Same(stepState, queriedStepState);
    }

    [Fact]
    public async Task RunState_UsesDebugUiValueWrappers_ForVariablesAndArtifacts()
    {
        MainState mainState = await CreateInitializedMainStateAsync();

        DebugValueState variableState = mainState.ActiveRun!.Variables["input"];
        DebugValueState artifactState = mainState.ActiveRun.Artifacts["artifact"];

        Assert.Equal("input", variableState.Key);
        Assert.Equal("initial", variableState.Envelope.DisplayText);
        Assert.Equal("artifact", artifactState.Key);
        Assert.Equal("artifact", artifactState.Envelope.DisplayText);
    }

    [Fact]
    public async Task QueryHelpers_ExposeOrderedLayers_LatestAttemptSummary_AndOrderedLogs()
    {
        MainState mainState = await CreateInitializedMainStateAsync();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "session-1",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });

        await reducer.ApplyLogEntryAsync(new LogEntrySignal
        {
            SessionId = "session-1",
            Entry = new DebugLogEntry
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Level = DebugLogLevel.Information,
                EventName = "SecondLogEvent",
                Message = "second",
                Lines = ["second"],
                Stage = "Main",
                StepId = 0,
                Iteration = 1
            }
        });

        StageNodeState stageState = Assert.Single(DebugRunStateQueries.GetOrderedStages(mainState.ActiveRun!));
        StageLayerState layerState = Assert.Single(DebugRunStateQueries.GetOrderedLayers(stageState));
        StepNodeState stepState = Assert.Single(DebugRunStateQueries.GetOrderedSteps(stageState, layerState));
        StepAttemptSummary summary = Assert.IsType<StepAttemptSummary>(DebugRunStateQueries.GetLatestAttemptSummary(stepState));
        IReadOnlyList<LogEntryState> logs = DebugRunStateQueries.GetOrderedLogs(stepState);

        Assert.Equal("layer-0", layerState.Key);
        Assert.Equal(1, summary.AttemptNumber);
        Assert.Equal(DebugRunStateQueries.GetLatestLogEntry(stepState.Attempts["1"])?.Message, summary.LatestLogEntry?.Message);
        Assert.True(logs.Count >= 2);
        Assert.Equal("second", logs[^1].Message);
    }

    private static async Task<MainState> CreateInitializedMainStateAsync()
    {
        MainState mainState = new MainState();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);
        await reducer.ApplyInitTimelineRunAsync(new InitTimelineRunSignal
        {
            SessionId = "session-1",
            Name = "Run",
            ProjectPath = "project.csproj",
            RunStructure = CreateRunStructure()
        });
        StateTestHelpers.Eventually(() => mainState.ActiveRun is not null, "Expected initialized run state.");
        return mainState;
    }

    private static TimelineRunStructure CreateRunStructure()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>
            {
                [new VariableIdentifier("input")] = new VariableState
                {
                    Key = "input",
                    Envelope = CreateEnvelope(DebugValueKind.Variable, "System.String", "initial", new JObject { ["value"] = "initial" })
                }
            },
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>
            {
                [new ArtifactIdentifier("artifact")] = new TestFramework.Core.Debugger.ArtifactState
                {
                    Key = "artifact",
                    Envelope = CreateEnvelope(DebugValueKind.Artifact, "Artifact", "artifact", new JObject { ["reference"] = "ref" })
                }
            },
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
                            Description = "desc",
                            DoesReturn = true,
                            ErrorHandlingOptions = new ErrorHandlingOptions(),
                            ExecutionOptions = new ExecutionOptions(),
                            IOContract = new StepIOContract
                            {
                                Inputs =
                                {
                                    new StepIOEntry("input", StepIOKind.Variable, false, typeof(string)),
                                    new StepIOEntry("artifact", StepIOKind.Artifact, false, typeof(object))
                                }
                            },
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

    private static TimelineRunStructure CreateParallelRunStructure()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Main stage",
                    Steps =
                    [
                        CreateIndependentStep("Left"),
                        CreateIndependentStep("Right")
                    ]
                }
            ]
        };
    }

    private static TimelineRunStructure CreatePhaseStructuredRun()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Main stage",
                    Steps =
                    [
                        CreateStep("Prepare Left", StepExecutionPhase.Prepare),
                        CreateStep("Prepare Right", StepExecutionPhase.Prepare),
                        CreateStep("Trigger", StepExecutionPhase.Act),
                        CreateStep("Register Artifact", StepExecutionPhase.Materialize)
                    ]
                }
            ]
        };
    }

    private static DebugStepState CreateIndependentStep(string name)
    {
        return CreateStep(name, StepExecutionPhase.Prepare);
    }

    private static DebugStepState CreateStep(string name, StepExecutionPhase phase)
    {
        return new DebugStepState
        {
            Name = name,
            Description = "parallel step",
            DoesReturn = false,
            ErrorHandlingOptions = new ErrorHandlingOptions(),
            ExecutionOptions = new ExecutionOptions(),
            IOContract = new StepIOContract(),
            Phase = phase,
            LabelOptions = new LabelOptions(),
            RetryOptions = new RetryOptions(),
            TimeOutOptions = new TimeOutOptions()
        };
    }

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
}