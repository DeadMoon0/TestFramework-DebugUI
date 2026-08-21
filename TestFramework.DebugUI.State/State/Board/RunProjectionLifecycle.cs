using System;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

public static partial class RunProjection
{
    /// <summary>
    /// Marks a step as paused at a breakpoint.
    /// </summary>
    /// <remarks>
    /// Only the request sets the flag; the step's next transition clears it, since resuming is
    /// reported as ordinary execution rather than as its own signal. Keeping the pause on the step
    /// rather than on the run is what lets the picker show which of several parallel runs is waiting
    /// for the user, and where.
    /// </remarks>
    public static RunGraph ApplyBreakpointHit(RunGraph graph, PipeBreakpointHitRequestSignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        int stageIndex = IndexOfStage(graph, signal.Stage);
        if (stageIndex < 0)
            return graph;

        StageNode stage = graph.Stages[stageIndex];
        int stepIndex = stage.Steps.FindIndex(step => step.StepId == signal.StepId);
        if (stepIndex < 0)
            return graph;

        StepNode step = stage.Steps[stepIndex];
        if (step.IsWaitingAtBreakpoint)
            return graph;

        StageNode updatedStage = stage with
        {
            Steps = stage.Steps.SetItem(stepIndex, step with { IsWaitingAtBreakpoint = true })
        };

        return graph with { Stages = graph.Stages.SetItem(stageIndex, updatedStage) };
    }

    /// <summary>
    /// Marks the run as having stopped producing events.
    /// </summary>
    /// <remarks>
    /// The run's outcome is not set here: it arrived earlier as a run transition. This records only
    /// that the stream ended, which is the difference between a run that finished and one whose
    /// producer disappeared — the latter never reaches this and is what the UI shows as aborted.
    /// Any step still shown as paused is released, because nothing is waiting on it any more.
    /// </remarks>
    public static RunGraph ApplyRunFinished(RunGraph graph, PipeTimelineRunFinishedSignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        if (graph.IsFinished)
            return graph;

        return graph with
        {
            IsFinished = true,
            Stages = graph.Stages.ConvertAll(ClearPauses)
        };
    }

    private static StageNode ClearPauses(StageNode stage)
    {
        bool anyPaused = stage.Steps.Exists(step => step.IsWaitingAtBreakpoint);
        if (!anyPaused)
            return stage;

        return stage with
        {
            Steps = stage.Steps.ConvertAll(step => step.IsWaitingAtBreakpoint
                ? step with { IsWaitingAtBreakpoint = false }
                : step)
        };
    }
}
