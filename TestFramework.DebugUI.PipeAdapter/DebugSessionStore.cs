using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal sealed class DebugSessionStore
{
    private const int MaxRetainedSessions = 20;
    private readonly object sync = new();
    private readonly Dictionary<string, StoredDebugSessionState> sessions = new(StringComparer.Ordinal);

    internal StoredDebugSignalEnvelope Append(ISignal signal)
    {
        lock (sync)
        {
            string sessionId = ResolveSessionId(signal);
            if (!sessions.TryGetValue(sessionId, out StoredDebugSessionState? sessionState))
            {
                sessionState = new StoredDebugSessionState
                {
                    Metadata = new StoredDebugSessionMetadata
                    {
                        SessionId = sessionId,
                        CreatedAtUtc = ResolveOccurredAtUtc(signal),
                        UpdatedAtUtc = ResolveOccurredAtUtc(signal),
                        LastSequenceNumber = 0
                    }
                };
                sessions.Add(sessionId, sessionState);
            }

            StoredDebugSessionMetadata metadata = sessionState.Metadata;

            metadata.UpdatedAtUtc = ResolveOccurredAtUtc(signal);
            metadata.LastSequenceNumber++;

            if (signal is InitTimelineRunSignal initSignal)
            {
                metadata.Name = initSignal.Name;
                metadata.ProjectPath = initSignal.ProjectPath;
            }

            if (signal is TimelineRunFinishedSignal)
            {
                metadata.IsFinished = true;
                metadata.FinishedAtUtc = ResolveOccurredAtUtc(signal);
            }

            StoredDebugSignalEnvelope envelope = new()
            {
                SessionId = sessionId,
                SequenceNumber = metadata.LastSequenceNumber,
                Kind = signal.Kind,
                OccurredAtUtc = ResolveOccurredAtUtc(signal),
                PersistedAtUtc = DateTimeOffset.UtcNow,
                Signal = signal
            };

            sessionState.Signals.Add(envelope);
            TrimSessions();
            return envelope;
        }
    }

    internal IReadOnlyList<StoredDebugSessionInfo> ListSessions()
    {
        lock (sync)
        {
            return sessions.Values
                .Select(sessionState => sessionState.Metadata)
                .Select(metadata => new StoredDebugSessionInfo
                {
                    SessionId = metadata.SessionId,
                    Name = metadata.Name,
                    ProjectPath = metadata.ProjectPath,
                    CreatedAtUtc = metadata.CreatedAtUtc,
                    UpdatedAtUtc = metadata.UpdatedAtUtc,
                    FinishedAtUtc = metadata.FinishedAtUtc,
                    IsFinished = metadata.IsFinished,
                    LastSequenceNumber = metadata.LastSequenceNumber
                })
                .OrderBy(info => info.CreatedAtUtc)
                .ToArray();
        }
    }

    internal IReadOnlyList<StoredDebugSignalEnvelope> LoadSignals(string sessionId)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out StoredDebugSessionState? sessionState))
                return [];

            return sessionState.Signals
                .OrderBy(envelope => envelope.SequenceNumber)
                .ToArray();
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            sessions.Clear();
        }
    }

    private static string ResolveSessionId(ISignal signal)
    {
        return signal switch
        {
            InitTimelineRunSignal initSignal => initSignal.SessionId,
            EntityTransitionSignal entityTransitionSignal => entityTransitionSignal.SessionId,
            ValueUpdateSignal valueUpdateSignal => valueUpdateSignal.SessionId,
            LogEntrySignal logEntrySignal => logEntrySignal.SessionId,
            AssertionSignal assertionSignal => assertionSignal.SessionId,
            BreakpointHitRequestSignal breakpointSignal => breakpointSignal.SessionId,
            TimelineRunFinishedSignal timelineRunFinishedSignal => timelineRunFinishedSignal.SessionId,
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal.Kind, "Unsupported signal kind for debug session storage.")
        };
    }

    private static DateTimeOffset ResolveOccurredAtUtc(ISignal signal)
    {
        return signal switch
        {
            EntityTransitionSignal entityTransitionSignal => entityTransitionSignal.OccurredAtUtc,
            ValueUpdateSignal valueUpdateSignal => valueUpdateSignal.ObservedAtUtc,
            LogEntrySignal logEntrySignal => logEntrySignal.Entry.OccurredAtUtc == default ? DateTimeOffset.UtcNow : logEntrySignal.Entry.OccurredAtUtc,
            AssertionSignal assertionSignal => assertionSignal.Entry.OccurredAtUtc == default ? DateTimeOffset.UtcNow : assertionSignal.Entry.OccurredAtUtc,
            _ => DateTimeOffset.UtcNow
        };
    }

    private void TrimSessions()
    {
        if (sessions.Count <= MaxRetainedSessions)
            return;

        foreach (string sessionId in sessions.Values
                     .Where(session => session.Metadata.IsFinished)
                     .OrderBy(session => session.Metadata.CreatedAtUtc)
                     .Select(session => session.Metadata.SessionId)
                     .ToArray())
        {
            if (sessions.Count <= MaxRetainedSessions)
                break;

            sessions.Remove(sessionId);
        }

        if (sessions.Count <= MaxRetainedSessions)
            return;

        foreach (string sessionId in sessions.Values
                     .OrderBy(session => session.Metadata.CreatedAtUtc)
                     .Select(session => session.Metadata.SessionId)
                     .ToArray())
        {
            if (sessions.Count <= MaxRetainedSessions)
                break;

            sessions.Remove(sessionId);
        }
    }

    private sealed class StoredDebugSessionState
    {
        public required StoredDebugSessionMetadata Metadata { get; init; }
        public List<StoredDebugSignalEnvelope> Signals { get; } = [];
    }

    private sealed class StoredDebugSessionMetadata
    {
        public string SessionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? FinishedAtUtc { get; set; }
        public bool IsFinished { get; set; }
        public long LastSequenceNumber { get; set; }
    }
}

internal sealed class StoredDebugSessionInfo
{
    public string SessionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public bool IsFinished { get; init; }
    public long LastSequenceNumber { get; init; }
}

internal sealed class StoredDebugSignalEnvelope
{
    public string SessionId { get; init; } = string.Empty;
    public long SequenceNumber { get; init; }
    public SignalKind Kind { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset PersistedAtUtc { get; init; }
    public required ISignal Signal { get; init; }

    internal ISignal DeserializeSignal()
    {
        return Signal;
    }
}