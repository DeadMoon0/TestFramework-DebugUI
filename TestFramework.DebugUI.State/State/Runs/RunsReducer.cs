using System;
using System.Collections.Immutable;
using System.Linq;
using Axiom.State.Reducers;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// Applies the transitions that change what runs the UI knows about.
/// </summary>
/// <remarks>
/// Scoped to <see cref="RunsState"/>, so nothing in here can reach another slice by accident. The
/// two transitions that legitimately need to — ingesting an event and changing the selection — live
/// in <see cref="MainReducer"/> instead.
/// </remarks>
public sealed class RunsReducer : Reducer<MainState>
{
    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public RunsReducer()
    {
        Scope(MainSelectors.SelectRuns)
            .On(RunsActions.AddRecorded, AddRecorded);
    }

    /// <summary>
    /// Merges runs found on disk into the picker.
    /// </summary>
    /// <remarks>
    /// A live run records itself as it goes, so the same session is both attached and on disk. The
    /// live copy wins: it is the one receiving events, and replacing it with the recorded summary
    /// would make a running test look finished.
    /// </remarks>
    private static RunsState AddRecorded(RunsState state, ImmutableList<RunSummary> runs)
    {
        if (runs is null || runs.Count == 0)
            return state;

        ImmutableList<RunSummary> merged = state.All;

        foreach (RunSummary run in runs)
        {
            if (merged.Exists(known => string.Equals(known.SessionId, run.SessionId, StringComparison.Ordinal)))
                continue;

            merged = merged.Add(run);
        }

        if (ReferenceEquals(merged, state.All))
            return state;

        // Newest first, so a picker showing them in order needs no sorting of its own.
        return state with { All = [.. merged.OrderByDescending(run => run.StartedAtUtc)] };
    }
}
