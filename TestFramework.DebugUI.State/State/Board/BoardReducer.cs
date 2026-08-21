using Axiom.State.Reducers;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// Applies the transitions that change what the board is showing.
/// </summary>
/// <remarks>
/// The graph itself is not written here. It is replaced by <see cref="MainReducer"/> as events
/// arrive, because an event updates the run's summary in the same transition.
/// </remarks>
public sealed class BoardReducer : Reducer<MainState>
{
    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public BoardReducer()
    {
        Scope(MainSelectors.SelectBoard)
            .On(BoardActions.SelectStep, (state, selection) => state with { SelectedStep = selection });
    }
}
