using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Exceptions;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

internal abstract class RunDebuggerHostPiped : IDisposable
{
    PipeHost pipeHost = null!;
    private int started;
    private int disposed;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource shutdown = new();
    private Task? runLoop;

    protected string PipeName => PipeStreamController.GetPipeName();

    internal void Begin()
    {
        if (System.Threading.Interlocked.Exchange(ref started, 1) == 1)
            return;

        runLoop = Task.Run(async () =>
        {
            try
            {
                pipeHost = PipeStreamController.CreateHost();
                await OnPipeServerReadyAsync();
                ready.TrySetResult(true);

                while (!shutdown.IsCancellationRequested)
                {
                    try
                    {
                        await pipeHost.WaitForNewConnectionAsync(shutdown.Token);
                        if (shutdown.IsCancellationRequested)
                            break;

                        await OnPipeConnectionAttachedAsync();

                        bool initialized = false;
                        while (!shutdown.IsCancellationRequested)
                        {
                            ISignal? signal = await pipeHost.WaitForSignalAsync(shutdown.Token);
                            if (signal is null)
                            {
                                if (!shutdown.IsCancellationRequested)
                                    await OnPipeConnectionDetachedAsync(pipeHost.LastDisconnectReason);
                                break;
                            }

                            if (!initialized && signal.Kind != SignalKind.InitTimelineRun)
                            {
                                pipeHost.Disconnect($"Expected InitTimelineRun as first signal but received {signal.Kind}.");
                                await OnPipeConnectionDetachedAsync(pipeHost.LastDisconnectReason);
                                break;
                            }

                            switch (signal.Kind)
                            {
                                case SignalKind.EntityTransition:
                                    await OnEntityTransitionAsync((EntityTransitionSignal)signal);
                                    break;
                                case SignalKind.InitTimelineRun:
                                    initialized = true;
                                    await OnInitTimelineRunAsync((InitTimelineRunSignal)signal);
                                    break;
                                case SignalKind.TimelineRunFinished:
                                    await OnTimelineRunFinishedAsync((TimelineRunFinishedSignal)signal);
                                    pipeHost.Disconnect("Run completed.");
                                    await OnPipeConnectionDetachedAsync(pipeHost.LastDisconnectReason);
                                    break;
                                case SignalKind.ValueUpdate:
                                    await OnValueUpdateAsync((ValueUpdateSignal)signal);
                                    break;
                                case SignalKind.LogEntry:
                                    await OnLogEntryAsync((LogEntrySignal)signal);
                                    break;
                                case SignalKind.Assertion:
                                    await OnAssertionAsync((AssertionSignal)signal);
                                    break;
                                case SignalKind.BreakpointHitRequest:
                                    await OnBreakpointHitRequestAsync((BreakpointHitRequestSignal)signal);
                                    break;
                                case SignalKind.BreakpointHitContinue:
                                    throw new UnsupportedFrameworkValueException("Unexpected signal kind '" + signal.Kind + "' is not supported by the debugger host.");
                                default:
                                    throw new System.ArgumentOutOfRangeException(nameof(signal.Kind), signal.Kind, null);
                            }

                            if (!pipeHost.Connected)
                                break;
                        }
                    }
                    catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (System.Exception ex)
                    {
                        if (shutdown.IsCancellationRequested)
                            break;

                        pipeHost.Disconnect($"Pipe host failure: {ex.Message}");
                        await OnPipeConnectionDetachedAsync(pipeHost.LastDisconnectReason);
                    }
                }
            }
            catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
            {
                ready.TrySetCanceled();
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                ready.TrySetCanceled();
            }
            catch (System.Exception ex)
            {
                ready.TrySetException(ex);
                throw;
            }
        });
    }

    internal Task SendSignalAsync(ISignal signal)
    {
        return pipeHost.SendSignalAsync(signal);
    }

    internal Task WaitUntilReadyAsync()
    {
        return ready.Task;
    }

    protected virtual Task OnPipeConnectionAttachedAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnPipeServerReadyAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnPipeConnectionDetachedAsync(string reason)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref disposed, 1) == 1)
            return;

        shutdown.Cancel();
        pipeHost?.Dispose();
        ready.TrySetCanceled();

        if (runLoop is null)
            return;

        try
        {
            runLoop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException or ObjectDisposedException or IOException))
        {
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    internal abstract Task OnEntityTransitionAsync(EntityTransitionSignal signal);
    internal abstract Task OnInitTimelineRunAsync(InitTimelineRunSignal signal);
    internal abstract Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal);
    internal abstract Task OnValueUpdateAsync(ValueUpdateSignal signal);
    internal abstract Task OnLogEntryAsync(LogEntrySignal signal);
    internal abstract Task OnAssertionAsync(AssertionSignal signal);
    internal abstract Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal);
}