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
                        case SignalKind.ArtifactUpdate:
                            await OnArtifactUpdateAsync((ArtifactUpdateSignal)signal);
                            break;
                        case SignalKind.InitTimelineRun:
                            await OnInitTimelineRunAsync((InitTimelineRunSignal)signal);
                            break;
                        case SignalKind.StageBegin:
                            await OnStageBeginAsync((StageBeginSignal)signal);
                            break;
                        case SignalKind.StepBegin:
                            await OnStepBeginAsync((StepBeginSignal)signal);
                            break;
                        case SignalKind.StepResultChange:
                            await OnStepResultChangeAsync((StepResultChangeSignal)signal);
                            break;
                        case SignalKind.TimelineRunFinished:
                            await OnTimelineRunFinishedAsync((TimelineRunFinishedSignal)signal);
                            break;
                        case SignalKind.VariableUpdate:
                            await OnVariableUpdateAsync((VariableUpdateSignal)signal);
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

    public abstract Task OnArtifactUpdateAsync(ArtifactUpdateSignal signal);
    public abstract Task OnInitTimelineRunAsync(InitTimelineRunSignal signal);
    public abstract Task OnStageBeginAsync(StageBeginSignal signal);
    public abstract Task OnStepBeginAsync(StepBeginSignal signal);
    public abstract Task OnStepResultChangeAsync(StepResultChangeSignal signal);
    public abstract Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal);
    public abstract Task OnVariableUpdateAsync(VariableUpdateSignal signal);
    public abstract Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal);
}