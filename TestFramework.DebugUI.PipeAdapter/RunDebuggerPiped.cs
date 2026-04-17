using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class RunDebuggerPiped : IRunDebugger
{
    private PipeClient client = PipeStreamController.CreateClient();

    public static IRunDebugger CreateNew() => new RunDebuggerPiped();

    public async Task SignalAndWaitBreakpointHitAsync(string sessionId, string stage, int stepId)
    {
        await client.SignalAsync(new BreakpointHitRequestSignal
        {
            SessionId = sessionId,
            Stage = stage,
            StepId = stepId
        });
        await client.WaitForAsync(SignalKind.BreakpointHitContinue);
    }

    public Task SignalArtifactUpdateAsync(string sessionId, string name, ArtifactState artifact)
    {
        return client.SignalAsync(new ArtifactUpdateSignal
        {
            SessionId = sessionId,
            Name = name,
            Artifact = artifact
        });
    }

    public Task SignalInitTimelineRunAsync(string sessionId, string name, string projectPath, TimelineRunStructure runStructure)
    {
        return client.SignalAsync(new InitTimelineRunSignal
        {
            SessionId = sessionId,
            Name = name,
            ProjectPath = projectPath,
            RunStructure = runStructure
        });
    }

    public Task SignalStageBeginAsync(string sessionId, string name)
    {
        return client.SignalAsync(new StageBeginSignal
        {
            SessionId = sessionId,
            Name = name
        });
    }

    public Task SignalStepBeginAsync(string sessionId, int stepId)
    {
        return client.SignalAsync(new StepBeginSignal
        {
            SessionId = sessionId,
            StepId = stepId
        });
    }

    public Task SignalStepResultChangeAsync(string sessionId, StepResultGeneric result)
    {
        return client.SignalAsync(new StepResultChangeSignal
        {
            SessionId = sessionId,
            Result = result
        });
    }

    public async Task SignalTimelineRunFinishedAsync(string sessionId)
    {
        await client.SignalAsync(new TimelineRunFinishedSignal
        {
            SessionId = sessionId
        });
        await client.WaitForFlushedAsync();
    }

    public Task SignalVariableUpdateAsync(string sessionId, string name, VariableState variable)
    {
        return client.SignalAsync(new VariableUpdateSignal
        {
            SessionId = sessionId,
            Name = name,
            Variable = variable
        });
    }
}