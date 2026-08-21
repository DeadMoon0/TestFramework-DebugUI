using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Axiom.State.Effects;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// Works out how the run on screen compares with the last run of the same test that passed.
/// </summary>
/// <remarks>
/// <para>
/// This was a <c>Task.Run</c> inside the shell controller, which read the store, computed off-thread
/// and dispatched the answer itself. As an effect the same work is declared against the action that
/// asks for it: the request is one dispatch, the answer is another, and both are visible to anything
/// watching the store instead of happening beside it.
/// </para>
/// <para>
/// Off the calling thread, because it reads journals: a comparison is worth waiting for but never
/// worth freezing the window for. The board is already on screen by the time this starts, so the
/// result arrives as a later state change and the rail badges itself when it does.
/// </para>
/// </remarks>
public sealed class ComparisonEffects : Effects<MainState>
{
    /// <summary>
    /// Registers the effect against the resolver it computes with.
    /// </summary>
    /// <param name="baselines">Reads earlier runs off disk and compares them with this one.</param>
    public ComparisonEffects(BaselineResolver baselines)
    {
        ArgumentNullException.ThrowIfNull(baselines);

        On(ComparisonActions.Refresh, (state, sessionId) => Compare(baselines, state, sessionId));
    }

    private EffectResult<MainState> Compare(BaselineResolver baselines, MainState state, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return DoNothing();

        RunSummary? run = state.Runs.All.Find(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal));

        if (run is null)
            return DoNothing();

        // Read out of the state now, on the dispatch thread, and captured. The work below runs later
        // and the state will have moved on by then; comparing against whatever it says at that point
        // would be comparing a run with a graph that is no longer the one it was asked about.
        RunGraph graph = state.Board.ActiveRun;
        ImmutableList<RunSummary> runs = state.Runs.All;

        return DoAction(
            () => Task.Run(() => baselines.Resolve(run, graph, runs)),
            comparison => Do(ComparisonActions.SetComparison, sessionId, comparison),

            // Reported as an unavailable comparison rather than dropped, so the panel can say why it
            // has nothing instead of looking like it is still thinking.
            _ => Do(
                ComparisonActions.SetComparison,
                sessionId,
                RunComparison.Unavailable("The comparison against an earlier run could not be computed.")));
    }
}
