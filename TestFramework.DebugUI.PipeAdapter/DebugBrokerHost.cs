using System;
using System.Linq;
using System.Threading.Tasks;
using TestFramework.Core.Exceptions;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;


internal sealed class DebugBrokerHost : RunDebuggerHostPiped
{
    private readonly DebugSessionStore store;
    private readonly Func<BreakpointHitRequestSignal, bool> shouldPauseAtBreakpoint;
    private readonly object breakpointSync = new();
    private PendingBreakpoint? activeBreakpoint;
    private TaskCompletionSource<bool>? activeBreakpointContinue;


    internal DebugBrokerHost(DebugSessionStore? store = null, Func<BreakpointHitRequestSignal, bool>? shouldPauseAtBreakpoint = null)
    {
        this.store = store ?? new DebugSessionStore();
        this.shouldPauseAtBreakpoint = shouldPauseAtBreakpoint ?? throw new ArgumentNullException(nameof(shouldPauseAtBreakpoint));
    }

    internal DebugSessionStore Store => store;

    internal string BrokerPipeName => PipeStreamController.GetPipeName();

    internal event Func<string, Task>? PipeServerReady;
    internal event Func<Task>? PipeConnectionAttached;
    internal event Func<string, Task>? PipeConnectionDetached;
    internal event Func<StoredDebugSignalEnvelope, Task>? SignalStored;

    protected override Task OnPipeServerReadyAsync()
    {
        return InvokeAsync(PipeServerReady, BrokerPipeName);
    }

    protected override Task OnPipeConnectionAttachedAsync()
    {
        return InvokeAsync(PipeConnectionAttached);
    }

    protected override Task OnPipeConnectionDetachedAsync(string reason)
    {
        ReleaseActiveBreakpoint();
        return InvokeAsync(PipeConnectionDetached, reason);
    }

    internal override Task OnEntityTransitionAsync(EntityTransitionSignal signal)
    {
        return StoreAndNotifyAsync(signal);
    }

    internal override Task OnInitTimelineRunAsync(InitTimelineRunSignal signal)
    {
        return StoreAndNotifyAsync(signal);
    }

    internal override Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal)
    {
        return StoreAndNotifyAsync(signal);
    }

    internal override Task OnValueUpdateAsync(ValueUpdateSignal signal)
    {
        return StoreAndNotifyAsync(signal);
    }

    internal override Task OnLogEntryAsync(LogEntrySignal signal)
    {
        return StoreAndNotifyAsync(signal);
    }

    internal override Task OnAssertionAsync(AssertionSignal signal)
    {
        return StoreAndNotifyAsync(signal);
    }


    internal override async Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal)
    {
        if (!shouldPauseAtBreakpoint(signal))
        {
            await SendSignalAsync(new BreakpointHitContinueSignal());
            return;
        }

        TaskCompletionSource<bool> continueSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (breakpointSync)
        {
            if (activeBreakpointContinue is not null)
                throw new FrameworkStateException("Only one active breakpoint is supported at a time.");

            activeBreakpoint = new PendingBreakpoint(signal.SessionId, signal.Stage, signal.StepId);
            activeBreakpointContinue = continueSignal;
        }

        await StoreAndNotifyAsync(signal);
        await continueSignal.Task;
        await SendSignalAsync(new BreakpointHitContinueSignal());
    }


    internal bool TryContinueActiveBreakpoint(out string sessionId, out string stageName, out int stepId)
    {
        TaskCompletionSource<bool>? continueSignal;
        PendingBreakpoint? pendingBreakpoint;
        lock (breakpointSync)
        {
            continueSignal = activeBreakpointContinue;
            pendingBreakpoint = activeBreakpoint;
            activeBreakpointContinue = null;
            activeBreakpoint = null;
        }

        if (continueSignal is null || pendingBreakpoint is null)
        {
            sessionId = string.Empty;
            stageName = string.Empty;
            stepId = 0;
            return false;
        }

        sessionId = pendingBreakpoint.SessionId;
        stageName = pendingBreakpoint.StageName;
        stepId = pendingBreakpoint.StepId;
        return continueSignal.TrySetResult(true);
    }

    private Task StoreAndNotifyAsync(ISignal signal)
    {
        StoredDebugSignalEnvelope envelope = store.Append(signal);
        return InvokeAsync(SignalStored, envelope);
    }

    private static Task InvokeAsync(Func<Task>? handler)
    {
        if (handler is null)
            return Task.CompletedTask;

        return Task.WhenAll(handler.GetInvocationList().Cast<Func<Task>>().Select(callback => callback()));
    }

    private static Task InvokeAsync<TArg>(Func<TArg, Task>? handler, TArg arg)
    {
        if (handler is null)
            return Task.CompletedTask;

        return Task.WhenAll(handler.GetInvocationList().Cast<Func<TArg, Task>>().Select(callback => callback(arg)));
    }

    private void ReleaseActiveBreakpoint()
    {
        TaskCompletionSource<bool>? continueSignal;
        lock (breakpointSync)
        {
            continueSignal = activeBreakpointContinue;
            activeBreakpointContinue = null;
            activeBreakpoint = null;
        }

        continueSignal?.TrySetResult(true);
    }

    private sealed record PendingBreakpoint(string SessionId, string StageName, int StepId);
}