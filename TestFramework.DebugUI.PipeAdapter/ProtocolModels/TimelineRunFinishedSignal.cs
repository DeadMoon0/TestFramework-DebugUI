using Newtonsoft.Json;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record TimelineRunFinishedSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.TimelineRunFinished;

    public required string SessionId { get; init; }
}
