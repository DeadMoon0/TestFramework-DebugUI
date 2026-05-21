using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal sealed class PipeClient : IDisposable
{
    private NamedPipeClientStream? pipeClient;
    private ProtocolStream? stream;

    internal PipeClient(NamedPipeClientStream? pipeClient)
    {
        this.pipeClient = pipeClient;
        stream = pipeClient is null ? null : new ProtocolStream(pipeClient);
    }

    private bool IsConnected => pipeClient?.IsConnected == true && stream is not null && !stream.PipeIsDead;

    internal Task SignalAsync(ISignal signal)
    {
        if (!IsConnected || stream is null)
            return Task.CompletedTask;

        return stream.SendSignalAsync(signal);
    }

    internal async Task<ISignal?> WaitForAsync(SignalKind kind)
    {
        if (!IsConnected || stream is null)
            return null;

        ISignal? signal = await stream.WaitSignalAsync();
        if (signal is null)
        {
            Dispose();
            return null;
        }

        if (signal.Kind != kind)
        {
            Dispose();
            return null;
        }

        return signal;
    }

    internal async Task WaitForFlushedAsync()
    {
        if (!IsConnected || stream is null)
            return;

        await stream.FlushAsync();
        if (stream.PipeIsDead)
            Dispose();
    }

    public void Dispose()
    {
        stream = null;
        pipeClient?.Dispose();
        pipeClient = null;
    }
}