using Axiom.State.Selectors;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// The ways the UI asks how this run compares with the last one that passed.
/// </summary>
public static class ComparisonSelectors
{
    /// <summary>Both halves of the comparison at once, for whoever needs them together.</summary>
    public static readonly Selector<MainState, ComparisonState> SelectComparison =
        MainSelectors.SelectBoard.Then(Selector.Property((BoardState board) => board.Comparison));

    /// <summary>How the run's values compare.</summary>
    public static readonly Selector<MainState, ValueDiff> SelectValues =
        SelectComparison.Then(Selector.Property((ComparisonState comparison) => comparison.Values));

    /// <summary>How the run's step timings compare.</summary>
    public static readonly Selector<MainState, TimingDiff> SelectTiming =
        SelectComparison.Then(Selector.Property((ComparisonState comparison) => comparison.Timing));

    /// <summary>How one artifact changed, or null when it did not or nothing was compared.</summary>
    public static Selector<MainState, ValueChange?> SelectArtifactChange(string key)
        => ReadOnlySelector.Of<ValueChange?>(nameof(SelectArtifactChange), state => state.Board.Comparison.Values.ChangeForArtifact(key));

    /// <summary>How one variable changed, or null when it did not or nothing was compared.</summary>
    public static Selector<MainState, ValueChange?> SelectVariableChange(string key)
        => ReadOnlySelector.Of<ValueChange?>(nameof(SelectVariableChange), state => state.Board.Comparison.Values.ChangeForVariable(key));

    /// <summary>What kind of change one artifact saw, for a badge with no room for the detail.</summary>
    public static Selector<MainState, ValueChangeKind?> SelectArtifactChangeKind(string key)
        => ReadOnlySelector.Of(nameof(SelectArtifactChangeKind), state => state.Board.Comparison.Values.ForArtifact(key));

    /// <summary>What kind of change one variable saw, for a badge with no room for the detail.</summary>
    public static Selector<MainState, ValueChangeKind?> SelectVariableChangeKind(string key)
        => ReadOnlySelector.Of(nameof(SelectVariableChangeKind), state => state.Board.Comparison.Values.ForVariable(key));
}
