using Newtonsoft.Json.Linq;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Tests.Support;

public sealed record DebugUiSampleScenario(
    string Key,
    string Description,
    TimelineRunStructure RunStructure,
    Func<DebugRunStateReducer, Task> DriveAsync)
{
    public override string ToString() => Key;
}

internal static class DebugUiSampleTimelineScenarios
{
    public static IReadOnlyList<DebugUiSampleScenario> All { get; } =
    [
        CreateLocalIoPipelineScenario(),
        CreateParallelFanOutScenario(),
        CreateRetryAndTimeoutScenario(),
        CreateMultiStageJourneyScenario(),
        CreateBreakpointInspectionScenario()
    ];

    private static DebugUiSampleScenario CreateLocalIoPipelineScenario()
    {
        TimelineRunStructure structure = new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>
            {
                [new VariableIdentifier("workingDirectory")] = CreateVariable("workingDirectory", @"C:\\temp\\showroom"),
                [new VariableIdentifier("command")] = CreateVariable("command", "dotnet test --filter Category=Smoke"),
                [new VariableIdentifier("pollDelay")] = CreateVariable("pollDelay", "00:00:02", "System.TimeSpan"),
                [new VariableIdentifier("filePath")] = CreateVariable("filePath", @"C:\\temp\\showroom\\results.json")
            },
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>
            {
                [new ArtifactIdentifier("stdout")]= CreateArtifact("stdout", "stdout.log"),
                [new ArtifactIdentifier("resultFile")]= CreateArtifact("resultFile", "results.json")
            },
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "LocalIO command and file pipeline",
                    Steps =
                    [
                        CreateStep(
                            name: "Prepare Working Directory",
                            phase: StepExecutionPhase.Prepare,
                            outputs:
                            [
                                new StepIOEntry("workingDirectory", StepIOKind.Variable, true, typeof(string)),
                                new StepIOEntry("command", StepIOKind.Variable, true, typeof(string))
                            ]),
                        CreateStep(
                            name: "CmdTrigger",
                            phase: StepExecutionPhase.Act,
                            inputs:
                            [
                                new StepIOEntry("workingDirectory", StepIOKind.Variable, true, typeof(string)),
                                new StepIOEntry("command", StepIOKind.Variable, true, typeof(string))
                            ],
                            outputs:
                            [
                                new StepIOEntry("stdout", StepIOKind.Artifact, false, typeof(object))
                            ]),
                        CreateStep(
                            name: "FileExistsEvent",
                            phase: StepExecutionPhase.Observe,
                            inputs:
                            [
                                new StepIOEntry("filePath", StepIOKind.Variable, true, typeof(string)),
                                new StepIOEntry("pollDelay", StepIOKind.Variable, false, typeof(TimeSpan))
                            ],
                            outputs:
                            [
                                new StepIOEntry("resultFile", StepIOKind.Artifact, false, typeof(object))
                            ]),
                        CreateStep(
                            name: "Register Result Artifact",
                            phase: StepExecutionPhase.Materialize,
                            inputs:
                            [
                                new StepIOEntry("resultFile", StepIOKind.Artifact, false, typeof(object))
                            ])
                    ]
                }
            ]
        };

        return new DebugUiSampleScenario(
            "LocalIO Pipeline",
            "Command trigger, file wait, and artifact materialization flow for LocalIO-like UI testing.",
            structure,
            async reducer =>
            {
                await RunStepAsync(reducer, 0, ["Prepared LocalIO command inputs."]);
                await RunStepAsync(reducer, 1,
                [
                    "Launching dotnet test from LocalIO.",
                    "Command still running...",
                    "Command finished with exit code 0."
                ],
                valueUpdates:
                [
                    CreateArtifactUpdate("stdout", 1, "stdout.log")
                ]);

                await RunStepAsync(reducer, 2,
                [
                    "Waiting for results.json to appear.",
                    "Detected result file."
                ],
                valueUpdates:
                [
                    CreateArtifactUpdate("resultFile", 2, "results.json")
                ]);

                await RunStepAsync(reducer, 3, ["Registered LocalIO result artifact."]);
            });
    }

    private static DebugUiSampleScenario CreateParallelFanOutScenario()
    {
        TimelineRunStructure structure = new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Parallel fan-out and join",
                    Steps =
                    [
                        CreateStep("Prepare Left", StepExecutionPhase.Prepare),
                        CreateStep("Prepare Right", StepExecutionPhase.Prepare),
                        CreateStep("Trigger Left", StepExecutionPhase.Act),
                        CreateStep("Trigger Right", StepExecutionPhase.Act),
                        CreateStep("Join Results", StepExecutionPhase.Materialize)
                    ]
                }
            ]
        };

        return new DebugUiSampleScenario(
            "Parallel Fan Out",
            "Parallel prepare steps followed by separate trigger steps and a join step.",
            structure,
            async reducer =>
            {
                await RunStepAsync(reducer, 0, ["Left side prepared."]);
                await RunStepAsync(reducer, 1, ["Right side prepared."]);
                await RunStepAsync(reducer, 2, ["Left branch triggered."]);
                await RunStepAsync(reducer, 3, ["Right branch triggered."]);
                await RunStepAsync(reducer, 4, ["Joined branch outputs."]);
            });
    }

    private static DebugUiSampleScenario CreateRetryAndTimeoutScenario()
    {
        TimelineRunStructure structure = new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>
            {
                [new VariableIdentifier("requestId")] = CreateVariable("requestId", "req-42")
            },
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Retry and timeout sample",
                    Steps =
                    [
                        CreateStep(
                            "Submit Request",
                            StepExecutionPhase.Act,
                            inputs: [new StepIOEntry("requestId", StepIOKind.Variable, true, typeof(string))])
                    ]
                }
            ]
        };

        return new DebugUiSampleScenario(
            "Retry And Timeout",
            "Single step with timeout, retry transition, and successful second attempt.",
            structure,
            async reducer =>
            {
                await StartStepAsync(reducer, 0, ["Submitting request req-42.", "Initial attempt stalled."], attemptNumber: 1);
                await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
                {
                    SessionId = SessionId,
                    EntityKind = DebugEntityKind.Step,
                    Stage = "Main",
                    StepId = 0,
                    State = DebugLifecycleState.WaitingForRetry,
                    PreviousState = DebugLifecycleState.Running,
                    OutcomeState = DebugLifecycleState.Timeout,
                    OccurredAtUtc = UtcNow()
                });
                await StartStepAsync(reducer, 0, ["Retrying request req-42.", "Retry succeeded."], attemptNumber: 2);
                await CompleteStepAsync(reducer, 0, DebugLifecycleState.Complete);
            });
    }

    private static DebugUiSampleScenario CreateMultiStageJourneyScenario()
    {
        TimelineRunStructure structure = new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>
            {
                [new VariableIdentifier("userId")] = CreateVariable("userId", "user-7"),
                [new VariableIdentifier("email")] = CreateVariable("email", "ada@example.com")
            },
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>
            {
                [new ArtifactIdentifier("userRecord")]= CreateArtifact("userRecord", "user-7.json")
            },
            Stages =
            [
                new DebugStageState
                {
                    Name = "Arrange",
                    Description = "Set up input state",
                    Steps = [CreateStep("Seed Variables", StepExecutionPhase.Prepare)]
                },
                new DebugStageState
                {
                    Name = "Act",
                    Description = "Trigger the system under test",
                    Steps = [CreateStep("Send Signup Command", StepExecutionPhase.Act)]
                },
                new DebugStageState
                {
                    Name = "Observe",
                    Description = "Wait for persistence",
                    Steps = [CreateStep("Wait For User Record", StepExecutionPhase.Observe)]
                },
                new DebugStageState
                {
                    Name = "Assert",
                    Description = "Materialize and verify outputs",
                    Steps = [CreateStep("Capture User Artifact", StepExecutionPhase.Materialize)]
                }
            ]
        };

        return new DebugUiSampleScenario(
            "Multi Stage Journey",
            "Arrange/Act/Observe/Assert layout for stage navigation and summary panels.",
            structure,
            async reducer =>
            {
                await RunStepAsync(reducer, 0, ["Seeded user variables."], stageName: "Arrange");
                await RunStepAsync(reducer, 0, ["Signup command sent."], stageName: "Act");
                await RunStepAsync(reducer, 0, ["Observed new user record."], stageName: "Observe");
                await RunStepAsync(reducer, 0, ["Captured userRecord artifact."], stageName: "Assert");
            });
    }

    private static DebugUiSampleScenario CreateBreakpointInspectionScenario()
    {
        TimelineRunStructure structure = new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Breakpoint and inspection scenario",
                    Steps =
                    [
                        CreateStep("Inspect Payload", StepExecutionPhase.Act),
                        CreateStep("Resume Validation", StepExecutionPhase.Observe)
                    ]
                }
            ]
        };

        return new DebugUiSampleScenario(
            "Breakpoint Inspection",
            "Breakpoint hit and resumed execution for inspector-style UI surfaces.",
            structure,
            async reducer =>
            {
                await StartStepAsync(reducer, 0, ["Reached payload inspection point."]);
                await reducer.ApplyBreakpointHitRequestAsync(new BreakpointHitRequestSignal
                {
                    SessionId = SessionId,
                    Stage = "Main",
                    StepId = 0
                });
                await reducer.ApplyLogEntryAsync(CreateLogSignal("Main", 0, 1, "Breakpoint acknowledged; awaiting continue."));
                await CompleteStepAsync(reducer, 0, DebugLifecycleState.Complete);
                await RunStepAsync(reducer, 1, ["Validation resumed after breakpoint."]);
            });
    }

    private static async Task RunStepAsync(DebugRunStateReducer reducer, int stepId, IReadOnlyList<string> logs, IReadOnlyList<ValueUpdateSignal>? valueUpdates = null, string stageName = "Main", int attemptNumber = 1)
    {
        await StartStepAsync(reducer, stepId, logs, stageName, attemptNumber);

        if (valueUpdates is not null)
        {
            foreach (ValueUpdateSignal valueUpdate in valueUpdates)
                await reducer.ApplyValueUpdateAsync(valueUpdate);
        }

        await CompleteStepAsync(reducer, stepId, DebugLifecycleState.Complete, stageName);
    }

    private static async Task StartStepAsync(DebugRunStateReducer reducer, int stepId, IReadOnlyList<string> logs, string stageName = "Main", int attemptNumber = 1)
    {
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = SessionId,
            EntityKind = DebugEntityKind.Stage,
            Stage = stageName,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized,
            OccurredAtUtc = UtcNow()
        });

        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = SessionId,
            EntityKind = DebugEntityKind.Step,
            Stage = stageName,
            StepId = stepId,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized,
            OccurredAtUtc = UtcNow()
        });

        foreach (string log in logs)
            await reducer.ApplyLogEntryAsync(CreateLogSignal(stageName, stepId, attemptNumber, log));
    }

    private static async Task CompleteStepAsync(DebugRunStateReducer reducer, int stepId, DebugLifecycleState finalState, string stageName = "Main")
    {
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = SessionId,
            EntityKind = DebugEntityKind.Step,
            Stage = stageName,
            StepId = stepId,
            State = finalState,
            PreviousState = DebugLifecycleState.Running,
            OccurredAtUtc = UtcNow()
        });
    }

    private static LogEntrySignal CreateLogSignal(string stageName, int stepId, int attemptNumber, string message)
    {
        return new LogEntrySignal
        {
            SessionId = SessionId,
            Entry = new DebugLogEntry
            {
                OccurredAtUtc = UtcNow(),
                Level = DebugLogLevel.Information,
                EventName = "SampleScenarioLog",
                Message = message,
                Lines = [message],
                Stage = stageName,
                StepId = stepId,
                Iteration = attemptNumber
            }
        };
    }

    private static ValueUpdateSignal CreateArtifactUpdate(string artifactKey, int stepId, string displayText)
    {
        return new ValueUpdateSignal
        {
            SessionId = SessionId,
            Name = artifactKey,
            ValueKind = DebugValueKind.Artifact,
            Stage = "Main",
            StepId = stepId,
            ObservedAtUtc = UtcNow(),
            Envelope = CreateEnvelope(DebugValueKind.Artifact, "Artifact", displayText, new JObject { ["reference"] = displayText })
        };
    }

    private static VariableState CreateVariable(string key, string displayText, string typeName = "System.String")
    {
        return new VariableState
        {
            Key = key,
            Envelope = CreateEnvelope(DebugValueKind.Variable, typeName, displayText, new JObject { ["value"] = displayText })
        };
    }

    private static TestFramework.Core.Debugger.ArtifactState CreateArtifact(string key, string displayText)
    {
        return new TestFramework.Core.Debugger.ArtifactState
        {
            Key = key,
            Envelope = CreateEnvelope(DebugValueKind.Artifact, "Artifact", displayText, new JObject { ["reference"] = displayText })
        };
    }

    private static DebugStepState CreateStep(string name, StepExecutionPhase phase, IReadOnlyList<StepIOEntry>? inputs = null, IReadOnlyList<StepIOEntry>? outputs = null)
    {
        StepIOContract contract = new StepIOContract();
        if (inputs is not null)
        {
            foreach (StepIOEntry input in inputs)
                contract.Inputs.Add(input);
        }

        if (outputs is not null)
        {
            foreach (StepIOEntry output in outputs)
                contract.Outputs.Add(output);
        }

        return new DebugStepState
        {
            Name = name,
            Description = $"Sample step for {name}",
            DoesReturn = outputs is not null && outputs.Count > 0,
            ErrorHandlingOptions = new ErrorHandlingOptions(),
            ExecutionOptions = new ExecutionOptions(),
            IOContract = contract,
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

    private static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    private const string SessionId = "sample-session";
}