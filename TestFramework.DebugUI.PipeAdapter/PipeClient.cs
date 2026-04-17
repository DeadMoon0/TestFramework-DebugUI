using System.IO.Pipes;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal class PipeClient(NamedPipeClientStream? pipeClient)
{
    private ProtocolStream? stream = pipeClient is null ? null : new ProtocolStream(pipeClient);

    internal Task SignalAsync(ISignal signal)
    {
        if (stream is null) return Task.CompletedTask;
        return stream.SendSignalAsync(signal);
    }

    internal async Task<ISignal?> WaitForAsync(SignalKind kind)
    {
        if (stream is null) return null;
        ISignal? signal = await stream.WaitSignalAsync();
        if (signal is null) return null;
        if (signal.Kind != kind) throw new System.InvalidOperationException("Expected Signal to be " + kind + " but got: " + signal.Kind);
        return signal;
    }

    internal Task WaitForFlushedAsync()
    {
        if (pipeClient is null) return Task.CompletedTask;
        return pipeClient.FlushAsync();
    }
}