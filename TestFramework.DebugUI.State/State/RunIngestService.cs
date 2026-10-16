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
    private readonly object dispatchOrder = new();
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
    /// <para>
    /// Called on the timer, on a breakpoint, and by a caller that wants the state settled — a test,
    /// or a transport that has reached the end of a journal.
    /// </para>
    /// <para>
    /// Three threads reach this: the timer, the pipe reader on a breakpoint, and the UI thread
    /// selecting a run. Taking the batch and dispatching it has to be one indivisible act for all of
    /// them, because the reducer folds transitions in the order it receives them — a thread that
    /// takes an older batch and is preempted before dispatching would let a newer batch land first,
    /// and a step already reported complete would go back to running.
    /// </para>
    /// </remarks>
    public void Flush()
    {
        // Ordering lock, held across the dispatch; the buffer lock is not, because the reducer runs
        // synchronously and notifies subscribers, and holding the buffer across that would let a
        // subscriber deadlock the ingest path. A subscriber that flushes re-enters this lock on its
        // own thread, which is why it is a monitor rather than a semaphore.
        lock (dispatchOrder)
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

            store.Dispatch(MainActions.IngestBatch, batch);
        }
    }

    /// <summary>
    /// Dispatches everything buffered so far, then runs <paramref name="dispatch"/> before any batch
    /// accepted after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a caller whose dispatches must not be overtaken by events still arriving — switching the
    /// selected run is the one there is. That clears the board and replays the run's history onto it,
    /// and an event the reader accepts in between would otherwise land on the empty board first: the
    /// reducer then takes the history, older by sequence, for events already shown and drops it,
    /// leaving a board with no structure under it.
    /// </para>
    /// <para>
    /// Accepting is not held up, only flushing: whatever arrives meanwhile waits in the buffer and goes
    /// in after <paramref name="dispatch"/>, where the board's own sequence drops what the replay
    /// already carried.
    /// </para>
    /// </remarks>
    /// <param name="dispatch">The dispatches to make with nothing accepted from now on ahead of them.</param>
    public void FlushThen(Action dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        lock (dispatchOrder)
        {
            Flush();
            dispatch();
        }
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
