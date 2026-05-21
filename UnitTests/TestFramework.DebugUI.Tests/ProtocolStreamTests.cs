using System.IO.Pipes;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFramework.DebugUI.Tests.Support;

namespace TestFramework.DebugUI.Tests;

[Collection("WpfHost")]
public sealed class ProtocolStreamTests
{
    [Fact]
    public async Task ProtocolStream_RoundTripsParallelSignalsWithoutCorruption()
    {
        string pipeName = $"ProtocolStreamTests_{Guid.NewGuid():N}";
        using NamedPipeServerStream server = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        Task connectTask = client.ConnectAsync();
        await server.WaitForConnectionAsync();
        await connectTask;

        ProtocolStream sender = new(client);
        ProtocolStream receiver = new(server);

        TimelineRunFinishedSignal[] signals = Enumerable.Range(0, 40)
            .Select(index => new TimelineRunFinishedSignal { SessionId = $"session-{index}" })
            .ToArray();

        List<string> receivedSessionIds = [];
        Task receiveTask = Task.Run(async () =>
        {
            for (int i = 0; i < signals.Length; i++)
            {
                ISignal? signal = await receiver.WaitSignalAsync();
                TimelineRunFinishedSignal finishedSignal = Assert.IsType<TimelineRunFinishedSignal>(signal);
                receivedSessionIds.Add(finishedSignal.SessionId);
            }
        });

        await Task.WhenAll(signals.Select(sender.SendSignalAsync));
        await sender.FlushAsync();

        await receiveTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(signals.Length, receivedSessionIds.Count);
        Assert.Equal(signals.Select(signal => signal.SessionId).OrderBy(x => x), receivedSessionIds.OrderBy(x => x));
        Assert.False(sender.PipeIsDead);
        Assert.False(receiver.PipeIsDead);
    }

    [Fact]
    public async Task ProtocolStream_InvalidFrameMarksPipeDeadAndCapturesReason()
    {
        string pipeName = $"ProtocolStreamTests_{Guid.NewGuid():N}";
        using NamedPipeServerStream server = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        Task connectTask = client.ConnectAsync();
        await server.WaitForConnectionAsync();
        await connectTask;

        ProtocolStream receiver = new(server);
        Task<ISignal?> receiveTask = receiver.WaitSignalAsync();

        byte[] invalidLength = BitConverter.GetBytes(-123);
        await client.WriteAsync(invalidLength);
        await client.FlushAsync();

        ISignal? signal = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(signal);
        Assert.True(receiver.PipeIsDead);
        Assert.Contains("Invalid pipe frame length", receiver.LastFailureReason);
    }
}