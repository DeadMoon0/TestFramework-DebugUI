using System.IO.Pipes;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal class PipeHost(NamedPipeServerStream pipeServer)
{
    private ProtocolStream stream = new ProtocolStream(pipeServer);

    public bool Connected { get; private set; }

    internal async Task WaitForNewConnectionAsync()
    {
        await pipeServer.WaitForConnectionAsync();
        Connected = true;
    }

    internal async Task<ISignal?> WaitForSignalAsync()
    {
        ISignal? signal = await stream.WaitSignalAsync();
        if (signal is null) Disconnect();
        return signal;
    }

    internal Task SendSignalAsync(ISignal signal)
    {
        return stream.SendSignalAsync(signal);
    }

    internal void Disconnect()
    {
        pipeServer.Disconnect();
        Connected = false;
    }
}