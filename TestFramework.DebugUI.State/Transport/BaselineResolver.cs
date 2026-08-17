using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// Finds the last run of a test that passed, and compares the current run against it.
/// </summary>
/// <remarks>
/// <para>
/// The selection rules live in <see cref="RunBaselineSelector"/> and are pure. This is the part that
/// touches disk: it walks the candidates newest first, reads each journal, and stops at the first run
/// that is fit to be a baseline. That loop is bounded, because a test whose last dozen runs all failed
/// is not worth reading a dozen journals for.
/// </para>
/// <para>
/// Results are cached per run. A run's own values are settled once it has finished and the baseline is
/// a historical fact, so recomputing on every selection would re-read the same files to reach the same
/// answer. Live runs are not cached — their values are still arriving.
/// </para>
/// </remarks>
public sealed class BaselineResolver(string? runsDirectory)
{
    private readonly ConcurrentDictionary<string, ValueDiff> cache = new(StringComparer.Ordinal);

    /// <summary>Forgets what was computed, so a test can observe a changed journal.</summary>
    public void Clear() => cache.Clear();

    /// <summary>
    /// Compares a run against the last run of the same test that passed.
    /// </summary>
    /// <param name="current">The run on screen.</param>
    /// <param name="currentGraph">Its projected graph, which is where its values come from.</param>
    /// <param name="runs">Every run the picker knows about.</param>
    /// <returns>
    /// The comparison, or a <see cref="ValueDiff"/> carrying the reason there is none. Never null and
    /// never throws: a failure to compare must not take the board down with it.
    /// </returns>
    public ValueDiff Resolve(RunSummary current, RunGraph currentGraph, IReadOnlyList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(currentGraph);
        ArgumentNullException.ThrowIfNull(runs);

        if (!current.IsLive && cache.TryGetValue(current.SessionId, out ValueDiff? cached))
            return cached;

        ValueDiff diff = Compute(current, currentGraph, runs);

        if (!current.IsLive)
            cache[current.SessionId] = diff;

        return diff;
    }

    private ValueDiff Compute(RunSummary current, RunGraph currentGraph, IReadOnlyList<RunSummary> runs)
    {
        if (runsDirectory is null)
            return RunBaselineSelector.Unavailable("No journal directory is configured, so earlier runs cannot be read.");

        if (string.IsNullOrWhiteSpace(current.FullyQualifiedName))
        {
            return RunBaselineSelector.Unavailable(
                "This run did not record which test produced it, so there is no way to tell which earlier runs are the same test.");
        }

        ImmutableList<RunSummary> candidates = RunBaselineSelector.CandidatesFor(runs, current);

        if (candidates.Count == 0)
            return RunBaselineSelector.Unavailable("No earlier run of this test has been recorded yet.");

        ImmutableList<AvailableRun> recorded;

        try
        {
            recorded = JournalRunEventSource.ListRuns(runsDirectory);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return RunBaselineSelector.Unavailable("The recorded runs could not be listed, so no baseline could be read.");
        }

        Dictionary<string, string> journals = new(StringComparer.Ordinal);
        foreach (AvailableRun run in recorded)
            journals.TryAdd(run.SessionId, run.JournalPath);

        int read = 0;

        foreach (RunSummary candidate in candidates)
        {
            if (!journals.TryGetValue(candidate.SessionId, out string? journalPath))
                continue;

            RunGraph replayed;

            try
            {
                replayed = RunBaselineSelector.Replay(JournalRunEventSource.ReadEnvelopes(journalPath));
            }
            catch (Exception e)
            {
                // One unreadable journal is not a reason to stop looking; an older run may still serve.
                Debug.WriteLine(e);
                continue;
            }

            read++;

            if (!RunBaselineSelector.IsUsableBaseline(replayed))
                continue;

            return RunBaselineSelector.Compare(currentGraph, RunBaselineSelector.BaselineFrom(candidate, replayed));
        }

        // Said with the number actually examined, so "no baseline" cannot be confused with "this test
        // has never run before".
        return RunBaselineSelector.Unavailable(read switch
        {
            0 => "The earlier runs of this test are listed but their journals could not be read.",
            1 => "The previous run of this test did not pass, so there is no known-good state to compare against.",
            _ => $"None of the last {read} runs of this test passed, so there is no known-good state to compare against."
        });
    }
}
