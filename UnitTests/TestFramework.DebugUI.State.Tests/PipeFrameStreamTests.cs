using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Sending a control message to a run: it either arrives, or the run is said to be unreachable - it never waits
/// for good.
/// </summary>
public class PipeFrameStreamTests
{
    [Fact]
    public async Task AControlToARunThatStoppedReading_GivesUpInsteadOfWaitingForGood()
    {
        // A producer that stops reading fills the pipe, and an unbounded write would then hold the send lock -
        // and every control for that run behind it - for as long as the window is open.
        string pipeName = "TestFrameworkDebugUITests_" + Guid.NewGuid().ToString("N");
        using NamedPipeServerStream server = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 1, outBufferSize: 1);
        using NamedPipeClientStream silentRun = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task accepted = server.WaitForConnectionAsync();
        await silentRun.ConnectAsync(5_000);
        await accepted;

        PipeFrameStream framing = new(server) { SendTimeout = TimeSpan.FromMilliseconds(200) };
        Stopwatch elapsed = Stopwatch.StartNew();

        bool sent = true;
        for (int attempt = 0; attempt < 10_000 && sent; attempt++)
            sent = await framing.SendAsync(new PipeBreakpointHitContinueSignal { SessionId = "silent" }, CancellationToken.None);

        Assert.False(sent);
        Assert.Equal("The run stopped reading.", framing.EndReason);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), $"Giving up took {elapsed.Elapsed}.");
    }
}
