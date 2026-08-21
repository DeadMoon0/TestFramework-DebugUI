using System;
using System.Collections.Immutable;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

public static partial class RunProjection
{
    /// <summary>
    /// Applies a lifecycle transition for a run, stage or step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The protocol is a state machine, not a sequential narrative: transitions carry the entity
    /// they describe, so they can arrive for steps running in parallel and must be applied by
    /// address rather than to whatever ran most recently.
    /// </para>
    /// <para>
    /// A transition naming an entity this graph does not have is ignored rather than treated as an
    /// error. A consumer attaching midway through a run, or replaying a truncated journal, will see
    /// exactly that, and it must not take the projection down.
    /// </para>
    /// </remarks>
    public static RunGraph ApplyTransition(RunGraph graph, PipeEntityTransitionSignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        return signal.EntityKind switch
        {
            DebugEntityKind.Run => graph with { Lifecycle = signal.State },
            DebugEntityKind.Stage => ApplyStageTransition(graph, signal),
            DebugEntityKind.Step => ApplyStepTransition(graph, signal),
            _ => graph
        };
    }

    private static RunGraph ApplyStageTransition(RunGraph graph, PipeEntityTransitionSignal signal)
    {
        int index = IndexOfStage(graph, signal.Stage);
        if (index < 0)
            return graph;

        StageNode updated = graph.Stages[index] with { Lifecycle = signal.State };
        return graph with { Stages = graph.Stages.SetItem(index, updated) };
    }

    private static RunGraph ApplyStepTransition(RunGraph graph, PipeEntityTransitionSignal signal)
    {
        int stageIndex = IndexOfStage(graph, signal.Stage);
        if (stageIndex < 0 || signal.StepId is not int stepId)
            return graph;

        StageNode stage = graph.Stages[stageIndex];
        int stepIndex = stage.Steps.FindIndex(step => step.StepId == stepId);
        if (stepIndex < 0)
            return graph;

        StepNode updated = ApplyStepState(stage.Steps[stepIndex], signal);
        StageNode updatedStage = stage with { Steps = stage.Steps.SetItem(stepIndex, updated) };

        return graph with { Stages = graph.Stages.SetItem(stageIndex, updatedStage) };
    }

    /// <summary>Whether a state is one a step stops at.</summary>
    /// <remarks>
    /// <c>WaitingForRetry</c> is deliberately not one of them: a step between attempts has not
    /// finished, and treating it as finished would report a fraction of what it cost.
    /// </remarks>
    private static bool IsSettled(DebugLifecycleState state)
        => state is DebugLifecycleState.Complete
            or DebugLifecycleState.Error
            or DebugLifecycleState.Timeout
            or DebugLifecycleState.Skipped;

    private static StepNode ApplyStepState(StepNode step, PipeEntityTransitionSignal signal)
    {
        StepNode updated = step with
        {
            Lifecycle = signal.State,

            // OutcomeState is how a retrying step keeps the result of the attempt that just failed:
            // its Lifecycle reads WaitingForRetry while the outcome still reads Error. Falling back
            // to the existing value keeps that visible across the wait.
            Outcome = signal.OutcomeState ?? step.Outcome,

            // Any transition away from a paused state clears the flag; only a breakpoint request
            // sets it.
            IsWaitingAtBreakpoint = false,

            // The first start, not the latest: a retried step costs the run everything from its
            // first attempt onwards, and reporting only the winning attempt would hide the waiting.
            StartedAtUtc = signal.State == DebugLifecycleState.Running
                ? step.StartedAtUtc ?? signal.OccurredAtUtc
                : step.StartedAtUtc,

            // Only a settled state ends the measurement. WaitingForRetry is not an ending — the
            // step is still costing the run while it waits.
            FinishedAtUtc = IsSettled(signal.State) ? signal.OccurredAtUtc : step.FinishedAtUtc
        };

        if (signal.State == DebugLifecycleState.Running)
            updated = StartAttempt(updated, signal.OccurredAtUtc);

        if (signal.Failure is not null)
            updated = AttachFailure(updated, signal.Failure);

        return updated;
    }

    /// <summary>
    /// Opens a new attempt, unless this is a redelivery of one already recorded.
    /// </summary>
    /// <remarks>
    /// Entering <see cref="DebugLifecycleState.Running"/> is what marks a new try, including a
    /// retry. The guard is what keeps a replayed journal from inventing attempts that never
    /// happened: without it, replaying a step that ran three times would produce six.
    /// </remarks>
    private static StepNode StartAttempt(StepNode step, DateTimeOffset startedAtUtc)
    {
        AttemptNode? current = step.Attempts.Count > 0 ? step.Attempts[^1] : null;

        if (current is not null && current.StartedAtUtc == startedAtUtc)
            return step;

        return step with
        {
            Attempts = step.Attempts.Add(new AttemptNode
            {
                Attempt = step.Attempts.Count + 1,
                StartedAtUtc = startedAtUtc
            })
        };
    }

    private static StepNode AttachFailure(StepNode step, DebugFailureDetail failure)
    {
        if (step.Attempts.Count == 0)
        {
            // A failure with no attempt open means the step never reported starting — a truncated
            // journal, or a late attach. Keep the failure rather than discard it.
            return step with
            {
                Attempts = ImmutableList.Create(new AttemptNode { Attempt = failure.Attempt, Failure = failure })
            };
        }

        int index = step.Attempts.Count - 1;
        AttemptNode updated = step.Attempts[index] with { Failure = failure };

        return step with { Attempts = step.Attempts.SetItem(index, updated) };
    }

    private static int IndexOfStage(RunGraph graph, string? stageName)
    {
        if (stageName is null)
            return -1;

        return graph.Stages.FindIndex(stage => string.Equals(stage.Name, stageName, StringComparison.Ordinal));
    }
}
