using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Axiom.State;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Buffers incoming debug envelopes and dispatches them to the store in batches.
/// </summary>
/// <remarks>
/// <para>
/// A run emits events far faster than a UI needs to redraw — thousands per second from a log-heavy
/// timeline. Dispatching each one separately would clone the state and notify every binding that
/// many times, so events are coalesced into a short window and applied together.
/// </para>
/// <para>
/// Breakpoints bypass the window. A paused run is waiting for the user, and making them wait an
/// extra window to find out is the one case where latency is worse than throughput.
/// </para>
/// </remarks>
public sealed class RunIngestService : IDisposable
{
    private readonly StateStore<MainState> store;
    private readonly TimeSpan window;
    private readonly object gate = new();
    private readonly List<DebugEnvelope> pending = [];

    private Timer? timer;
    private bool disposed;

    /// <summary>
    /// Creates an ingest service that flushes on the given window.
    /// </summary>
    /// <param name="store">The store to dispatch into.</param>
    /// <param name="window">
    /// How long to accumulate before dispatching. Long enough that a burst costs one clone, short
    /// enough to read as live.
    /// </param>
    public RunIngestService(StateStore<MainState> store, TimeSpan? window = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.window = window ?? TimeSpan.FromMilliseconds(50);
    }

    /// <summary>
    /// Accepts an envelope, dispatching immediately or on the next flush as appropriate.
    /// </summary>
    public void Accept(DebugEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (disposed)
            return;

        // A breakpoint means a run is parked waiting for a human, so it goes straight through —
        // along with everything already buffered, or the pause would appear before the events that
        // led to it.
        if (envelope.Kind == PipeSignalKind.BreakpointHitRequest)
        {
            lock (gate) pending.Add(envelope);
            Flush();
            return;
        }

        lock (gate)
        {
            pending.Add(envelope);
            timer ??= new Timer(_ => Flush(), null, window, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Dispatches everything buffered so far.
    /// </summary>
    /// <remarks>
    /// Called on the timer, on a breakpoint, and by a caller that wants the state settled — a test,
    /// or a transport that has reached the end of a journal.
    /// </remarks>
    public void Flush()
    {
        ImmutableList<DebugEnvelope> batch;

        lock (gate)
        {
            timer?.Dispose();
            timer = null;

            if (pending.Count == 0)
                return;

            batch = [.. pending];
            pending.Clear();
        }

        // Dispatched outside the lock: the reducer runs synchronously and notifies subscribers, and
        // holding the buffer lock across that would let a subscriber deadlock the ingest path.
        store.Dispatch(RunActions.IngestBatch, batch);
    }

    /// <summary>
    /// Flushes anything outstanding and stops the timer.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // Flush first: events already accepted have been reported as received, and dropping them on
        // shutdown would lose the tail of the last run.
        Flush();

        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
    }
}
