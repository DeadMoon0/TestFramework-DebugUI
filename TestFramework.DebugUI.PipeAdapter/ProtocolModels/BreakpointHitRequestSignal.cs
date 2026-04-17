using Newtonsoft.Json;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record BreakpointHitRequestSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.BreakpointHitRequest;

    public required string SessionId { get; init; }
    public required string Stage { get; init; }
    public required int StepId { get; init; }
}