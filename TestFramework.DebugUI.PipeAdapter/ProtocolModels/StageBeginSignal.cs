using Newtonsoft.Json;
using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record StageBeginSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.StageBegin;

    public required string SessionId { get; init; }
    public required string Name { get; init; }
}