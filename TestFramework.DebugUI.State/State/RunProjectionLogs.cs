using System;
using System.Collections.Immutable;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

public static partial class RunProjection
{
    /// <summary>
    /// Appends a log entry to the attempt that produced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Logs belong to an attempt, not to a step. A step that failed twice before passing has three
    /// sets of lines, and reading them merged would make a retry look like one long confusing
    /// execution. The entry's <c>Iteration</c> is what routes it, which is also why an entry that
    /// does not identify a step is dropped: there is nowhere correct to put it.
    /// </para>
    /// <para>
    /// Appending is the one rule here that is not naturally idempotent — replaying a journal would
    /// otherwise duplicate every line — so redelivery is filtered by timestamp and message.
    /// </para>
    /// </remarks>
    public static RunGraph ApplyLogEntry(RunGraph graph, PipeLogEntrySignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        DebugLogEntry entry = signal.Entry;

        int stageIndex = IndexOfStage(graph, entry.Stage);
        if (stageIndex < 0 || entry.StepId is not int stepId)
            return graph;

        StageNode stage = graph.Stages[stageIndex];
        int stepIndex = stage.Steps.FindIndex(step => step.StepId == stepId);
        if (stepIndex < 0)
            return graph;

        StepNode step = stage.Steps[stepIndex];
        if (step.Attempts.Count == 0)
            return graph;

        // Iteration counts from one and addresses the attempt directly; without it the line belongs
        // to whichever attempt is currently open.
        int attemptIndex = entry.Iteration is int iteration
            ? iteration - 1
            : step.Attempts.Count - 1;

        if (attemptIndex < 0 || attemptIndex >= step.Attempts.Count)
            return graph;

        AttemptNode attempt = step.Attempts[attemptIndex];
        LogNode line = new()
        {
            OccurredAtUtc = entry.OccurredAtUtc,
            Level = entry.Level,
            Message = entry.Message
        };

        if (IsDuplicate(attempt, line))
            return graph;

        AttemptNode updatedAttempt = attempt with { Logs = attempt.Logs.Add(line) };
        StepNode updatedStep = step with { Attempts = step.Attempts.SetItem(attemptIndex, updatedAttempt) };
        StageNode updatedStage = stage with { Steps = stage.Steps.SetItem(stepIndex, updatedStep) };

        return graph with { Stages = graph.Stages.SetItem(stageIndex, updatedStage) };
    }

    /// <summary>
    /// Detects a redelivered line.
    /// </summary>
    /// <remarks>
    /// Only the tail is checked. A run legitimately logs the same text repeatedly — a poll loop
    /// reporting "still waiting" is the obvious case — so scanning the whole attempt would discard
    /// real lines. A redelivery, by contrast, arrives immediately after the line it repeats.
    /// </remarks>
    private static bool IsDuplicate(AttemptNode attempt, LogNode line)
    {
        if (attempt.Logs.Count == 0)
            return false;

        LogNode last = attempt.Logs[^1];

        return last.OccurredAtUtc == line.OccurredAtUtc
            && last.Level == line.Level
            && string.Equals(last.Message, line.Message, StringComparison.Ordinal);
    }
}
