using Newtonsoft.Json;
using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record StepBeginSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.StepBegin;

    public required string SessionId { get; init; }
    public required int StepId { get; init; }
}