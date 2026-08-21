using Axiom.State.Actions;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// The actions that change what the board is showing.
/// </summary>
public static class BoardActions
{
    /// <summary>Shows one step's detail.</summary>
    public static readonly StateAction<StepSelection> SelectStep = new(nameof(BoardActions), nameof(SelectStep));
}
