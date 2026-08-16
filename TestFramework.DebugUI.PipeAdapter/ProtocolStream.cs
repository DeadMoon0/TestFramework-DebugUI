using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter;

internal class ProtocolStream(PipeStream stream)
{
    internal bool PipeIsDead = false;
    private readonly SemaphoreSlim sendLock = new(1, 1);

    /// <summary>
    /// Per-connection, and therefore per session, since one connection carries one run.
    /// </summary>
    private long sequence;

    internal string LastFailureReason { get; private set; } = "Pipe closed.";

    internal async Task SendSignalAsync(ISignal signal)
    {
        if (PipeIsDead)
            return;

        bool lockAcquired = false;
        try
        {
            await sendLock.WaitAsync();
            lockAcquired = true;
            // Core's codec rather than a second implementation here. Keeping a private copy of the
            // framing is what let the two sides silently diverge: Core moved to UTF-8 envelopes
            // while this still wrote UTF-16 bare signals, so every frame parsed as garbage and the
            // UI simply never saw a run start.
            byte[] buffer = DebugEnvelopeCodec.EncodeFrame(
                DebugEnvelopeCodec.Wrap(signal, Interlocked.Increment(ref sequence)));
            await stream.WriteAsync(buffer, 0, buffer.Length, CancellationToken.None);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            MarkPipeDead($"Write failed: {e.Message}");
        }
        finally
        {
            if (lockAcquired)
                sendLock.Release();
        }
    }

    internal async Task<ISignal?> WaitSignalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] lenBuf = new byte[sizeof(Int32)];
            await stream.ReadExactlyAsync(lenBuf, 0, lenBuf.Length, cancellationToken);
            int messageLength = BitConverter.ToInt32(lenBuf);
            if (messageLength <= 0 || messageLength > DebugEnvelopeCodec.MaxMessageBytes)
                throw new InvalidDataException($"Invalid pipe frame length: {messageLength}");

            byte[] jsonBuf = new byte[messageLength];
            await stream.ReadExactlyAsync(jsonBuf, 0, jsonBuf.Length, cancellationToken);

            DebugEnvelope envelope = DebugEnvelopeCodec.Deserialize(DebugEnvelopeCodec.WireEncoding.GetString(jsonBuf));
            return DebugEnvelopeCodec.Unwrap(envelope);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            MarkPipeDead($"Read failed: {e.Message}");
            return null;
        }
    }

    internal async Task FlushAsync()
    {
        if (PipeIsDead)
            return;

        bool lockAcquired = false;
        try
        {
            await sendLock.WaitAsync();
            lockAcquired = true;
            await stream.FlushAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            MarkPipeDead($"Flush failed: {e.Message}");
        }
        finally
        {
            if (lockAcquired)
                sendLock.Release();
        }
    }

    private void MarkPipeDead(string reason)
    {
        LastFailureReason = reason;
        PipeIsDead = true;
    }
}