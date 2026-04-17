using System;
using System.IO.Pipes;

namespace TestFramework.DebugUI.PipeAdapter;

internal static class PipeStreamController
{
    private const string PIPE_NAME = "TestFrameworkDebug_79d7aa2d-da07-4c84-b1f2-0639b0009290";

    internal static PipeHost CreateHost()
    {
        NamedPipeServerStream pipeServer = new NamedPipeServerStream(PIPE_NAME, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        return new PipeHost(pipeServer);
    }

    internal static PipeClient CreateClient()
    {
        NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipeClient.Connect(TimeSpan.FromSeconds(1));
        return new PipeClient(pipeClient);
    }
}