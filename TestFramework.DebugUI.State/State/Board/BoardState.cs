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

    /// <summary>
    /// The sequence number of the last event projected onto this board, or <see langword="null"/> before the first.
    /// </summary>
    /// <remarks>
    /// Events can reach the board twice: selecting a live run replays the events it kept, and the newest of those may
    /// also still be on their way through the ingest queue. Projections append, so a second copy doubles log lines,
    /// assertion entries and widgets. A fresh board - which is what selecting a run makes - starts without one, so a
    /// replay is projected.
    /// </remarks>
    public long? LastSeq = null;
}
