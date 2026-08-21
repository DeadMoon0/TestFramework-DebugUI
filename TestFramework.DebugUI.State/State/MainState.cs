using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell;

namespace TestFramework.DebugUI.State;

/// <summary>
/// The root of the UI's state.
/// </summary>
/// <remarks>
/// <para>
/// A <c>record struct</c> with public fields, which is what Axiom's reflection-based cloner walks.
/// Everything here is either a slice of the same kind or a reference to an immutable graph, so a
/// dispatch clones a handful of nodes rather than the whole run — see <see cref="RunGraph"/> for why
/// that split exists.
/// </para>
/// <para>
/// The root holds nothing of its own. It is three slices, each with its own actions, reducer,
/// effects and selectors beside it in the folder tree, so that adding a field to one of them cannot
/// widen what the others clone or bind to. Only the transitions that genuinely cross slices —
/// ingesting an event, and changing the selection — are owned here, because Axiom allows exactly one
/// reducer per action and those two write to more than one slice at once.
/// </para>
/// </remarks>
public record struct MainState()
{
    /// <summary>Which runs the UI knows about, and which one is selected.</summary>
    public RunsState Runs = new();

    /// <summary>The selected run as it is drawn: its graph, the open step, the comparison.</summary>
    public BoardState Board = new();

    /// <summary>Transport, awaited re-run, and the message feed.</summary>
    public ShellState Shell = new();
}
