using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFrameworkDebugUI;

namespace TestFramework.DebugUI;

internal class DebugPipeTranslator : RunDebuggerHostPiped
{
    private readonly DebugRunStateReducer reducer = new(MainWindow.State);

    protected override Task OnPipeServerReadyAsync()
    {
        return reducer.ApplyPipeServerReadyAsync(PipeName);
    }

    protected override Task OnPipeConnectionAttachedAsync()
    {
        return reducer.ApplyPipeConnectionAttachedAsync(PipeName);
    }

    protected override Task OnPipeConnectionDetachedAsync(string reason)
    {
        return reducer.ApplyPipeConnectionDetachedAsync(reason);
    }

    public override async Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal)
    {
        await reducer.ApplyBreakpointHitRequestAsync(signal);
        await SendSignalAsync(new BreakpointHitContinueSignal());
    }

    public override async Task OnInitTimelineRunAsync(InitTimelineRunSignal signal)
    {
        await reducer.ApplyInitTimelineRunAsync(signal);
    }

    public override async Task OnEntityTransitionAsync(EntityTransitionSignal signal)
    {
        await reducer.ApplyEntityTransitionAsync(signal);
    }

    public override async Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal)
    {
        await reducer.ApplyTimelineRunFinishedAsync(signal);
    }

    public override async Task OnValueUpdateAsync(ValueUpdateSignal signal)
    {
        await reducer.ApplyValueUpdateAsync(signal);
    }

    public override async Task OnLogEntryAsync(LogEntrySignal signal)
    {
        await reducer.ApplyLogEntryAsync(signal);
    }

    public override async Task OnAssertionAsync(AssertionSignal signal)
    {
        await reducer.ApplyAssertionAsync(signal);
    }
}