using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFramework.DebugUI.State;
using WpfStateService.Common;

namespace TestFramework.DebugUI;

public sealed class DebugRunStateReducer(MainState mainState)
{
    public Task ApplyInitTimelineRunAsync(InitTimelineRunSignal signal)
    {
        RunState runState = new RunState
        {
            SessionId = signal.SessionId,
            Name = signal.Name,
            ProjectPath = signal.ProjectPath,
            Structure = signal.RunStructure,
            LifecycleState = DebugLifecycleState.Initialized,
            LastTransitionAtUtc = DateTimeOffset.UtcNow,
            Artifacts = new StateDictionary<ArtifactState>(),
            Variables = new StateDictionary<VariableState>(),
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
                ExecutionLayerOrder = executionLayers.Select(layer => layer.Key).ToArray(),
                CurrentExecutionLayerKey = "",
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
                    Iterations = new StateDictionary<StepAttemptState>(),
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
        return Task.CompletedTask;
    }

    public Task ApplyEntityTransitionAsync(EntityTransitionSignal signal)
    {
        if (!TryGetRun(signal.SessionId, out RunState runState))
            return Task.CompletedTask;

        switch (signal.EntityKind)
        {
            case DebugEntityKind.Run:
                ApplyLifecycle(runState, signal.State, signal.PreviousState, signal.OccurredAtUtc);
                break;
            case DebugEntityKind.Stage:
                if (!TryGetStage(runState, signal.Stage, out StageNodeState stageState))
                    return Task.CompletedTask;

                ApplyLifecycle(stageState, signal.State, signal.PreviousState, signal.OccurredAtUtc);
                break;
            case DebugEntityKind.Step:
                if (!TryGetStep(runState, signal.Stage, signal.StepId, out StageNodeState owningStage, out StepNodeState stepState))
                    return Task.CompletedTask;

                ApplyLifecycle(stepState, signal.State, signal.PreviousState, signal.OccurredAtUtc);
                stepState.IsWaitingAtBreakpoint = false;

                if (signal.State == DebugLifecycleState.Running)
                {
                    stepState.AttemptCount++;
                    StepAttemptState iterationState = EnsureIteration(stepState, stepState.AttemptCount, signal.OccurredAtUtc);
                    PopulateStepInputs(runState, signal.Stage!, signal.StepId!.Value, stepState);
                    iterationState.LifecycleState = DebugLifecycleState.Running;
                    AddFrameworkLogEntry(stepState, iterationState, signal.OccurredAtUtc, DebugLogLevel.Information, "StepRunning", stepState.AttemptCount == 1 ? $"Executing Step: {GetStepDisplayName(stepState)}" : $"Executing Step Iteration {stepState.AttemptCount}: {GetStepDisplayName(stepState)}");
                }

                if (signal.PreviousState == DebugLifecycleState.Running && TryGetLatestIteration(stepState, out StepAttemptState latestIteration))
                {
                    latestIteration.IsActive = false;
                    latestIteration.FinishedAtUtc = signal.OccurredAtUtc;
                    latestIteration.LifecycleState = signal.OutcomeState ?? signal.State;
                    AddFrameworkLogEntry(stepState, latestIteration, signal.OccurredAtUtc, signal.State == DebugLifecycleState.WaitingForRetry ? DebugLogLevel.Warning : MapLogLevel(signal.OutcomeState ?? signal.State), "StepOutcome", $"{MapStatePrefix(signal.OutcomeState ?? signal.State)}  {GetStepDisplayName(stepState)}");
                }

                stepState.State = MapStepState(signal.State, signal.OutcomeState, stepState.State);
                RefreshStageExecutionLayers(owningStage);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(signal.EntityKind), signal.EntityKind, null);
        }

        return Task.CompletedTask;
    }

    public Task ApplyLogEntryAsync(LogEntrySignal signal)
    {
        if (!TryGetRun(signal.SessionId, out RunState runState))
            return Task.CompletedTask;

        if (!TryGetStep(runState, signal.Entry.Stage, signal.Entry.StepId, out _, out StepNodeState stepState))
            return Task.CompletedTask;

        int iterationNumber = signal.Entry.Iteration ?? 0;
        if (iterationNumber <= 0)
            return Task.CompletedTask;

        StepAttemptState iterationState = EnsureIteration(stepState, iterationNumber, signal.Entry.OccurredAtUtc);
        string key = iterationState.LogEntries.Count.ToString();
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
            IterationNumber = signal.Entry.Iteration,
            AssertionScope = signal.Entry.AssertionScope ?? ""
        };

        AppendIterationLog(stepState, iterationState, entryState);

        return Task.CompletedTask;
    }

    public Task ApplyAssertionAsync(AssertionSignal signal)
    {
        if (!TryGetRun(signal.SessionId, out RunState runState))
            return Task.CompletedTask;

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

        return Task.CompletedTask;
    }

    public Task ApplyValueUpdateAsync(ValueUpdateSignal signal)
    {
        if (!TryGetRun(signal.SessionId, out RunState runState))
            return Task.CompletedTask;

        switch (signal.ValueKind)
        {
            case DebugValueKind.Variable:
                VariableState variableState = new VariableState { Key = signal.Name, Envelope = signal.Envelope };
                Upsert(runState.Variables, variableState.Key, variableState);

                if (TryGetStep(runState, signal.Stage, signal.StepId, out _, out StepNodeState variableStepState))
                {
                    UpsertConnectionValue(variableStepState, IOConnectionDirection.Output, StepIOKind.Variable, variableState.Key, variableState.Envelope);
                    if (TryGetActiveIteration(variableStepState, out StepAttemptState activeIteration))
                        AddFrameworkLogEntry(variableStepState, activeIteration, signal.ObservedAtUtc, DebugLogLevel.Information, "VariableUpdate", $"Set Variable ({variableState.Key}) = {signal.Envelope.DisplayText}");
                }
                break;
            case DebugValueKind.Artifact:
                ArtifactState artifactState = new ArtifactState { Key = signal.Name, Envelope = signal.Envelope };
                Upsert(runState.Artifacts, artifactState.Key, artifactState);

                if (TryGetStep(runState, signal.Stage, signal.StepId, out _, out StepNodeState artifactStepState))
                {
                    UpsertConnectionValue(artifactStepState, IOConnectionDirection.Output, StepIOKind.Artifact, artifactState.Key, artifactState.Envelope);
                    if (TryGetActiveIteration(artifactStepState, out StepAttemptState activeIteration))
                        AddFrameworkLogEntry(artifactStepState, activeIteration, signal.ObservedAtUtc, DebugLogLevel.Information, "ArtifactUpdate", $"Set Artifact ({artifactState.Key}) = {signal.Envelope.DisplayText}");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(signal.ValueKind), signal.ValueKind, null);
        }

        return Task.CompletedTask;
    }

    public Task ApplyBreakpointHitRequestAsync(BreakpointHitRequestSignal signal)
    {
        if (!TryGetRun(signal.SessionId, out RunState runState))
            return Task.CompletedTask;

        if (!TryGetStep(runState, signal.Stage, signal.StepId, out _, out StepNodeState stepState))
            return Task.CompletedTask;

        stepState.BreakpointHitCount++;
        stepState.IsWaitingAtBreakpoint = true;
        stepState.LastBreakpointAtUtc = DateTimeOffset.UtcNow;
        if (TryGetActiveIteration(stepState, out StepAttemptState activeIteration))
            AddFrameworkLogEntry(stepState, activeIteration, DateTimeOffset.UtcNow, DebugLogLevel.Information, "Breakpoint", $"Breakpoint Hit: {GetStepDisplayName(stepState)}");
        return Task.CompletedTask;
    }

    public Task ApplyTimelineRunFinishedAsync(TimelineRunFinishedSignal signal)
    {
        if (!TryGetRun(signal.SessionId, out RunState runState))
            return Task.CompletedTask;

        runState.IsFinished = true;
        runState.FinishedAtUtc = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    private static void CopyValues<TState>(IEnumerable<TState> values, StateDictionary<TState> target) where TState : class
    {
        target.Clear();

        foreach (TState value in values)
        {
            switch (value)
            {
                case VariableState variableState:
                    Upsert(target, variableState.Key, value);
                    break;
                case ArtifactState artifactState:
                    Upsert(target, artifactState.Key, value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }
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

    private static bool TryGetStep(RunState runState, string? stageName, int? stepId, out StageNodeState stageState, out StepNodeState stepState)
    {
        stepState = null!;
        if (!TryGetStage(runState, stageName, out stageState) || stepId is null)
            return false;

        return stageState.Steps.TryGetValue(stepId.Value.ToString(), out stepState!);
    }

    private static void PopulateStepInputs(RunState runState, string stageName, int stepId, StepNodeState stepState)
    {
        DebugStepState debugStep = runState.Structure.Stages.First(x => x.Name == stageName).Steps[stepId];

        ResetConnectionValues(stepState.Inputs);

        foreach (StepIOEntry ioEntry in debugStep.IOContract.Inputs)
        {
            switch (ioEntry.Kind)
            {
                case StepIOKind.Variable when runState.Variables.TryGetValue(ioEntry.Key, out VariableState? variableState) && variableState is not null:
                    UpsertConnectionValue(stepState, IOConnectionDirection.Input, StepIOKind.Variable, variableState.Key, variableState.Envelope, ioEntry.Required, ioEntry.DeclaredType?.FullName ?? ioEntry.DeclaredType?.Name ?? "");
                    break;
                case StepIOKind.Artifact when runState.Artifacts.TryGetValue(ioEntry.Key, out ArtifactState? artifactState) && artifactState is not null:
                    UpsertConnectionValue(stepState, IOConnectionDirection.Input, StepIOKind.Artifact, artifactState.Key, artifactState.Envelope, ioEntry.Required, ioEntry.DeclaredType?.FullName ?? ioEntry.DeclaredType?.Name ?? "");
                    break;
            }
        }
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

    private static StepAttemptState EnsureIteration(StepNodeState stepState, int iterationNumber, DateTimeOffset startedAtUtc)
    {
        string key = iterationNumber.ToString();
        if (!stepState.Iterations.TryGetValue(key, out StepAttemptState? iterationState) || iterationState is null)
        {
            iterationState = new StepAttemptState
            {
                Key = key,
                Name = $"Iteration {iterationNumber}",
                IterationNumber = iterationNumber,
                StartedAtUtc = startedAtUtc,
                IsActive = true,
                LifecycleState = DebugLifecycleState.Running,
                DebugOut = "",
                LogEntries = new StateDictionary<LogEntryState>()
            };
            stepState.Iterations[key] = iterationState;
        }

        iterationState.IsActive = true;
        iterationState.StartedAtUtc ??= startedAtUtc;
        iterationState.LifecycleState = DebugLifecycleState.Running;
        return iterationState;
    }

    private static void AddFrameworkLogEntry(StepNodeState stepState, StepAttemptState iterationState, DateTimeOffset occurredAtUtc, DebugLogLevel level, string eventName, string message)
    {
        LogEntryState entryState = new()
        {
            Key = iterationState.LogEntries.Count.ToString(),
            OccurredAtUtc = occurredAtUtc,
            Level = level,
            EventName = eventName,
            Message = message,
            IndentLevel = 0,
            StageName = stepState.StageName,
            StepId = stepState.StepId,
            IterationNumber = iterationState.IterationNumber,
            AssertionScope = ""
        };

        AppendIterationLog(stepState, iterationState, entryState);
    }

    private static void AppendIterationLog(StepNodeState stepState, StepAttemptState iterationState, LogEntryState entryState)
    {
        Upsert(iterationState.LogEntries, entryState.Key, entryState);
        iterationState.LatestLogEntry = entryState;
        iterationState.DebugOut = string.IsNullOrEmpty(iterationState.DebugOut)
            ? entryState.Message
            : iterationState.DebugOut + Environment.NewLine + entryState.Message;
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

    private static bool TryGetActiveIteration(StepNodeState stepState, out StepAttemptState iterationState)
    {
        iterationState = stepState.Iterations.Values
            .Where(candidate => candidate.IsActive)
            .OrderByDescending(candidate => candidate.IterationNumber)
            .FirstOrDefault()!;
        return iterationState is not null;
    }

    private static bool TryGetLatestIteration(StepNodeState stepState, out StepAttemptState iterationState)
    {
        iterationState = stepState.Iterations.Values
            .OrderByDescending(candidate => candidate.IterationNumber)
            .FirstOrDefault()!;
        return iterationState is not null;
    }

    private static void RefreshStageExecutionLayers(StageNodeState stageState)
    {
        string currentExecutionLayerKey = "";

        foreach (StageLayerState layerState in stageState.ExecutionLayers.Values.OrderBy(layer => layer.Order))
        {
            StepNodeState[] layerSteps = layerState.StepIds.Select(stepId => stageState.Steps[stepId.ToString()]).ToArray();
            bool isComplete = layerSteps.Length > 0 && layerSteps.All(IsStepInFinalLifecycleState);
            bool hasStarted = layerSteps.Any(step => step.AttemptCount > 0 || step.LifecycleState != DebugLifecycleState.Initialized);
            bool isActive = hasStarted && !isComplete;

            layerState.IsComplete = isComplete;
            layerState.IsActive = isActive;

            if (string.IsNullOrEmpty(currentExecutionLayerKey) && isActive)
                currentExecutionLayerKey = layerState.Key;
        }

        stageState.CurrentExecutionLayerKey = currentExecutionLayerKey;
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