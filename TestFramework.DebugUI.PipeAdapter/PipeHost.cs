using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal sealed class PipeHost(NamedPipeServerStream pipeServer) : IDisposable
{
    private ProtocolStream stream = new ProtocolStream(pipeServer);
    private bool disposed;

    public bool Connected { get; private set; }

    public string LastDisconnectReason { get; private set; } = "Pipe not connected.";

    internal async Task WaitForNewConnectionAsync(CancellationToken cancellationToken)
    {
        await pipeServer.WaitForConnectionAsync(cancellationToken);
        Connected = true;
        LastDisconnectReason = "Connected.";
    }

    internal async Task<ISignal?> WaitForSignalAsync(CancellationToken cancellationToken = default)
    {
        ISignal? signal = await stream.WaitSignalAsync(cancellationToken);
        if (signal is null)
            Disconnect(stream.LastFailureReason);
        return signal;
    }

    internal Task SendSignalAsync(ISignal signal)
    {
        return stream.SendSignalAsync(signal);
    }

    internal void Disconnect(string reason)
    {
        if (disposed)
            return;

        LastDisconnectReason = reason;
        if (pipeServer.IsConnected)
            pipeServer.Disconnect();
        Connected = false;
        stream = new ProtocolStream(pipeServer);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Connected = false;
        pipeServer.Dispose();
    }
}