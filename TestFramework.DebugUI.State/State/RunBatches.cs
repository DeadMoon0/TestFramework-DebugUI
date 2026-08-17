using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Runs gathered into the bursts they arrived in.
/// </summary>
/// <remarks>
/// <para>
/// Runs are not produced one at a time. A suite is run and fifty of them appear inside a second, and a
/// list that shows fifty timestamps two milliseconds apart is spending a column on a number that is the
/// same for all of them. Grouped, that reads as one heading — "51 runs, 17:20" — and the rows under it
/// can spend their width on what actually differs.
/// </para>
/// <para>
/// Split on a gap rather than on the clock. A calendar boundary would cut one suite in half whenever it
/// happened to straddle a minute, and would join this morning's run to yesterday's if both fell in the
/// same hour of the day.
/// </para>
/// </remarks>
public static class RunBatches
{
    /// <summary>
    /// How long a silence ends a burst.
    /// </summary>
    /// <remarks>
    /// Two minutes. Long enough that a suite pausing on a slow test stays one batch, short enough that
    /// coming back after reading a failure starts a new one.
    /// </remarks>
    public static readonly TimeSpan DefaultGap = TimeSpan.FromMinutes(2);

    /// <summary>Gathers runs into bursts, newest burst first and newest run first within each.</summary>
    public static ImmutableList<RunBatch> Of(ImmutableList<RunSummary> runs, TimeSpan? gap = null)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Count == 0)
            return [];

        TimeSpan silence = gap ?? DefaultGap;

        List<RunSummary> ordered = [.. runs.OrderByDescending(run => run.StartedAtUtc)];
        List<RunBatch> batches = [];
        List<RunSummary> current = [ordered[0]];

        for (int index = 1; index < ordered.Count; index++)
        {
            // Measured against the run before it rather than against the batch's start, so a long
            // uninterrupted stream stays one batch however long it goes on.
            if (ordered[index - 1].StartedAtUtc - ordered[index].StartedAtUtc > silence)
            {
                batches.Add(new RunBatch { Runs = [.. current] });
                current = [];
            }

            current.Add(ordered[index]);
        }

        batches.Add(new RunBatch { Runs = [.. current] });

        return [.. batches];
    }
}

/// <summary>One burst of runs.</summary>
public sealed record RunBatch
{
    /// <summary>Gets the runs, newest first.</summary>
    public required ImmutableList<RunSummary> Runs { get; init; }

    /// <summary>Gets when the burst began.</summary>
    public DateTimeOffset StartedAtUtc => Runs.Min(run => run.StartedAtUtc);

    /// <summary>Gets when the last run in the burst began.</summary>
    public DateTimeOffset LastStartedAtUtc => Runs.Max(run => run.StartedAtUtc);

    /// <summary>Gets how the burst stands, taken from its worst run.</summary>
    public RunHealth Health => RunTree.WorstOf(Runs);

    /// <summary>
    /// Gets how many of the runs have never been opened.
    /// </summary>
    /// <remarks>
    /// Counted for the heading, so the rows underneath do not each have to carry a sentence saying they
    /// are recordings nobody has replayed yet. Said once, it is context; said fifty times, it is noise.
    /// </remarks>
    public int NotOpened => Runs.Count(run => run.Health == RunHealth.Unknown);
}
