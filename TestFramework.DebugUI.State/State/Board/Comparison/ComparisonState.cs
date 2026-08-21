namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// How the selected run stands against the last run of the same test that passed.
/// </summary>
/// <remarks>
/// Its own slice, beside the graph rather than inside it, because the graph is what one run reported
/// and this is a statement about two. Both halves are computed from one journal read and are cleared
/// together whenever the selection changes: a comparison left over from the previous run would badge
/// the new run's values and steps with the old run's verdict.
/// </remarks>
public record struct ComparisonState()
{
    /// <summary>How the run's values compare, or <see cref="ValueDiff.None"/> before it is known.</summary>
    public ValueDiff Values = ValueDiff.None;

    /// <summary>How the run's step timings compare, or <see cref="TimingDiff.None"/> before it is known.</summary>
    public TimingDiff Timing = TimingDiff.None;
}
