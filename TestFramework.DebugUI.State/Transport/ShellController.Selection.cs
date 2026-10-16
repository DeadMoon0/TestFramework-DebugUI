using System;
using System.Linq;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

// Switching what is shown: picking a run or a step, and rebuilding the board and its comparison from
// whatever record of the run exists.
public sealed partial class ShellController
{
    /// <summary>
    /// Shows a run, rebuilding its board from whatever record of it exists.
    /// </summary>
    /// <remarks>
    /// Only the selected run is projected, so switching means replaying: from the events kept in
    /// memory for a live run, and from the journal for one that was recorded. A run with neither is
    /// listed but has nothing to show, which is reported rather than left as an empty board.
    /// </remarks>
    public void SelectRun(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ObjectDisposedException.ThrowIf(disposed, this);

        histories.TryGetValue(sessionId, out RunHistory? history);

        // Whatever the reader has queued goes in first, while another run is still on the board; then the
        // selection and the replayed history, with nothing the reader accepts meanwhile let in between.
        // Otherwise an event arriving during the switch would land on the fresh board ahead of the history,
        // and the history - older by sequence - would be dropped as already shown. The snapshot is taken
        // inside, so an event the reader recorded before it is in the replay, and one recorded after it is
        // newer than everything the replay carries.
        ingest.FlushThen(() =>
        {
            store.Dispatch(MainActions.SelectRun, sessionId);

            if (history is not null)
                store.Dispatch(MainActions.IngestBatch, history.Snapshot());
        });

        if (history is not null)
        {
            RefreshDiff(sessionId);

            if (!history.Complete)
            {
                Report(Notice(
                    FeedSeverity.Warning,
                    "Only the recent part of this run could be shown.",
                    $"It produced more than {RetainedEventsPerRun:N0} events while it was not selected. Once it finishes, opening it again replays the whole run from its journal.",
                    FeedSource.Run,
                    sessionId));
            }

            return;
        }

        ReplayRecorded(sessionId);
    }

    /// <summary>
    /// Shows one step's detail.
    /// </summary>
    public void SelectStep(string stageName, int stepId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        store.Dispatch(BoardActions.SelectStep, new StepSelection { StageName = stageName, StepId = stepId });
    }

    private void ReplayRecorded(string sessionId)
    {
        if (runsDirectory is null)
            return;

        AvailableRun? recorded = JournalRunEventSource
            .ListRuns(runsDirectory)
            .FirstOrDefault(run => string.Equals(run.SessionId, sessionId, StringComparison.Ordinal));

        if (recorded is null)
        {
            Report(Notice(
                FeedSeverity.Warning,
                "This run has nothing to show.",
                "It was not recorded, and the events it sent while it was attached are no longer held.",
                FeedSource.Run,
                sessionId));

            return;
        }

        JournalRunEventSource source = new(recorded.JournalPath);
        source.EnvelopeReceived += ingest.Accept;
        source.Notice += Report;

        try
        {
            source.Start();
        }
        catch (Exception e)
        {
            Log.Write(e);
            Report(Notice(FeedSeverity.Error, "The recorded run could not be replayed.", e.Message, FeedSource.Journal, sessionId));
        }
        finally
        {
            source.EnvelopeReceived -= ingest.Accept;
            source.Notice -= Report;
            ingest.Flush();
        }

        RefreshDiff(sessionId);
    }

    /// <summary>
    /// Asks for the selected run's comparison against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// The work itself is <see cref="ComparisonEffects"/>. What is left here is the timing: a
    /// comparison needs the run's graph, and the graph does not exist when the selection changes —
    /// it is replayed from the retained events or from the journal first. This is called once that
    /// replay has been flushed, which is the only point at which there is something to compare.
    /// </remarks>
    public void RefreshDiff(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (disposed)
            return;

        store.Dispatch(ComparisonActions.Refresh, sessionId);
    }
}
