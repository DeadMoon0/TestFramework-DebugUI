using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Debugger;
using WpfStateService.Common;

namespace TestFramework.DebugUI.State;

public sealed record StepAttemptSummary(
    int AttemptNumber,
    DebugLifecycleState LifecycleState,
    bool IsActive,
    string DebugOut,
    LogEntryState? LatestLogEntry);

public static class DebugRunStateQueries
{
    public static IReadOnlyList<StageNodeState> GetOrderedStages(RunState runState)
    {
        return GetOrderedStages(runState.Stages);
    }

    public static IReadOnlyList<StageNodeState> GetOrderedStages(StateDictionary<StageNodeState> stages)
    {
        return stages.Values.OrderBy(stage => stage.Order).ToArray();
    }

    public static IReadOnlyList<StepNodeState> GetOrderedSteps(StageNodeState stageState)
    {
        return stageState.Steps.Values.OrderBy(step => step.Order).ToArray();
    }

    public static IReadOnlyList<StageLayerState> GetOrderedLayers(StageNodeState stageState)
    {
        return stageState.ExecutionLayers.Values.OrderBy(layer => layer.Order).ToArray();
    }

    public static IReadOnlyList<string> GetExecutionLayerKeys(StageNodeState stageState)
    {
        return GetOrderedLayers(stageState).Select(layer => layer.Key).ToArray();
    }

    public static string GetCurrentExecutionLayerKey(StageNodeState stageState)
    {
        return GetOrderedLayers(stageState).FirstOrDefault(layer => layer.IsActive)?.Key ?? "";
    }

    public static IReadOnlyList<StepNodeState> GetOrderedSteps(StageNodeState stageState, StageLayerState layerState)
    {
        return layerState.StepIds
            .Select(stepId => stageState.Steps.TryGetValue(stepId.ToString(), out StepNodeState? stepState) ? stepState : null)
            .Where(stepState => stepState is not null)
            .Cast<StepNodeState>()
            .OrderBy(step => step.Order)
            .ToArray();
    }

    public static IReadOnlyList<StepAttemptState> GetOrderedAttempts(StepNodeState stepState)
    {
        return stepState.Attempts.Values.OrderBy(attempt => attempt.AttemptNumber).ToArray();
    }

    public static StepAttemptState? GetLatestAttempt(StepNodeState stepState)
    {
        return stepState.Attempts.Values.OrderByDescending(attempt => attempt.AttemptNumber).FirstOrDefault();
    }

    public static StepAttemptState? GetActiveAttempt(StepNodeState stepState)
    {
        return stepState.Attempts.Values
            .Where(attempt => attempt.IsActive)
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .FirstOrDefault();
    }

    public static StepAttemptSummary? GetLatestAttemptSummary(StepNodeState stepState)
    {
        StepAttemptState? latestAttempt = GetLatestAttempt(stepState);
        return latestAttempt is null
            ? null
            : new StepAttemptSummary(
                latestAttempt.AttemptNumber,
                latestAttempt.LifecycleState,
                latestAttempt.IsActive,
                GetDebugOut(latestAttempt),
                GetLatestLogEntry(latestAttempt));
    }

    public static LogEntryState? GetLatestLogEntry(StepAttemptState attemptState)
    {
        return GetOrderedLogs(attemptState).LastOrDefault();
    }

    public static string GetDebugOut(StepAttemptState attemptState)
    {
        return string.Join(Environment.NewLine, GetOrderedLogs(attemptState).Select(entry => entry.Message));
    }

    public static IReadOnlyList<LogEntryState> GetOrderedLogs(StepAttemptState attemptState)
    {
        return attemptState.LogEntries.Values.OrderBy(entry => entry.OccurredAtUtc).ThenBy(entry => entry.Key).ToArray();
    }

    public static IReadOnlyList<LogEntryState> GetOrderedLogs(StepNodeState stepState)
    {
        return GetOrderedAttempts(stepState)
            .SelectMany(GetOrderedLogs)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Key)
            .ToArray();
    }

    public static IReadOnlyList<IOConnectionState> GetPopulatedInputs(StepNodeState stepState)
    {
        return stepState.Inputs.Values.Where(connection => connection.HasValue).ToArray();
    }

    public static bool TryGetStep(RunState? runState, string stageName, int stepId, out StepNodeState stepState)
    {
        stepState = null!;

        if (runState is null)
            return false;

        return runState.Stages.TryGetValue(stageName, out StageNodeState? stageState)
            && stageState is not null
            && stageState.Steps.TryGetValue(stepId.ToString(), out stepState!);
    }
}