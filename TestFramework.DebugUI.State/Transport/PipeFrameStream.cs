using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// Reads and writes debug envelopes over one pipe connection.
/// </summary>
/// <remarks>
/// Framing comes from <see cref="DebugEnvelopeCodec"/> rather than being reimplemented here. The
/// previous adapter kept its own copy, which is exactly how the two sides silently diverged: Core
/// moved to UTF-8 envelopes while the consumer still wrote UTF-16 bare signals, the solution still
/// compiled, and the only symptom was a UI that never saw a run start.
/// </remarks>
internal sealed class PipeFrameStream(PipeStream stream)
{
    /// <summary>Serializes writes, which arrive from the reader loop and from the UI thread alike.</summary>
    private readonly SemaphoreSlim sendLock = new(1, 1);

    /// <summary>
    /// One connection carries one run, so a per-connection counter is also per-session. Only used
    /// for the few messages travelling back to the producer.
    /// </summary>
    private long sequence;

    private bool ended;

    /// <summary>Why the connection ended, once it has.</summary>
    internal string EndReason { get; private set; } = "The run disconnected.";

    /// <summary>Whether the connection ended because of a failure rather than an ordinary close.</summary>
    internal bool EndedInFailure { get; private set; }

    /// <summary>
    /// Reads the next envelope, or returns null once the connection has ended.
    /// </summary>
    internal async Task<DebugEnvelope?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[] lengthBuffer = new byte[sizeof(int)];
            await stream.ReadExactlyAsync(lengthBuffer, 0, lengthBuffer.Length, cancellationToken);

            int messageLength = BitConverter.ToInt32(lengthBuffer);
            if (messageLength <= 0 || messageLength > DebugEnvelopeCodec.MaxMessageBytes)
                throw new InvalidDataException($"Invalid pipe frame length: {messageLength}.");

            byte[] messageBuffer = new byte[messageLength];
            await stream.ReadExactlyAsync(messageBuffer, 0, messageBuffer.Length, cancellationToken);

            return DebugEnvelopeCodec.Deserialize(DebugEnvelopeCodec.WireEncoding.GetString(messageBuffer));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            End("The UI stopped listening.", failure: false);
            return null;
        }
        catch (EndOfStreamException)
        {
            // The ordinary way a run ends: the test host exited and closed its end. Whether that was
            // expected is decided by whether a finish signal arrived first, not here.
            End("The run disconnected.", failure: false);
            return null;
        }
        catch (Exception e)
        {
            Log.Write(e);
            End(e.Message, failure: true);
            return null;
        }
    }

    /// <summary>
    /// Sends a control message back to the producer, reporting whether it got through.
    /// </summary>
    /// <remarks>
    /// A failed send is returned rather than thrown: the callers are releasing a breakpoint and
    /// cancelling a run, and neither has anything useful to do with an exception beyond telling the
    /// user the run is no longer reachable.
    /// <para>
    /// Bounded by <see cref="SendTimeout"/>. A producer that stops reading - a test host frozen in a debugger,
    /// say - fills the pipe's buffer, and an unbounded write would then hold the send lock for good, so every
    /// later control for that run, and the window waiting on one, would wait with it.
    /// </para>
    /// </remarks>
    internal async Task<bool> SendAsync(IPipeSignal signal, CancellationToken cancellationToken)
    {
        bool lockAcquired = false;
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(SendTimeout);

        try
        {
            await sendLock.WaitAsync(bounded.Token);
            lockAcquired = true;

            byte[] frame = DebugEnvelopeCodec.EncodeFrame(
                DebugEnvelopeCodec.Wrap(signal, Interlocked.Increment(ref sequence)));

            await stream.WriteAsync(frame, 0, frame.Length, bounded.Token);
            await stream.FlushAsync(bounded.Token);
            return true;
        }
        catch (OperationCanceledException) when (bounded.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            End("The run stopped reading.", failure: true);

            // Closed, not only marked: an open pipe keeps the run's connection slot and its session, so a
            // healthy reconnect would be refused and every later control would wait out the bound again.
            // Closing it ends the read loop that owns both.
            stream.Dispose();
            return false;
        }
        catch (Exception e)
        {
            Log.Write(e);
            End(e.Message, failure: true);
            return false;
        }
        finally
        {
            if (lockAcquired)
                sendLock.Release();
        }
    }

    /// <summary>How long one control message may take to send before the run is treated as unreachable.</summary>
    internal TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(5);

    private void End(string reason, bool failure)
    {
        // First reason wins: a read failure is usually followed by a write failure describing the
        // same broken connection, and the first one is the one that explains it.
        if (ended)
            return;

        ended = true;
        EndReason = reason;
        EndedInFailure = failure;
    }
}
