using System.Threading.Tasks;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

public abstract class RunDebuggerHostPiped
{
    PipeHost pipeHost = null!;

    public void Begin()
    {
        Task.Run(async () =>
        {
            pipeHost = PipeStreamController.CreateHost();
            while (true)
            {
                await pipeHost.WaitForNewConnectionAsync();
                while (true)
                {
                    ISignal? signal = await pipeHost.WaitForSignalAsync();
                    if (signal is null) break;
                    switch (signal.Kind)
                    {
                        case SignalKind.EntityTransition:
                            await OnEntityTransitionAsync((EntityTransitionSignal)signal);
                            break;
                        case SignalKind.InitTimelineRun:
                            await OnInitTimelineRunAsync((InitTimelineRunSignal)signal);
                            break;
                        case SignalKind.TimelineRunFinished:
                            await OnTimelineRunFinishedAsync((TimelineRunFinishedSignal)signal);
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
                        default: throw new System.ArgumentOutOfRangeException(nameof(signal.Kind), signal.Kind, null);
                    }
                }
            }
        });
    }

    public Task SendSignalAsync(ISignal signal)
    {
        return pipeHost.SendSignalAsync(signal);
    }

    public abstract Task OnEntityTransitionAsync(EntityTransitionSignal signal);
    public abstract Task OnInitTimelineRunAsync(InitTimelineRunSignal signal);
    public abstract Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal);
    public abstract Task OnValueUpdateAsync(ValueUpdateSignal signal);
    public abstract Task OnLogEntryAsync(LogEntrySignal signal);
    public abstract Task OnAssertionAsync(AssertionSignal signal);
    public abstract Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal);
}