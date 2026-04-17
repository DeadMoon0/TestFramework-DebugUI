using Newtonsoft.Json;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record BreakpointHitContinueSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.BreakpointHitContinue;
}