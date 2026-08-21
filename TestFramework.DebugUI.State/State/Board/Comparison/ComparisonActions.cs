using Axiom.State.Actions;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// The actions that change how this run reads against the last one that passed.
/// </summary>
public static class ComparisonActions
{
    /// <summary>
    /// Asks for the comparison of one session to be worked out.
    /// </summary>
    /// <remarks>
    /// Reduced by nothing — it changes no state, it only starts
    /// <see cref="ComparisonEffects"/>. Declared with <c>dontReduce</c> so Axiom does not look for a
    /// reducer, and so a request costs neither a clone nor a notification to every binding.
    /// <para>
    /// A request rather than a consequence of <see cref="MainActions.SelectRun"/>, because the
    /// comparison has to read the run's graph and the graph does not exist yet when the selection
    /// changes: it is replayed from the retained events or from the journal first. Whoever finishes
    /// that replay asks for this.
    /// </para>
    /// </remarks>
    public static readonly StateAction<string> Refresh = new(nameof(ComparisonActions), nameof(Refresh), dontReduce: true);

    /// <summary>
    /// Records both comparisons against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One action for the pair because they are computed together off one journal read, and
    /// dispatching them separately would put the board in a state where the values had been compared
    /// and the clock had not.
    /// </para>
    /// <para>
    /// It carries the session it was computed for, and the reducer drops it if that is no longer the
    /// selected one. Someone clicking through several runs quickly would otherwise have an earlier
    /// run's comparison land on a later run's values, which is the one outcome worse than showing no
    /// comparison at all. Checked in the reducer rather than before the dispatch so there is no
    /// window between the two: the selection cannot move while the transition is being applied.
    /// </para>
    /// </remarks>
    public static readonly StateAction<string, RunComparison> SetComparison =
        new(nameof(ComparisonActions), nameof(SetComparison));
}
