using System;
using System.IO.Pipes;

namespace TestFramework.DebugUI.PipeAdapter;

internal static class PipeStreamController
{
    private const string DEFAULT_PIPE_NAME = "TestFrameworkDebug_79d7aa2d-da07-4c84-b1f2-0639b0009290";

    private static string PipeName => Environment.GetEnvironmentVariable("TESTFRAMEWORK_DEBUG_PIPE_NAME") ?? DEFAULT_PIPE_NAME;

    internal static string GetPipeName()
    {
        return PipeName;
    }

    internal static PipeHost CreateHost()
    {
        NamedPipeServerStream pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        return new PipeHost(pipeServer);
    }

    internal static PipeClient CreateClient()
    {
        try
        {
            NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipeClient.Connect(TimeSpan.FromSeconds(1));
            return new PipeClient(pipeClient);
        }
        catch
        {
            return new PipeClient(null);
        }
    }
}