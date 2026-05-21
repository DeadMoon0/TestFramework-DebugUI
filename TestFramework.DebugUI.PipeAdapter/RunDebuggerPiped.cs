using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;

namespace TestFramework.DebugUI.PipeAdapter;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class RunDebuggerPiped : IRunDebugger, IDisposable
{
    private readonly PipeClient client = PipeStreamController.CreateClient();

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

    public Task SignalEntityTransitionAsync(string sessionId, DebugEntityKind entityKind, string? stage, int? stepId, DebugLifecycleState state, DebugLifecycleState? previousState = null, DebugLifecycleState? outcomeState = null)
    {
        return client.SignalAsync(new EntityTransitionSignal
        {
            SessionId = sessionId,
            EntityKind = entityKind,
            Stage = stage,
            StepId = stepId,
            PreviousState = previousState,
            OutcomeState = outcomeState,
            State = state
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

    public Task SignalValueUpdateAsync(string sessionId, string name, DebugValueKind valueKind, string? stage, int? stepId, DebugValueEnvelope value)
    {
        return client.SignalAsync(new ValueUpdateSignal
        {
            SessionId = sessionId,
            Name = name,
            ValueKind = valueKind,
            Stage = stage,
            StepId = stepId,
            Envelope = value
        });
    }

    public Task SignalLogEntryAsync(string sessionId, DebugLogEntry entry)
    {
        return client.SignalAsync(new LogEntrySignal
        {
            SessionId = sessionId,
            Entry = entry
        });
    }

    public Task SignalAssertionAsync(string sessionId, DebugAssertionEntry entry)
    {
        return client.SignalAsync(new AssertionSignal
        {
            SessionId = sessionId,
            Entry = entry
        });
    }

    public async Task SignalTimelineRunFinishedAsync(string sessionId)
    {
        await client.SignalAsync(new TimelineRunFinishedSignal
        {
            SessionId = sessionId
        });
        await client.WaitForFlushedAsync();
        client.Dispose();
    }

    public void Dispose()
    {
        client.Dispose();
    }
}