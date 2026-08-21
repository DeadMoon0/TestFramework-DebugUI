using System;
using Axiom.State.Reducers;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// Applies the comparison against the last run of the same test that passed.
/// </summary>
/// <remarks>
/// Not scoped to <see cref="ComparisonState"/>, and deliberately so: it writes only that slice, but
/// it has to read the selection out of <see cref="RunsState"/> to decide whether the result it was
/// handed still belongs to the run on screen. A scope would hide exactly the field the decision
/// depends on.
/// </remarks>
public sealed class ComparisonReducer : Reducer<MainState>
{
    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public ComparisonReducer()
    {
        On(ComparisonActions.SetComparison, Apply);
    }

    /// <summary>
    /// Records a comparison, unless the selection has moved on since it was asked for.
    /// </summary>
    /// <remarks>
    /// Reading a journal takes long enough for someone to click through several runs, and each
    /// request answers whenever it answers. Dropping a result that names a run other than the
    /// selected one is what stops an earlier run's verdict landing on a later run's values.
    /// </remarks>
    private static MainState Apply(MainState state, string sessionId, RunComparison comparison)
    {
        if (comparison is null || !string.Equals(state.Runs.SelectedSessionId, sessionId, StringComparison.Ordinal))
            return state;

        return state with
        {
            Board = state.Board with
            {
                Comparison = new ComparisonState
                {
                    Values = comparison.Values,
                    Timing = comparison.Timing
                }
            }
        };
    }
}
