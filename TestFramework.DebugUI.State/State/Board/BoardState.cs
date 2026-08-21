using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// What the board is showing: the selected run's graph, and the step opened out of it.
/// </summary>
/// <remarks>
/// Only the selected run is projected in full. Every other session contributes just a
/// <see cref="RunSummary"/> to <see cref="RunsState.All"/>, which is what keeps a suite running
/// twenty timelines at once from inflating the per-dispatch clone.
/// </remarks>
public record struct BoardState()
{
    /// <summary>The graph of the selected run.</summary>
    public RunGraph ActiveRun = RunGraph.Empty;

    /// <summary>The step whose detail is shown, or null when none is.</summary>
    public StepSelection? SelectedStep = null;

    /// <summary>How this run stands against the last run of the same test that passed.</summary>
    public ComparisonState Comparison = new();
}
