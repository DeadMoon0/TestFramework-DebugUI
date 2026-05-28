using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal class ProtocolStream(PipeStream stream)
{
    internal bool PipeIsDead = false;
    private static readonly Encoding Encoding = Encoding.Unicode;
    private const int MAX_MESSAGE_BYTES = 4 * 1024 * 1024;
    private readonly SemaphoreSlim sendLock = new(1, 1);

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
            string json = JsonConvert.SerializeObject(signal);
            byte[] buffer = [.. BitConverter.GetBytes(Encoding.GetByteCount(json)), .. Encoding.GetBytes(json)];
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
            if (messageLength <= 0 || messageLength > MAX_MESSAGE_BYTES)
                throw new InvalidDataException($"Invalid pipe frame length: {messageLength}");

            byte[] jsonBuf = new byte[messageLength];
            await stream.ReadExactlyAsync(jsonBuf, 0, jsonBuf.Length, cancellationToken);
            return SignalFactory.DeserializeSignal(Encoding.GetString(jsonBuf));
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