using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>One attached run, and the connection carrying it.</summary>
/// <remarks>
/// Owns what belongs to one connection and nothing else: the pipe and its framing, whether the run said it
/// finished, and the one breakpoint and one request for evidence that may be outstanding on it.
/// <see cref="PipeRunEventSource"/> decides what the frames mean; this only keeps the connection's state.
/// </remarks>
internal sealed class PipeSession(string sessionId, NamedPipeServerStream server, PipeFrameStream framing)
{
    private readonly object gate = new();

    /// <summary>Whether the connection carrying this run is still open.</summary>
    internal bool IsConnected
    {
        get
        {
            try
            {
                return server.IsConnected;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
    private PipeBreakpointHitRequestSignal? pendingBreakpoint;

    /// <summary>
    /// What is waiting for this run to answer a request for fresh evidence.
    /// </summary>
    /// <remarks>
    /// One at a time, per run. A reader pressing the button twice wants a second picture rather
    /// than two answers to one question, and letting the second ask overwrite the first would
    /// leave the first waiting for an answer that had been handed to someone else.
    /// </remarks>
    private TaskCompletionSource<PipeCaptureWidgetAckSignal>? pendingCapture;

    internal string SessionId => sessionId;

    /// <summary>Why the connection ended, once it has.</summary>
    internal string EndReason => framing.EndReason;

    /// <summary>Whether the run reported finishing, as opposed to just going away.</summary>
    internal bool SawFinish { get; set; }

    /// <summary>Holds this run's breakpoint; true when it replaced one that was still held.</summary>
    /// <remarks>
    /// One per run, on both ends of the pipe: the run waits for one answer per kind at a time, so a second
    /// held step is not something this side could release separately even if it remembered both.
    /// </remarks>
    internal bool SetPendingBreakpoint(PipeBreakpointHitRequestSignal request)
    {
        lock (gate)
        {
            bool replaced = pendingBreakpoint is not null;
            pendingBreakpoint = request;
            return replaced;
        }
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

    /// <summary>
    /// Claims the right to ask this run for evidence, and hands back what will answer.
    /// </summary>
    /// <returns>The answer to wait on, or null when an ask is already in flight.</returns>
    internal Task<PipeCaptureWidgetAckSignal>? BeginCapture()
    {
        lock (gate)
        {
            if (pendingCapture is not null)
                return null;

            pendingCapture = new TaskCompletionSource<PipeCaptureWidgetAckSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
            return pendingCapture.Task;
        }
    }

    /// <summary>Delivers the run's answer, if anything is still waiting for it.</summary>
    internal void CompleteCapture(PipeCaptureWidgetAckSignal ack)
    {
        lock (gate)
            pendingCapture?.TrySetResult(ack);
    }

    /// <summary>Gives up the claim, whether or not an answer arrived.</summary>
    internal void EndCapture()
    {
        lock (gate)
            pendingCapture = null;
    }

    /// <summary>Sends a signal to the run, reporting whether it went out.</summary>
    internal Task<bool> SendAsync(IPipeSignal signal, CancellationToken cancellationToken)
        => framing.SendAsync(signal, cancellationToken);

    internal void Close()
    {
        try
        {
            server.Dispose();
        }
        catch (Exception e)
        {
            Log.Write(e);
        }
    }
}
