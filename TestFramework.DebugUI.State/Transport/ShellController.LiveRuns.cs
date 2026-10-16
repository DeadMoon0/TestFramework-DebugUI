using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Shell;

namespace TestFramework.DebugUI.State.Transport;

// Following the runs attached over the pipe: their events, their connections, and what is kept of each
// so it can be shown when it is selected.
public sealed partial class ShellController
{
    /// <summary>
    /// Keeps the reported status level with how many runs are actually connected.
    /// </summary>
    /// <remarks>
    /// Only ever moves between listening and attached. A transport that has faulted stays faulted —
    /// that is a fact about the tool rather than about how busy it happens to be, and quietly
    /// downgrading it to "listening" because nothing is connected would hide it.
    /// </remarks>
    private void OnConnectionsChanged()
    {
        if (disposed)
            return;

        PublishTransportDetails();

        TransportStatus current = store.GetValue(state => state.Shell.Transport);

        if (current == TransportStatus.Faulted || current == TransportStatus.Idle)
            return;

        TransportStatus next = pipe.AttachedRunCount > 0 ? TransportStatus.Attached : TransportStatus.Listening;

        if (next != current)
            store.Dispatch(ShellActions.SetTransportStatus, next);
    }

    /// <summary>
    /// Puts what the transport is doing into the state, for whatever draws it.
    /// </summary>
    /// <remarks>
    /// So that no surface has to hold the transport to describe it. The count changes on every attach and
    /// detach; the other two are facts about this transport and are sent along with it rather than being
    /// fetched separately by whoever happens to need them.
    /// </remarks>
    private void PublishTransportDetails()
        => store.Dispatch(ShellActions.SetTransportDetails, new TransportDetails
        {
            AttachedRuns = pipe.AttachedRunCount,
            MaxConcurrentRuns = pipe.MaxConcurrentRuns,
            PipeName = pipe.PipeName
        });

    private void OnLiveEnvelope(DebugEnvelope envelope)
    {
        // Kept for every session, not just the selected one. An unselected run is only a name in the
        // picker until someone opens it, and these events are what the board is rebuilt from.
        histories.GetOrAdd(envelope.SessionId, _ => new RunHistory()).Add(envelope);

        // Before the events are handed on, because both things this reads have to be known by the time the
        // run's next step asks whether to stop — which is sooner than the store will have heard about it.
        WatchForBreakpointContext(envelope);

        if (store.GetValue(state => state.Shell.Transport) != TransportStatus.Attached)
            store.Dispatch(ShellActions.SetTransportStatus, TransportStatus.Attached);

        ingest.Accept(envelope);

        // A live run's values are still arriving, so comparing it mid-flight would badge values that
        // have not been assigned yet as removed. Its finish is the moment they settle, and the moment
        // the comparison is worth making.
        if (envelope.Kind == PipeSignalKind.TimelineRunFinished
            && string.Equals(store.GetValue(state => state.Runs.SelectedSessionId), envelope.SessionId, StringComparison.Ordinal))
        {
            ingest.Flush();
            RefreshDiff(envelope.SessionId);
        }

        if (envelope.Kind == PipeSignalKind.TimelineRunFinished)
            ReleaseHistory(envelope.SessionId);
    }

    /// <summary>
    /// Lets go of what was kept for a session whose connection has gone.
    /// </summary>
    /// <remarks>
    /// The finish signal already released a healthy run's history, so what this adds is the run that
    /// never sent one — a killed or crashed host, whose entry would otherwise be held for the life of
    /// the process. Under watch mode that is measured in days and thousands of runs.
    /// </remarks>
    private void OnSessionEnded(SessionEnded ended)
    {
        tests.TryRemove(ended.SessionId, out _);
        ReleaseHistory(ended.SessionId);
    }

    /// <summary>
    /// Drops a run's retained events once its journal can answer for them instead.
    /// </summary>
    /// <remarks>
    /// The events are kept so an unselected run can still be opened, and selecting a recorded run
    /// replays its journal from scratch anyway — so once a recording exists the memory is a second
    /// copy of it. Without one it is the only record there will ever be, and it stays.
    /// </remarks>
    private void ReleaseHistory(string sessionId)
    {
        if (runsDirectory is null || !JournalRunEventSource.HasRun(runsDirectory, sessionId))
            return;

        histories.TryRemove(sessionId, out _);
    }

    /// <summary>What one session has sent, kept so its board can be rebuilt when it is selected.</summary>
    private sealed class RunHistory
    {
        private readonly object gate = new();
        private readonly Queue<DebugEnvelope> envelopes = new();

        /// <summary>Whether every event this run sent is still held.</summary>
        internal bool Complete { get; private set; } = true;

        internal void Add(DebugEnvelope envelope)
        {
            lock (gate)
            {
                envelopes.Enqueue(envelope);

                while (envelopes.Count > RetainedEventsPerRun)
                {
                    envelopes.Dequeue();
                    Complete = false;
                }
            }
        }

        internal ImmutableList<DebugEnvelope> Snapshot()
        {
            lock (gate)
                return [.. envelopes];
        }
    }
}
