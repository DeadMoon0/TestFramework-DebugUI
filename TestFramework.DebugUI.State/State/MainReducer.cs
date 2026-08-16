using System;
using System.Collections.Immutable;
using Axiom.State.Reducers;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Applies debug events to the UI state.
/// </summary>
/// <remarks>
/// The reducer decides <em>where</em> an event goes; <see cref="RunProjection"/> decides what it
/// means. Keeping those apart is what lets the projection rules be tested exhaustively without a
/// store, and what lets a replayed journal and a live stream share one implementation.
/// </remarks>
public sealed class MainReducer : Reducer<MainState>
{
    /// <summary>Bounds the feed so a long session cannot grow it without limit.</summary>
    private const int MaxFeedEntries = 500;

    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public MainReducer()
    {
        On(RunActions.IngestBatch, (state, envelopes) => Ingest(state, envelopes));
        On(RunActions.SelectRun, (state, sessionId) => Select(state, sessionId));
        On(RunActions.AppendFeedEntry, (state, entry) => AppendFeed(state, entry));
        On(RunActions.ClearUnreadFeed, state => state with { Shell = state.Shell with { UnreadFeedCount = 0 } });
        On(RunActions.SetTransportStatus, (state, status) => state with { Shell = state.Shell with { Transport = status } });
    }

    private static MainState Ingest(MainState state, ImmutableList<DebugEnvelope> envelopes)
    {
        if (envelopes is null || envelopes.Count == 0)
            return state;

        foreach (DebugEnvelope envelope in envelopes)
            state = IngestOne(state, envelope);

        return state;
    }

    private static MainState IngestOne(MainState state, DebugEnvelope envelope)
    {
        IPipeSignal signal;
        try
        {
            signal = DebugEnvelopeCodec.Unwrap(envelope);
        }
        catch (Exception)
        {
            // A malformed payload is a transport or version problem, not a reason to lose the run
            // that is already on screen. The transport reports it to the feed.
            return state;
        }

        state = TrackSession(state, envelope, signal);

        // Every session is tracked, but only the selected one is projected in full. That is what
        // keeps twenty parallel runs from each dragging a graph through the per-dispatch clone.
        if (!string.Equals(state.SelectedSessionId, envelope.SessionId, StringComparison.Ordinal))
            return state;

        return state with { ActiveRun = Project(state.ActiveRun, signal) };
    }

    private static RunGraph Project(RunGraph graph, IPipeSignal signal) => signal switch
    {
        PipeInitTimelineRunSignal init => RunProjection.ApplyInit(init),
        PipeEntityTransitionSignal transition => RunProjection.ApplyTransition(graph, transition),
        PipeValueUpdateSignal value => RunProjection.ApplyValueUpdate(graph, value),
        PipeLogEntrySignal log => RunProjection.ApplyLogEntry(graph, log),
        PipeAssertionSignal assertion => RunProjection.ApplyAssertion(graph, assertion),
        PipeBreakpointHitRequestSignal breakpoint => RunProjection.ApplyBreakpointHit(graph, breakpoint),
        PipeTimelineRunFinishedSignal finished => RunProjection.ApplyRunFinished(graph, finished),
        _ => graph
    };

    private static MainState TrackSession(MainState state, DebugEnvelope envelope, IPipeSignal signal)
    {
        int index = state.Runs.FindIndex(run => string.Equals(run.SessionId, envelope.SessionId, StringComparison.Ordinal));

        if (index < 0)
        {
            if (signal is not PipeInitTimelineRunSignal init)
                return state;

            RunSummary summary = new()
            {
                SessionId = envelope.SessionId,
                Name = init.Name,
                StartedAtUtc = envelope.AtUtc,
                IsLive = true,
                FullyQualifiedName = init.Identity?.FullyQualifiedName,
                CanRerun = init.Identity?.CanRerun ?? false
            };

            // Newest first, so the picker's default order needs no sorting.
            state = state with { Runs = state.Runs.Insert(0, summary) };

            // Nothing selected yet means this is the first run the UI has seen; showing it beats
            // showing an empty board and making the user pick.
            return state.SelectedSessionId is null
                ? state with { SelectedSessionId = envelope.SessionId }
                : state;
        }

        RunSummary existing = state.Runs[index];
        RunSummary updated = signal switch
        {
            PipeTimelineRunFinishedSignal => existing with { IsFinished = true, IsLive = false, IsWaitingAtBreakpoint = false },
            PipeBreakpointHitRequestSignal => existing with { IsWaitingAtBreakpoint = true },

            // Any step transition means the run moved on, so it is no longer parked. Tracked on the
            // summary so the picker can badge which of several parallel runs wants attention.
            PipeEntityTransitionSignal { EntityKind: DebugEntityKind.Step } => existing with { IsWaitingAtBreakpoint = false },
            _ => existing
        };

        return ReferenceEquals(existing, updated)
            ? state
            : state with { Runs = state.Runs.SetItem(index, updated) };
    }

    private static MainState Select(MainState state, string sessionId)
    {
        if (string.Equals(state.SelectedSessionId, sessionId, StringComparison.Ordinal))
            return state;

        // The newly selected run's graph is not held anywhere, so it is rebuilt by whoever owns the
        // source — replayed from its journal, or re-requested from the transport. Clearing here
        // keeps the board from briefly showing the previous run's contents under the new run's name.
        return state with { SelectedSessionId = sessionId, ActiveRun = RunGraph.Empty };
    }

    private static MainState AppendFeed(MainState state, FeedEntry entry)
    {
        ImmutableList<FeedEntry> feed = state.Shell.Feed.Add(entry);

        if (feed.Count > MaxFeedEntries)
            feed = feed.RemoveRange(0, feed.Count - MaxFeedEntries);

        return state with
        {
            Shell = state.Shell with
            {
                Feed = feed,
                UnreadFeedCount = state.Shell.UnreadFeedCount + 1
            }
        };
    }
}
