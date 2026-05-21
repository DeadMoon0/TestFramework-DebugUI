using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

public abstract class RunDebuggerHostPiped : IDisposable
{
    PipeHost pipeHost = null!;
    private int started;
    private int disposed;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource shutdown = new();
    private Task? runLoop;

    protected string PipeName => PipeStreamController.GetPipeName();

    public void Begin()
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
                            ISignal? signal = await pipeHost.WaitForSignalAsync();
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
                                    throw new System.InvalidOperationException("Unexpected Signal Kind " + signal.Kind + " this is not Supported as the Host.");
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

    public Task SendSignalAsync(ISignal signal)
    {
        return pipeHost.SendSignalAsync(signal);
    }

    public Task WaitUntilReadyAsync()
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
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException or ObjectDisposedException))
        {
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    public abstract Task OnEntityTransitionAsync(EntityTransitionSignal signal);
    public abstract Task OnInitTimelineRunAsync(InitTimelineRunSignal signal);
    public abstract Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal);
    public abstract Task OnValueUpdateAsync(ValueUpdateSignal signal);
    public abstract Task OnLogEntryAsync(LogEntrySignal signal);
    public abstract Task OnAssertionAsync(AssertionSignal signal);
    public abstract Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal);
}