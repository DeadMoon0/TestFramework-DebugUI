using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// Accepts live runs over the debug pipe.
/// </summary>
/// <remarks>
/// <para>
/// One connection is one run. Several runs attach at once — a test project running its timelines in
/// parallel is the normal case, not an exotic one — so the server keeps an accept pending at all
/// times and serves each connection on its own reader. The previous host allowed a single server
/// instance and read one signal at a time, which meant the second concurrent run silently lost
/// debugging altogether.
/// </para>
/// <para>
/// Nothing here interprets a run. Envelopes are handed on exactly as received, so a live run and a
/// replayed journal drive the same projection.
/// </para>
/// </remarks>
public sealed class PipeRunEventSource : IRunEventSource, IDisposable
{
    /// <summary>
    /// The name the framework's transport connects to when nothing overrides it.
    /// </summary>
    /// <remarks>
    /// Duplicated from Core rather than shared: it is wire configuration, and a consumer needs it
    /// without taking a dependency on Core's internals. It must match
    /// <c>PipeTransport.GetPipeName</c>, and the round-trip test that runs a real timeline against
    /// this server is what proves it still does.
    /// </remarks>
    public const string DefaultPipeName = "TestFrameworkDebug_79d7aa2d-da07-4c84-b1f2-0639b0009290";

    /// <summary>
    /// How many runs may be attached at once.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. A run that cannot connect does not fail — it just loses its debugger
    /// without saying so — and a suite fanning out across cores can hold many connections briefly,
    /// so the bound exists to catch a leak rather than to ration normal use.
    /// </remarks>
    public const int DefaultMaxConcurrentRuns = 64;

    private readonly string pipeName;
    private readonly SemaphoreSlim slots;
    private readonly CancellationTokenSource shutdown = new();

    /// <summary>
    /// Captured once. Reading <c>shutdown.Token</c> after the source is disposed would throw, and
    /// the send path is reached from the UI thread at moments the window is closing.
    /// </summary>
    private readonly CancellationToken shutdownToken;
    private readonly ConcurrentDictionary<string, PipeSession> sessions = new(StringComparer.Ordinal);

    private int started;
    private bool disposed;
    private Task? acceptLoop;

    /// <summary>
    /// Creates a source listening on the debug pipe.
    /// </summary>
    /// <param name="pipeName">
    /// The pipe to listen on. Defaults to the framework's well-known name; a test passes its own so
    /// it cannot collide with a real UI or another test running beside it.
    /// </param>
    /// <param name="maxConcurrentRuns">How many runs may be attached at once.</param>
    public PipeRunEventSource(string? pipeName = null, int maxConcurrentRuns = DefaultMaxConcurrentRuns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRuns, 1);

        this.pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? Environment.GetEnvironmentVariable("TESTFRAMEWORK_DEBUG_PIPE_NAME") ?? DefaultPipeName
            : pipeName;

        MaxConcurrentRuns = maxConcurrentRuns;
        slots = new SemaphoreSlim(maxConcurrentRuns, maxConcurrentRuns);
        shutdownToken = shutdown.Token;
    }

    /// <inheritdoc />
    public event Action<DebugEnvelope>? EnvelopeReceived;

    /// <inheritdoc />
    public event Action<FeedEntry>? Notice;

    /// <summary>
    /// Raised whenever a run attaches or detaches.
    /// </summary>
    /// <remarks>
    /// Without this the transport could report becoming busy but never becoming quiet, so the status
    /// said "Attached" for the rest of the session after the first run — long after everything had
    /// disconnected. A consumer that shows a live count needs both edges.
    /// </remarks>
    public event Action? ConnectionsChanged;

    /// <summary>
    /// Decides whether a step that reached a breakpoint should be held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every step asks, before it runs — the framework has no way to know which steps the user
    /// marked, so it defers the whole decision here. That makes the default load-bearing: with no
    /// policy set nothing is ever held, because a consumer that quietly paused every step would
    /// stall each one until the producer's ten-minute wait expired.
    /// </para>
    /// <para>
    /// A question is not an event. Steps that are not held never reach
    /// <see cref="EnvelopeReceived"/> at all, so the run's history records the pauses that happened
    /// rather than one entry per step asking permission to continue.
    /// </para>
    /// </remarks>
    public Func<PipeBreakpointHitRequestSignal, bool>? PauseAtBreakpoint { get; set; }

    /// <summary>Gets the pipe this source listens on.</summary>
    public string PipeName => pipeName;

    /// <summary>Gets how many runs may be attached at once.</summary>
    public int MaxConcurrentRuns { get; }

    /// <summary>Gets the runs currently attached.</summary>
    public int AttachedRunCount => sessions.Count;

    /// <summary>Gets the sessions currently attached.</summary>
    public IReadOnlyCollection<string> AttachedSessionIds => sessions.Keys.ToArray();

    /// <summary>
    /// Begins accepting runs. Further calls do nothing.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Interlocked.Exchange(ref started, 1) == 1)
            return;

        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token), CancellationToken.None);
    }

    /// <summary>
    /// Releases a run held at a breakpoint, reporting whether it was reached.
    /// </summary>
    /// <remarks>
    /// Held breakpoints are tracked per session, so releasing one parallel run leaves the others
    /// paused. The previous host kept a single pending breakpoint and threw outright when a second
    /// run paused while one already had.
    /// </remarks>
    public async Task<bool> ContinueAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!sessions.TryGetValue(sessionId, out PipeSession? session))
            return false;

        if (!session.TryClearPendingBreakpoint())
            return false;

        return await SendAsync(session, new PipeBreakpointHitContinueSignal { SessionId = sessionId }, "release the breakpoint");
    }

    /// <summary>
    /// Asks a run to stop, reporting whether the request was delivered.
    /// </summary>
    /// <remarks>
    /// The run stops itself: the timeline unwinds through its Cleanup stage, so artifacts are
    /// deconstructed and environment components torn down. That is the entire reason this is a
    /// message rather than a process kill, which would strand exactly the resources cleanup exists
    /// to release. A run held at a breakpoint is released first, or it would sit waiting for a
    /// continue that is no longer coming.
    /// </remarks>
    public async Task<bool> CancelAsync(string sessionId, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!sessions.TryGetValue(sessionId, out PipeSession? session))
            return false;

        if (session.TryClearPendingBreakpoint())
            await SendAsync(session, new PipeBreakpointHitContinueSignal { SessionId = sessionId }, "release the breakpoint");

        return await SendAsync(session, new PipeCancelRunSignal { SessionId = sessionId, Reason = reason }, "cancel the run");
    }

    /// <summary>
    /// Stops accepting runs and drops every attached connection.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        try
        {
            shutdown.Cancel();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        foreach (PipeSession session in sessions.Values)
            session.Close();

        sessions.Clear();

        try
        {
            // Bounded: a reader parked on a read that will not observe the cancellation must not
            // hold up closing the window.
            acceptLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        // Neither the token source nor the semaphore is disposed. Readers still unwinding release
        // their slot and observe the token on the way out, and disposing either would turn an
        // orderly shutdown into a race that throws. Both hold no thread and no handle, so the only
        // thing disposal would buy is the exception.
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        Report(FeedSeverity.Info, "Listening for test runs.", pipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            bool slotTaken = false;

            try
            {
                await slots.WaitAsync(cancellationToken);
                slotTaken = true;

                server = CreateServer();
                await server.WaitForConnectionAsync(cancellationToken);

                NamedPipeServerStream connected = server;
                server = null;
                slotTaken = false;

                // Deliberately not awaited. Serving one run must not stop the next from attaching,
                // which is the whole difference between this and the single-instance host it
                // replaces. The loop immediately posts another accept.
                _ = Task.Run(() => ServeAsync(connected, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                Report(FeedSeverity.Error, "Could not accept a test run.", e.Message);

                // A failing accept must not spin. The usual causes — another UI holding the name, or
                // every instance in use — resolve on their own or not at all, and either way a tight
                // retry loop would burn a core reporting the same thing thousands of times.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                server?.Dispose();

                if (slotTaken)
                    slots.Release();
            }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        // CurrentUserOnly puts an owner-only ACL on the pipe, so a run under another account cannot
        // feed this UI and, with the matching option on the client, a pipe squatting on the
        // well-known name under another account cannot receive a run's debug stream.
        // MaxAllowedServerInstances rather than the concurrency bound: the bound is ours to enforce
        // and reporting it as a feed entry beats the OS refusing to create the instance.
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        PipeFrameStream framing = new(server);
        PipeSession? session = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DebugEnvelope? envelope = await framing.ReadAsync(cancellationToken);
                if (envelope is null)
                    break;

                session ??= Attach(envelope.SessionId, server, framing);

                if (!await HandleAsync(session, envelope, cancellationToken))
                    continue;

                Raise(envelope);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Report(FeedSeverity.Error, "A test run's connection failed.", e.Message, session?.SessionId);
        }
        finally
        {
            Detach(session, framing);

            try
            {
                server.Dispose();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            slots.Release();
        }
    }

    /// <summary>
    /// Applies transport-level meaning to an envelope, reporting whether it should be passed on.
    /// </summary>
    private async Task<bool> HandleAsync(PipeSession session, DebugEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Kind == PipeSignalKind.TimelineRunFinished)
        {
            session.SawFinish = true;
            return true;
        }

        if (envelope.Kind != PipeSignalKind.BreakpointHitRequest)
            return true;

        PipeBreakpointHitRequestSignal? request = TryUnwrapBreakpoint(envelope);

        if (request is not null && ShouldPause(request))
        {
            // Recorded before the envelope is raised, so a UI that reacts by releasing it
            // immediately cannot find nothing to release.
            session.SetPendingBreakpoint(request);
            return true;
        }

        await SendAsync(session, new PipeBreakpointHitContinueSignal { SessionId = envelope.SessionId }, "let the step continue");
        return false;
    }

    private PipeBreakpointHitRequestSignal? TryUnwrapBreakpoint(DebugEnvelope envelope)
    {
        try
        {
            return DebugEnvelopeCodec.Unwrap(envelope) as PipeBreakpointHitRequestSignal;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);

            // The step is waiting on an answer, so a frame we cannot read still gets one. Holding a
            // run because its consumer was confused is the one outcome worse than losing the pause.
            Report(FeedSeverity.Warning, "A breakpoint could not be read, so the step was allowed to continue.", e.Message, envelope.SessionId);
            return null;
        }
    }

    private bool ShouldPause(PipeBreakpointHitRequestSignal request)
    {
        Func<PipeBreakpointHitRequestSignal, bool>? policy = PauseAtBreakpoint;
        if (policy is null)
            return false;

        try
        {
            return policy(request);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Report(FeedSeverity.Error, "Deciding whether to pause failed, so the step was allowed to continue.", e.Message, request.SessionId);
            return false;
        }
    }

    private PipeSession Attach(string sessionId, NamedPipeServerStream server, PipeFrameStream framing)
    {
        PipeSession session = new(sessionId, server, framing);

        // A session id repeating means the previous connection for it is gone; the newcomer owns it.
        sessions[sessionId] = session;

        ConnectionsChanged?.Invoke();

        return session;
    }

    private void Detach(PipeSession? session, PipeFrameStream framing)
    {
        if (session is null)
        {
            // Never sent a single frame, so there is no run to report on — a probe, or a connection
            // dropped before it said anything.
            return;
        }

        sessions.TryRemove(new KeyValuePair<string, PipeSession>(session.SessionId, session));

        ConnectionsChanged?.Invoke();

        if (session.SawFinish)
            return;

        // The producer went away without finishing. That is what a killed or crashed test host looks
        // like from here, and it is worth saying: the run is on screen and simply stops, which is
        // otherwise indistinguishable from one that is merely slow.
        Report(
            framing.EndedInFailure ? FeedSeverity.Error : FeedSeverity.Warning,
            "A test run disconnected before it finished.",
            framing.EndReason,
            session.SessionId);
    }

    private async Task<bool> SendAsync(PipeSession session, IPipeSignal signal, string what)
    {
        if (await session.Framing.SendAsync(signal, shutdownToken))
            return true;

        Report(FeedSeverity.Warning, $"Could not {what}: the run is no longer reachable.", session.Framing.EndReason, session.SessionId);
        return false;
    }

    /// <summary>
    /// Hands an envelope to every subscriber, in isolation from each other.
    /// </summary>
    /// <remarks>
    /// A consumer that throws is a bug in the consumer, and it must cost neither the run's
    /// connection nor the other subscribers: a multicast delegate stops at the first exception, so
    /// one broken handler would silently starve the ones behind it — including the one feeding the
    /// board. Walking the invocation list allocates, which is nothing next to the deserialization
    /// each envelope already pays for.
    /// </remarks>
    private void Raise(DebugEnvelope envelope)
    {
        Action<DebugEnvelope>? subscribers = EnvelopeReceived;
        if (subscribers is null)
            return;

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<DebugEnvelope>)subscriber)(envelope);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                Report(FeedSeverity.Error, "A run event could not be applied.", e.Message, envelope.SessionId);
            }
        }
    }

    private void Report(FeedSeverity severity, string title, string? detail, string? sessionId = null)
    {
        try
        {
            Notice?.Invoke(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = severity,
                Source = FeedSource.Transport,
                Title = title,
                Detail = detail,
                SessionId = sessionId
            });
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    /// <summary>One attached run, and the connection carrying it.</summary>
    private sealed class PipeSession(string sessionId, NamedPipeServerStream server, PipeFrameStream framing)
    {
        private readonly object gate = new();
        private PipeBreakpointHitRequestSignal? pendingBreakpoint;

        internal string SessionId => sessionId;

        internal PipeFrameStream Framing => framing;

        /// <summary>Whether the run reported finishing, as opposed to just going away.</summary>
        internal bool SawFinish { get; set; }

        internal void SetPendingBreakpoint(PipeBreakpointHitRequestSignal request)
        {
            lock (gate)
                pendingBreakpoint = request;
        }

        /// <summary>Takes the held breakpoint, so only one caller can release it.</summary>
        internal bool TryClearPendingBreakpoint()
        {
            lock (gate)
            {
                if (pendingBreakpoint is null)
                    return false;

                pendingBreakpoint = null;
                return true;
            }
        }

        internal void Close()
        {
            try
            {
                server.Dispose();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }
    }
}
