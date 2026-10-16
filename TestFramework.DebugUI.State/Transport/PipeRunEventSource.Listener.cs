using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

// The listener: keeps an accept pending on the debug pipe and hands each connection to its own reader.
public sealed partial class PipeRunEventSource
{
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        Report(FeedSeverity.Info, "Listening for test runs.", pipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            bool slotTaken = false;

            try
            {
                await slots.WaitAsync(cancellationToken);
                slotTaken = true;

                server = CreateServer();
                await server.WaitForConnectionAsync(cancellationToken);

                NamedPipeServerStream connected = server;
                server = null;
                slotTaken = false;

                // Deliberately not awaited. Serving one run must not stop the next from attaching,
                // which is the whole difference between this and the single-instance host it
                // replaces. The loop immediately posts another accept.
                _ = Task.Run(() => ServeAsync(connected, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                Log.Write(e);
                Report(FeedSeverity.Error, "Could not accept a test run.", e.Message);

                // A failing accept must not spin. The usual causes — another UI holding the name, or
                // every instance in use — resolve on their own or not at all, and either way a tight
                // retry loop would burn a core reporting the same thing thousands of times.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                server?.Dispose();

                if (slotTaken)
                    slots.Release();
            }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        // CurrentUserOnly puts an owner-only ACL on the pipe, so a run under another account cannot
        // feed this UI and, with the matching option on the client, a pipe squatting on the
        // well-known name under another account cannot receive a run's debug stream.
        // MaxAllowedServerInstances rather than the concurrency bound: the bound is ours to enforce
        // and reporting it as a feed entry beats the OS refusing to create the instance.
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
}
