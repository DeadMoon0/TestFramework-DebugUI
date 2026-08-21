using Axiom.State;
using Axiom.State.Store.Builder;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Assembles the store out of the slices in this tree.
/// </summary>
/// <remarks>
/// One list of reducers, in one place. The window used to name them at its own construction and every
/// test named a shorter list of its own, which worked only while there was a single reducer: the
/// moment a slice took over an action, a store built from the shorter list would answer a dispatch
/// with <c>NoReducerFoundForActionException</c> at run time and nothing would catch it before then.
/// <para>
/// The effects are optional because they are the parts that reach outside the process — reading
/// journals, starting a test host. A test that is checking a state transition passes neither and gets
/// a store that reduces and nothing more.
/// </para>
/// </remarks>
public static class MainStore
{
    /// <summary>
    /// Registers every reducer, and the effects whose collaborators were supplied.
    /// </summary>
    /// <param name="baselines">Compares a run with earlier ones, or null to leave comparisons unhandled.</param>
    /// <param name="rerunner">Builds and runs a test again, or null to leave re-runs unhandled.</param>
    public static IStateStoreBuilder<MainState> Create(BaselineResolver? baselines = null, TestRerunner? rerunner = null)
    {
        IStateStoreBuilder<MainState> builder = StateStore<MainState>.Create()
            .AddReducer(new MainReducer())
            .AddReducer(new RunsReducer())
            .AddReducer(new BoardReducer())
            .AddReducer(new ComparisonReducer())
            .AddReducer(new ShellReducer())
            .AddReducer(new FeedReducer());

        if (rerunner is not null)
            builder = builder.AddEffects(new RunsEffects(rerunner));

        if (baselines is not null)
            builder = builder.AddEffects(new ComparisonEffects(baselines));

        return builder;
    }
}
