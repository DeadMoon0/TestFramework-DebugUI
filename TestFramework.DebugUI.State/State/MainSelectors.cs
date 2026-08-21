using Axiom.State.Selectors;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell;

namespace TestFramework.DebugUI.State;

/// <summary>
/// The three lanes out of the root, which every other selector in the tree is built from.
/// </summary>
/// <remarks>
/// Only the root's own fields live here. Each slice's lanes are declared beside that slice and reach
/// the root through one of these with <c>Then</c>, so the path from <see cref="MainState"/> to a
/// slice is written once. That is what lets a slice be moved or renested by editing one line here
/// instead of every call site that binds into it.
/// </remarks>
public static class MainSelectors
{
    /// <summary>The run catalogue and the current selection.</summary>
    public static readonly Selector<MainState, RunsState> SelectRuns = Selector.Property((MainState state) => state.Runs);

    /// <summary>The selected run as it is drawn.</summary>
    public static readonly Selector<MainState, BoardState> SelectBoard = Selector.Property((MainState state) => state.Board);

    /// <summary>Everything that belongs to the shell rather than to a run.</summary>
    public static readonly Selector<MainState, ShellState> SelectShell = Selector.Property((MainState state) => state.Shell);
}
