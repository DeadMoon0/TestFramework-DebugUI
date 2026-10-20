using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

// Serving one connection: reading its frames, giving the transport-level ones their meaning, and
// registering and releasing the run it carries.
public sealed partial class PipeRunEventSource
{
    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        PipeFrameStream framing = new(server);
        PipeSession? session = null;

        try
        {
            DebugEnvelope? first = await ReadFirstFrameAsync(framing, cancellationToken);
            if (first is null)
                return;

            for (DebugEnvelope? envelope = first; envelope is not null && !cancellationToken.IsCancellationRequested;
                 envelope = await framing.ReadAsync(cancellationToken))
            {
                session ??= Attach(envelope.SessionId, server, framing);
                if (session is null)
                    return;

                if (!await HandleAsync(session, envelope, cancellationToken))
                    continue;

                Raise(envelope);
            }
        }
        catch (Exception e)
        {
            Log.Write(e);
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
                Log.Write(e);
            }

            slots.Release();
        }
    }

    /// <summary>
    /// Reads a connection's first frame within <see cref="FirstFrameDeadline"/>; null when the connection closed,
    /// or said nothing in time and was let go.
    /// </summary>
    private async Task<DebugEnvelope?> ReadFirstFrameAsync(PipeFrameStream framing, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(FirstFrameDeadline);

        // The frame reader turns a cancelled read into the end of the connection, so whose cancellation it was
        // is read from the tokens afterwards.
        DebugEnvelope? first = await framing.ReadAsync(deadline.Token);

        if (first is null && deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Report(
                FeedSeverity.Warning,
                "A connection on the debug pipe sent nothing and was closed.",
                $"It said nothing for {FirstFrameDeadline.TotalSeconds:0} s after connecting. A test host that hangs right after connecting is the usual cause.");
        }

        return first;
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

        // A reply to something this side asked, not news about the run. Handing it to the projection
        // would put a message in the run's history saying that a button had been pressed.
        if (envelope.Kind == PipeSignalKind.CaptureWidgetAck)
        {
            AnswerCapture(session, envelope);
            return false;
        }

        if (envelope.Kind != PipeSignalKind.BreakpointHitRequest)
            return true;

        PipeBreakpointHitRequestSignal? request = TryUnwrapBreakpoint(envelope);

        if (request is not null && ShouldPause(request))
        {
            // Recorded before the envelope is raised, so a UI that reacts by releasing it
            // immediately cannot find nothing to release.
            if (session.SetPendingBreakpoint(request))
            {
                Report(
                    FeedSeverity.Warning,
                    "A second step of one run asked to be held while another was.",
                    "Only one step of a run can be held at a time. The earlier one is no longer releasable from here.",
                    session.SessionId);
            }

            return true;
        }

        await SendAsync(session, new PipeBreakpointHitContinueSignal { SessionId = envelope.SessionId }, "let the step continue");
        return false;
    }

    /// <summary>
    /// Hands a run's answer to whoever asked for it.
    /// </summary>
    /// <remarks>
    /// An answer nobody is waiting for is dropped on purpose, and it is a normal thing rather than a
    /// fault: the asker gave up, or a second answer arrived for one request. Reporting it would put a
    /// warning in the feed for something that cost nothing.
    /// </remarks>
    private void AnswerCapture(PipeSession session, DebugEnvelope envelope)
    {
        try
        {
            if (DebugEnvelopeCodec.Unwrap(envelope) is PipeCaptureWidgetAckSignal ack)
                session.CompleteCapture(ack);
        }
        catch (Exception e)
        {
            Log.Write(e);

            // Nothing is holding the run here, unlike a breakpoint whose answer went missing, so the
            // asker simply waits out its own timeout and says the run did not answer.
            Report(FeedSeverity.Warning, "A run's answer about fresh evidence could not be read.", e.Message, envelope.SessionId);
        }
    }

    private PipeBreakpointHitRequestSignal? TryUnwrapBreakpoint(DebugEnvelope envelope)
    {
        try
        {
            return DebugEnvelopeCodec.Unwrap(envelope) as PipeBreakpointHitRequestSignal;
        }
        catch (Exception e)
        {
            Log.Write(e);

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
            Log.Write(e);
            Report(FeedSeverity.Error, "Deciding whether to pause failed, so the step was allowed to continue.", e.Message, request.SessionId);
            return false;
        }
    }

    /// <summary>
    /// Registers a connection as the run it says it is; null when that run is already attached and still
    /// connected.
    /// </summary>
    /// <remarks>
    /// A run that lost its connection reconnects under the same session id, sometimes before the old
    /// connection's loop has noticed it ended - so a repeated id whose connection is gone is taken over. A
    /// repeated id whose connection is still live is a second client claiming somebody else's run: accepted,
    /// every control meant for that run - a breakpoint release, a capture request - would go to the newcomer.
    /// </remarks>
    private PipeSession? Attach(string sessionId, NamedPipeServerStream server, PipeFrameStream framing)
    {
        // A run that already finished, being added to: the checks made on it once its test had it back. Its
        // connection closing is the end of that addition, not a run going away halfway.
        PipeSession session = new(sessionId, server, framing) { SawFinish = finishedRuns.Finished(sessionId) };

        while (!sessions.TryAdd(sessionId, session))
        {
            if (!sessions.TryGetValue(sessionId, out PipeSession? existing))
                continue;

            if (existing.IsConnected)
            {
                Report(
                    FeedSeverity.Warning,
                    "A second connection claimed a run that is still attached, and was closed.",
                    "Controls for the run keep going to the connection that started it.",
                    sessionId);
                return null;
            }

            if (sessions.TryUpdate(sessionId, session, existing))
                break;
        }

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

        SessionEnded?.Invoke(new SessionEnded { SessionId = session.SessionId, Finished = session.SawFinish });

        if (session.SawFinish)
        {
            finishedRuns.Remember(session.SessionId);
            return;
        }

        // The producer went away without finishing. That is what a killed or crashed test host looks
        // like from here, and it is worth saying: the run is on screen and simply stops, which is
        // otherwise indistinguishable from one that is merely slow.
        Report(
            framing.EndedInFailure ? FeedSeverity.Error : FeedSeverity.Warning,
            "A test run disconnected before it finished.",
            framing.EndReason,
            session.SessionId);
    }
}
