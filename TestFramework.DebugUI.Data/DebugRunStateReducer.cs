using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFramework.DebugUI.State;
using WpfStateService;
using WpfStateService.Common;

namespace TestFramework.DebugUI;

public sealed class DebugRunStateReducer(MainState mainState)
{
    public Task ApplyPipeServerReadyAsync(string pipeName)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            PipeConnectionState connectionState = mainState.PipeConnection;
            connectionState.PipeName = pipeName;
            connectionState.Status = PipeConnectionStatus.Listening;
            connectionState.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            AppendConnectionDebugInfo($"Pipe server ready ({pipeName})");
        });
    }

    public Task ApplyPipeConnectionAttachedAsync(string pipeName)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            PipeConnectionState connectionState = mainState.PipeConnection;
            connectionState.PipeName = pipeName;
            connectionState.Status = PipeConnectionStatus.Connected;
            connectionState.IsConnected = true;
            connectionState.ConnectionCount++;
            connectionState.LastConnectedAtUtc = DateTimeOffset.UtcNow;
            connectionState.LastUpdatedAtUtc = connectionState.LastConnectedAtUtc;
            connectionState.LastDisconnectReason = "";
            connectionState.LastFailureReason = "";
            connectionState.ConnectedSessionId = "";
            AppendConnectionDebugInfo("Piped Debugger Attached");
        });
    }

    public Task ApplyPipeConnectionDetachedAsync(string reason)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            PipeConnectionState connectionState = mainState.PipeConnection;
            connectionState.Status = ClassifyDisconnect(reason);
            connectionState.IsConnected = false;
            connectionState.DisconnectCount++;
            connectionState.LastDisconnectedAtUtc = DateTimeOffset.UtcNow;
            connectionState.LastUpdatedAtUtc = connectionState.LastDisconnectedAtUtc;
            connectionState.LastDisconnectReason = reason;
            connectionState.LastFailureReason = connectionState.Status == PipeConnectionStatus.Faulted ? reason : "";
            connectionState.ConnectedSessionId = "";
            AppendConnectionDebugInfo($"Piped Debugger Dettached ({reason})");
        });
    }

    public Task ApplyInitTimelineRunAsync(InitTimelineRunSignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            RunState runState = new RunState
            {
                SessionId = signal.SessionId,
                Name = signal.Name,
                ProjectPath = signal.ProjectPath,
                LifecycleState = DebugLifecycleState.Initialized,
                LastTransitionAtUtc = DateTimeOffset.UtcNow,
                Artifacts = new StateDictionary<DebugValueState>(),
                Variables = new StateDictionary<DebugValueState>(),
                Assertions = new StateDictionary<AssertionEntryState>(),
                Stages = new StateDictionary<StageNodeState>()
            };

            CopyValues(signal.RunStructure.Variables.Values, runState.Variables);
            CopyValues(signal.RunStructure.Artifacts.Values, runState.Artifacts);

            for (int stageIndex = 0; stageIndex < signal.RunStructure.Stages.Length; stageIndex++)
            {
                DebugStageState stage = signal.RunStructure.Stages[stageIndex];
                ExecutionLayerPlan[] executionLayers = BuildExecutionLayers(stage.Steps).ToArray();
                StageNodeState stageState = new StageNodeState
                {
                    Name = stage.Name,
                    Description = stage.Description,
                    Order = stageIndex,
                    LifecycleState = DebugLifecycleState.Initialized,
                    ExecutionLayers = new StateDictionary<StageLayerState>(),
                    Steps = new StateDictionary<StepNodeState>()
                };

                foreach (ExecutionLayerPlan layer in executionLayers)
                {
                    stageState.ExecutionLayers[layer.Key] = new StageLayerState
                    {
                        Key = layer.Key,
                        Order = layer.Order,
                        StepIds = layer.StepIds,
                        IsActive = false,
                        IsComplete = false
                    };
                }

                for (int index = 0; index < stage.Steps.Length; index++)
                {
                    DebugStepState step = stage.Steps[index];
                    string layerKey = executionLayers.First(layer => layer.StepIds.Contains(index)).Key;
                    StepNodeState stepState = new StepNodeState
                    {
                        StageName = stage.Name,
                        StepId = index,
                        Order = index,
                        ExecutionLayerKey = layerKey,
                        Name = step.Name,
                        Description = step.Description,
                        DoesReturn = step.DoesReturn,
                        ParallelizationMode = step.ExecutionOptions.ParallelizationMode,
                        Phase = step.Phase,
                        LifecycleState = DebugLifecycleState.Initialized,
                        State = StepState.NotRun,
                        Attempts = new StateDictionary<StepAttemptState>(),
                        Inputs = new StateDictionary<IOConnectionState>(),
                        Outputs = new StateDictionary<IOConnectionState>()
                    };
                    SeedDeclaredConnections(stepState, step.IOContract.Inputs, IOConnectionDirection.Input);
                    SeedDeclaredConnections(stepState, step.IOContract.Outputs, IOConnectionDirection.Output);
                    stageState.Steps[index.ToString()] = stepState;
                }

                RefreshStageExecutionLayers(stageState);

                runState.Stages[stage.Name] = stageState;
            }

            mainState.ActiveRun = runState;
            mainState.PipeConnection.ConnectedSessionId = signal.SessionId;
            mainState.PipeConnection.LastSessionId = signal.SessionId;
            mainState.PipeConnection.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        });
    }

    public Task ApplyEntityTransitionAsync(EntityTransitionSignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            if (!TryGetRun(signal.SessionId, out RunState runState))
                return;

            switch (signal.EntityKind)
            {
                case DebugEntityKind.Run:
                    ApplyLifecycle(runState, signal.State, signal.PreviousState, signal.OccurredAtUtc);
                    break;
                case DebugEntityKind.Stage:
                    if (!TryGetStage(runState, signal.Stage, out StageNodeState stageState))
                        return;

                    ApplyLifecycle(stageState, signal.State, signal.PreviousState, signal.OccurredAtUtc);
                    break;
                case DebugEntityKind.Step:
                    if (!TryGetStep(runState, signal.Stage, signal.StepId, out StageNodeState owningStage, out StepNodeState stepState))
                        return;

                    ApplyLifecycle(stepState, signal.State, signal.PreviousState, signal.OccurredAtUtc);
                    stepState.IsWaitingAtBreakpoint = false;

                    if (signal.State == DebugLifecycleState.Running)
                    {
                        stepState.AttemptCount++;
                        StepAttemptState attemptState = EnsureAttempt(stepState, stepState.AttemptCount, signal.OccurredAtUtc);
                        PopulateStepInputs(runState, signal.Stage!, signal.StepId!.Value, stepState);
                        attemptState.LifecycleState = DebugLifecycleState.Running;
                        AddFrameworkLogEntry(stepState, attemptState, signal.OccurredAtUtc, DebugLogLevel.Information, "StepRunning", stepState.AttemptCount == 1 ? $"Executing Step: {GetStepDisplayName(stepState)}" : $"Executing Step Attempt {stepState.AttemptCount}: {GetStepDisplayName(stepState)}");
                    }

                    if (signal.PreviousState == DebugLifecycleState.Running && TryGetLatestAttempt(stepState, out StepAttemptState latestAttempt))
                    {
                        latestAttempt.IsActive = false;
                        latestAttempt.FinishedAtUtc = signal.OccurredAtUtc;
                        latestAttempt.LifecycleState = signal.OutcomeState ?? signal.State;
                        AddFrameworkLogEntry(stepState, latestAttempt, signal.OccurredAtUtc, signal.State == DebugLifecycleState.WaitingForRetry ? DebugLogLevel.Warning : MapLogLevel(signal.OutcomeState ?? signal.State), "StepOutcome", $"{MapStatePrefix(signal.OutcomeState ?? signal.State)}  {GetStepDisplayName(stepState)}");
                    }

                    stepState.State = MapStepState(signal.State, signal.OutcomeState, stepState.State);
                    RefreshStageExecutionLayers(owningStage);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(signal.EntityKind), signal.EntityKind, null);
            }
        });
    }

    public Task ApplyLogEntryAsync(LogEntrySignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            if (!TryGetRun(signal.SessionId, out RunState runState))
                return;

            if (!TryGetStep(runState, signal.Entry.Stage, signal.Entry.StepId, out _, out StepNodeState stepState))
                return;

            int attemptNumber = signal.Entry.Iteration ?? 0;
            if (attemptNumber <= 0)
                return;

            StepAttemptState attemptState = EnsureAttempt(stepState, attemptNumber, signal.Entry.OccurredAtUtc);
            string key = attemptState.LogEntries.Count.ToString();
            LogEntryState entryState = new()
            {
                Key = key,
                OccurredAtUtc = signal.Entry.OccurredAtUtc,
                Level = signal.Entry.Level,
                EventName = signal.Entry.EventName,
                Message = signal.Entry.Message,
                IndentLevel = signal.Entry.IndentLevel,
                StageName = signal.Entry.Stage ?? "",
                StepId = signal.Entry.StepId,
                AttemptNumber = signal.Entry.Iteration,
                AssertionScope = signal.Entry.AssertionScope ?? ""
            };

            AppendAttemptLog(stepState, attemptState, entryState);
        });
    }

    public Task ApplyAssertionAsync(AssertionSignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            if (!TryGetRun(signal.SessionId, out RunState runState))
                return;

            string key = runState.Assertions.Count.ToString();
            runState.Assertions[key] = new AssertionEntryState
            {
                Key = key,
                OccurredAtUtc = signal.Entry.OccurredAtUtc,
                TargetKind = signal.Entry.TargetKind,
                Target = signal.Entry.Target,
                AssertionName = signal.Entry.AssertionName,
                AssertionDisplay = signal.Entry.AssertionDisplay,
                Succeeded = signal.Entry.Succeeded,
                Expected = signal.Entry.Expected,
                Actual = signal.Entry.Actual,
                FailureReason = signal.Entry.FailureReason,
                AssertionScope = signal.Entry.AssertionScope ?? ""
            };
        });
    }

    public Task ApplyValueUpdateAsync(ValueUpdateSignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            if (!TryGetRun(signal.SessionId, out RunState runState))
                return;

            switch (signal.ValueKind)
            {
                case DebugValueKind.Variable:
                    DebugValueState variableState = CreateDebugValueState(signal.Name, signal.Envelope);
                    Upsert(runState.Variables, variableState.Key, variableState);

                    if (TryGetStep(runState, signal.Stage, signal.StepId, out _, out StepNodeState variableStepState))
                    {
                        UpsertConnectionValue(variableStepState, IOConnectionDirection.Output, StepIOKind.Variable, variableState.Key, variableState.Envelope);
                        if (TryGetActiveAttempt(variableStepState, out StepAttemptState activeAttempt))
                            AddFrameworkLogEntry(variableStepState, activeAttempt, signal.ObservedAtUtc, DebugLogLevel.Information, "VariableUpdate", $"Set Variable ({variableState.Key}) = {signal.Envelope.DisplayText}");
                    }
                    break;
                case DebugValueKind.Artifact:
                    DebugValueState artifactState = CreateDebugValueState(signal.Name, signal.Envelope);
                    Upsert(runState.Artifacts, artifactState.Key, artifactState);

                    if (TryGetStep(runState, signal.Stage, signal.StepId, out _, out StepNodeState artifactStepState))
                    {
                        UpsertConnectionValue(artifactStepState, IOConnectionDirection.Output, StepIOKind.Artifact, artifactState.Key, artifactState.Envelope);
                        if (TryGetActiveAttempt(artifactStepState, out StepAttemptState activeAttempt))
                            AddFrameworkLogEntry(artifactStepState, activeAttempt, signal.ObservedAtUtc, DebugLogLevel.Information, "ArtifactUpdate", $"Set Artifact ({artifactState.Key}) = {signal.Envelope.DisplayText}");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(signal.ValueKind), signal.ValueKind, null);
            }
        });
    }

    public Task ApplyBreakpointHitRequestAsync(BreakpointHitRequestSignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            if (!TryGetRun(signal.SessionId, out RunState runState))
                return;

            if (!TryGetStep(runState, signal.Stage, signal.StepId, out _, out StepNodeState stepState))
                return;

            stepState.BreakpointHitCount++;
            stepState.IsWaitingAtBreakpoint = true;
            stepState.LastBreakpointAtUtc = DateTimeOffset.UtcNow;
            if (TryGetActiveAttempt(stepState, out StepAttemptState activeAttempt))
                AddFrameworkLogEntry(stepState, activeAttempt, DateTimeOffset.UtcNow, DebugLogLevel.Information, "Breakpoint", $"Breakpoint Hit: {GetStepDisplayName(stepState)}");
        });
    }

    public Task ApplyTimelineRunFinishedAsync(TimelineRunFinishedSignal signal)
    {
        return StateServiceDispatcher.DispatchAsync(() =>
        {
            if (!TryGetRun(signal.SessionId, out RunState runState))
                return;

            runState.IsFinished = true;
            runState.FinishedAtUtc = DateTimeOffset.UtcNow;
        });
    }

    private static void CopyValues(IEnumerable<VariableState> values, StateDictionary<DebugValueState> target)
    {
        target.Clear();

        foreach (VariableState value in values)
            Upsert(target, value.Key, CreateDebugValueState(value.Key, value.Envelope));
    }

    private static void CopyValues(IEnumerable<ArtifactState> values, StateDictionary<DebugValueState> target)
    {
        target.Clear();

        foreach (ArtifactState value in values)
            Upsert(target, value.Key, CreateDebugValueState(value.Key, value.Envelope));
    }

    private static void Upsert<TValue>(StateDictionary<TValue> dictionary, string key, TValue value)
    {
        dictionary[key] = value;
    }

    private bool TryGetRun(string sessionId, out RunState runState)
    {
        runState = mainState.ActiveRun!;
        return mainState.ActiveRun is not null && StringComparer.Ordinal.Equals(mainState.ActiveRun.SessionId, sessionId);
    }

    private static bool TryGetStage(RunState runState, string? stageName, out StageNodeState stageState)
    {
        stageState = null!;
        return stageName is not null && runState.Stages.TryGetValue(stageName, out stageState!);
    }

    private void AppendConnectionDebugInfo(string message)
    {
        PipeConnectionState connectionState = mainState.PipeConnection;
        connectionState.DebugInfo = AppendLine(connectionState.DebugInfo, message);
    }

    private static string AppendLine(string existing, string line)
    {
        return string.IsNullOrWhiteSpace(existing) ? line : existing + Environment.NewLine + line;
    }

    private static PipeConnectionStatus ClassifyDisconnect(string reason)
    {
        return reason.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("failure", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("unexpected", StringComparison.OrdinalIgnoreCase)
            ? PipeConnectionStatus.Faulted
            : PipeConnectionStatus.Disconnected;
    }

    private static bool TryGetStep(RunState runState, string? stageName, int? stepId, out StageNodeState stageState, out StepNodeState stepState)
    {
        stepState = null!;
        if (!TryGetStage(runState, stageName, out stageState) || stepId is null)
            return false;

        return stageState.Steps.TryGetValue(stepId.Value.ToString(), out stepState!);
    }

    private static void PopulateStepInputs(RunState runState, string stageName, int stepId, StepNodeState stepState)
    {
        ResetConnectionValues(stepState.Inputs);

        foreach (IOConnectionState inputConnection in stepState.Inputs.Values.ToArray())
        {
            switch (inputConnection.Kind)
            {
                case StepIOKind.Variable when runState.Variables.TryGetValue(inputConnection.Name, out DebugValueState? variableState) && variableState is not null:
                    UpsertConnectionValue(stepState, IOConnectionDirection.Input, StepIOKind.Variable, variableState.Key, variableState.Envelope, inputConnection.IsRequired, inputConnection.DeclaredTypeName);
                    break;
                case StepIOKind.Artifact when runState.Artifacts.TryGetValue(inputConnection.Name, out DebugValueState? artifactState) && artifactState is not null:
                    UpsertConnectionValue(stepState, IOConnectionDirection.Input, StepIOKind.Artifact, artifactState.Key, artifactState.Envelope, inputConnection.IsRequired, inputConnection.DeclaredTypeName);
                    break;
            }
        }
    }

    private static DebugValueState CreateDebugValueState(string key, DebugValueEnvelope envelope)
    {
        return new DebugValueState
        {
            Key = key,
            Envelope = envelope
        };
    }

    private static void ApplyLifecycle(RunState runState, DebugLifecycleState state, DebugLifecycleState? previousState, DateTimeOffset occurredAtUtc)
    {
        runState.PreviousLifecycleState = previousState;
        runState.LifecycleState = state;
        runState.LastTransitionAtUtc = occurredAtUtc;
    }

    private static void ApplyLifecycle(StageNodeState stageState, DebugLifecycleState state, DebugLifecycleState? previousState, DateTimeOffset occurredAtUtc)
    {
        stageState.PreviousLifecycleState = previousState;
        stageState.LifecycleState = state;
        stageState.LastTransitionAtUtc = occurredAtUtc;
    }

    private static void ApplyLifecycle(StepNodeState stepState, DebugLifecycleState state, DebugLifecycleState? previousState, DateTimeOffset occurredAtUtc)
    {
        stepState.PreviousLifecycleState = previousState;
        stepState.LifecycleState = state;
        stepState.LastTransitionAtUtc = occurredAtUtc;
    }

    private static StepState MapStepState(DebugLifecycleState state, DebugLifecycleState? outcomeState, StepState currentState)
    {
        if (state == DebugLifecycleState.WaitingForRetry)
            return currentState;

        DebugLifecycleState effectiveState = outcomeState ?? state;
        return effectiveState switch
        {
            DebugLifecycleState.Complete => StepState.Complete,
            DebugLifecycleState.Error => StepState.Error,
            DebugLifecycleState.Timeout => StepState.Timeout,
            DebugLifecycleState.Skipped => StepState.Skipped,
            _ => currentState
        };
    }

    private static StepAttemptState EnsureAttempt(StepNodeState stepState, int attemptNumber, DateTimeOffset startedAtUtc)
    {
        string key = attemptNumber.ToString();
        if (!stepState.Attempts.TryGetValue(key, out StepAttemptState? attemptState) || attemptState is null)
        {
            attemptState = new StepAttemptState
            {
                Key = key,
                Name = $"Attempt {attemptNumber}",
                AttemptNumber = attemptNumber,
                StartedAtUtc = startedAtUtc,
                IsActive = true,
                LifecycleState = DebugLifecycleState.Running,
                LogEntries = new StateDictionary<LogEntryState>()
            };
            stepState.Attempts[key] = attemptState;
        }

        attemptState.IsActive = true;
        attemptState.StartedAtUtc ??= startedAtUtc;
        attemptState.LifecycleState = DebugLifecycleState.Running;
        return attemptState;
    }

    private static void AddFrameworkLogEntry(StepNodeState stepState, StepAttemptState attemptState, DateTimeOffset occurredAtUtc, DebugLogLevel level, string eventName, string message)
    {
        LogEntryState entryState = new()
        {
            Key = attemptState.LogEntries.Count.ToString(),
            OccurredAtUtc = occurredAtUtc,
            Level = level,
            EventName = eventName,
            Message = message,
            IndentLevel = 0,
            StageName = stepState.StageName,
            StepId = stepState.StepId,
            AttemptNumber = attemptState.AttemptNumber,
            AssertionScope = ""
        };

        AppendAttemptLog(stepState, attemptState, entryState);
    }

    private static void AppendAttemptLog(StepNodeState stepState, StepAttemptState attemptState, LogEntryState entryState)
    {
        Upsert(attemptState.LogEntries, entryState.Key, entryState);
    }

    private static string GetStepDisplayName(StepNodeState stepState)
    {
        return stepState.Name;
    }

    private static IReadOnlyList<ExecutionLayerPlan> BuildExecutionLayers(DebugStepState[] steps)
    {
        Dictionary<int, List<int>> dependents = Enumerable.Range(0, steps.Length).ToDictionary(index => index, _ => new List<int>());
        Dictionary<int, int> indegree = Enumerable.Range(0, steps.Length).ToDictionary(index => index, _ => 0);

        for (int leftIndex = 0; leftIndex < steps.Length; leftIndex++)
        {
            for (int rightIndex = leftIndex + 1; rightIndex < steps.Length; rightIndex++)
            {
                if (!RequiresSequentialOrdering(steps[leftIndex], steps[rightIndex]))
                    continue;

                dependents[leftIndex].Add(rightIndex);
                indegree[rightIndex]++;
            }
        }

        List<ExecutionLayerPlan> layers = [];
        HashSet<int> scheduled = [];

        while (scheduled.Count < steps.Length)
        {
            int[] readyStepIds = indegree
                .Where(entry => !scheduled.Contains(entry.Key) && entry.Value == 0)
                .Select(entry => entry.Key)
                .OrderBy(index => index)
                .ToArray();

            if (readyStepIds.Length == 0)
                throw new InvalidOperationException("Unable to derive debugger-visible execution layers for the stage.");

            int layerIndex = layers.Count;
            layers.Add(new ExecutionLayerPlan($"layer-{layerIndex}", layerIndex, readyStepIds));

            foreach (int readyStepId in readyStepIds)
            {
                scheduled.Add(readyStepId);
                foreach (int dependent in dependents[readyStepId])
                    indegree[dependent]--;
            }
        }

        return layers;
    }

    private static bool RequiresSequentialOrdering(DebugStepState left, DebugStepState right)
    {
        if (RequiresPhaseOrdering(left, right))
            return true;

        if (left.ExecutionOptions.ParallelizationMode == StepParallelizationMode.DoNotParallelize || right.ExecutionOptions.ParallelizationMode == StepParallelizationMode.DoNotParallelize)
            return true;

        return HasAccessConflict(left.IOContract, right.IOContract);
    }

    private static bool RequiresPhaseOrdering(DebugStepState left, DebugStepState right)
    {
        if (left.Phase != right.Phase)
            return true;

        return !IsMergeablePhase(left.Phase);
    }

    private static bool IsMergeablePhase(StepExecutionPhase phase)
    {
        return phase is StepExecutionPhase.Prepare or StepExecutionPhase.Materialize;
    }

    private static bool HasAccessConflict(StepIOContract left, StepIOContract right)
    {
        foreach (StepIOEntry leftOutput in left.Outputs)
        {
            if (ContainsEntry(right.Inputs, leftOutput) || ContainsEntry(right.Outputs, leftOutput))
                return true;
        }

        foreach (StepIOEntry leftInput in left.Inputs)
        {
            if (ContainsEntry(right.Outputs, leftInput))
                return true;
        }

        return false;
    }

    private static bool ContainsEntry(IEnumerable<StepIOEntry> entries, StepIOEntry candidate)
    {
        return entries.Any(x => x.Kind == candidate.Kind && StringComparer.Ordinal.Equals(x.Key, candidate.Key));
    }

    private static void SeedDeclaredConnections(StepNodeState stepState, IEnumerable<StepIOEntry> entries, IOConnectionDirection direction)
    {
        foreach (StepIOEntry entry in entries)
        {
            StateDictionary<IOConnectionState> connections = GetConnections(stepState, direction);
            string connectionKey = GetConnectionKey(entry.Kind, entry.Key);
            if (!connections.TryGetValue(connectionKey, out IOConnectionState? connection) || connection is null)
            {
                connection = new IOConnectionState
                {
                    Key = connectionKey,
                    Name = entry.Key,
                    Direction = direction,
                    Kind = entry.Kind,
                    IsRequired = entry.Required,
                    DeclaredTypeName = entry.DeclaredType?.FullName ?? entry.DeclaredType?.Name ?? "",
                    HasValue = false,
                    DisplayText = ""
                };
                connections[connectionKey] = connection;
            }
        }
    }

    private static void ResetConnectionValues(StateDictionary<IOConnectionState> connections)
    {
        foreach (IOConnectionState connection in connections.Values)
        {
            connection.HasValue = false;
            connection.DisplayText = "";
            connection.Envelope = null!;
        }
    }

    private static void UpsertConnectionValue(StepNodeState stepState, IOConnectionDirection direction, StepIOKind kind, string name, DebugValueEnvelope envelope, bool? isRequired = null, string declaredTypeName = "")
    {
        StateDictionary<IOConnectionState> connections = GetConnections(stepState, direction);
        string key = GetConnectionKey(kind, name);
        if (!connections.TryGetValue(key, out IOConnectionState? connection) || connection is null)
        {
            connection = new IOConnectionState
            {
                Key = key,
                Name = name,
                Direction = direction,
                Kind = kind,
                IsRequired = isRequired ?? false,
                DeclaredTypeName = declaredTypeName
            };
            connections[key] = connection;
        }

        connection.IsRequired = isRequired ?? connection.IsRequired;
        if (!string.IsNullOrWhiteSpace(declaredTypeName))
            connection.DeclaredTypeName = declaredTypeName;
        connection.Envelope = envelope;
        connection.HasValue = true;
        connection.DisplayText = envelope.DisplayText;
    }

    private static StateDictionary<IOConnectionState> GetConnections(StepNodeState stepState, IOConnectionDirection direction)
    {
        return direction == IOConnectionDirection.Input ? stepState.Inputs : stepState.Outputs;
    }

    private static string GetConnectionKey(StepIOKind kind, string name)
    {
        return $"{kind}:{name}";
    }

    private static bool TryGetActiveAttempt(StepNodeState stepState, out StepAttemptState attemptState)
    {
        attemptState = stepState.Attempts.Values
            .Where(candidate => candidate.IsActive)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefault()!;
        return attemptState is not null;
    }

    private static bool TryGetLatestAttempt(StepNodeState stepState, out StepAttemptState attemptState)
    {
        attemptState = stepState.Attempts.Values
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefault()!;
        return attemptState is not null;
    }

    private static void RefreshStageExecutionLayers(StageNodeState stageState)
    {
        foreach (StageLayerState layerState in stageState.ExecutionLayers.Values.OrderBy(layer => layer.Order))
        {
            StepNodeState[] layerSteps = layerState.StepIds.Select(stepId => stageState.Steps[stepId.ToString()]).ToArray();
            bool isComplete = layerSteps.Length > 0 && layerSteps.All(IsStepInFinalLifecycleState);
            bool hasStarted = layerSteps.Any(step => step.AttemptCount > 0 || step.LifecycleState != DebugLifecycleState.Initialized);
            bool isActive = hasStarted && !isComplete;

            layerState.IsComplete = isComplete;
            layerState.IsActive = isActive;
        }
    }

    private static bool IsStepInFinalLifecycleState(StepNodeState stepState)
    {
        return stepState.LifecycleState is DebugLifecycleState.Complete or DebugLifecycleState.Error or DebugLifecycleState.Timeout or DebugLifecycleState.Skipped;
    }

    private sealed record ExecutionLayerPlan(string Key, int Order, int[] StepIds);

    private static string MapStatePrefix(DebugLifecycleState state)
    {
        return state switch
        {
            DebugLifecycleState.Complete => "[PASS]",
            DebugLifecycleState.Error => "[FAIL]",
            DebugLifecycleState.Timeout => "[TIMEOUT]",
            DebugLifecycleState.Skipped => "[SKIPPED]",
            DebugLifecycleState.WaitingForRetry => "[RETRY]",
            _ => "[STATE]"
        };
    }

    private static DebugLogLevel MapLogLevel(DebugLifecycleState state)
    {
        return state switch
        {
            DebugLifecycleState.Error => DebugLogLevel.Error,
            DebugLifecycleState.Timeout => DebugLogLevel.Warning,
            DebugLifecycleState.WaitingForRetry => DebugLogLevel.Warning,
            _ => DebugLogLevel.Information
        };
    }
}