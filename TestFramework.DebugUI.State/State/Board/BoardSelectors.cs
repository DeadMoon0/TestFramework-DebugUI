using System.Linq;
using Axiom.State.Selectors;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// The ways the UI asks about the run on screen.
/// </summary>
/// <remarks>
/// The derived lanes here are the ones that were duplicated most damagingly. "Is a step held at a
/// breakpoint" was written out in the run bar and again in the window, as two copies of the same
/// nested <c>Any</c> over stages and steps — and the window's copy is what decides whether the app
/// comes to the front, so the two drifting apart would mean a run that pauses without saying so.
/// </remarks>
public static class BoardSelectors
{
    /// <summary>The selected run's graph.</summary>
    public static readonly Selector<MainState, RunGraph> SelectActiveRun =
        MainSelectors.SelectBoard.Then(Selector.Property((BoardState board) => board.ActiveRun));

    /// <summary>The step whose detail is open, or null when none is.</summary>
    public static readonly Selector<MainState, StepSelection?> SelectSelectedStep =
        MainSelectors.SelectBoard.Then(Selector.Property((BoardState board) => board.SelectedStep));

    /// <summary>Whether the graph holds no stages yet.</summary>
    public static readonly Selector<MainState, bool> SelectIsEmpty =
        ReadOnlySelector.Of(nameof(SelectIsEmpty), static state => state.Board.ActiveRun.Stages.Count == 0);

    /// <summary>Whether any step of the run on screen is waiting to be released.</summary>
    public static readonly Selector<MainState, bool> SelectIsWaitingAtBreakpoint =
        ReadOnlySelector.Of(nameof(SelectIsWaitingAtBreakpoint), static state =>
            state.Board.ActiveRun.Stages.Any(stage => stage.Steps.Any(step => step.IsWaitingAtBreakpoint)));

    /// <summary>Whether any step of the run on screen failed or timed out.</summary>
    public static readonly Selector<MainState, bool> SelectHasFailure =
        ReadOnlySelector.Of(nameof(SelectHasFailure), static state =>
            state.Board.ActiveRun.Stages.Any(stage => stage.Steps.Any(
                step => step.Lifecycle is DebugLifecycleState.Error or DebugLifecycleState.Timeout)));

    /// <summary>Whether the run on screen is still going.</summary>
    public static readonly Selector<MainState, bool> SelectIsRunning =
        ReadOnlySelector.Of(nameof(SelectIsRunning), static state =>
            state.Runs.SelectedSessionId is not null && !state.Board.ActiveRun.IsFinished);

    /// <summary>One artifact of the run on screen, or null when it holds no such key.</summary>
    public static Selector<MainState, ArtifactNode?> SelectArtifact(string key)
        => ReadOnlySelector.Of<ArtifactNode?>(nameof(SelectArtifact), state =>
            state.Board.ActiveRun.Artifacts.TryGetValue(key, out ArtifactNode? artifact) ? artifact : null);

    /// <summary>One variable of the run on screen, or null when it holds no such key.</summary>
    public static Selector<MainState, ValueNode?> SelectVariable(string key)
        => ReadOnlySelector.Of<ValueNode?>(nameof(SelectVariable), state =>
            state.Board.ActiveRun.Variables.TryGetValue(key, out ValueNode? value) ? value : null);
}
