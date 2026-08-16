using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;

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
            Debug.WriteLine(e);
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
    /// </remarks>
    internal async Task<bool> SendAsync(IPipeSignal signal, CancellationToken cancellationToken)
    {
        bool lockAcquired = false;

        try
        {
            await sendLock.WaitAsync(cancellationToken);
            lockAcquired = true;

            byte[] frame = DebugEnvelopeCodec.EncodeFrame(
                DebugEnvelopeCodec.Wrap(signal, Interlocked.Increment(ref sequence)));

            await stream.WriteAsync(frame, 0, frame.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            End(e.Message, failure: true);
            return false;
        }
        finally
        {
            if (lockAcquired)
                sendLock.Release();
        }
    }

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
