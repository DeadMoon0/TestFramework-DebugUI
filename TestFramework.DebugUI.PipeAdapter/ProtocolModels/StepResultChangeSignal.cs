using Newtonsoft.Json;
using TestFramework.Core.Steps;
using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record StepResultChangeSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.StepResultChange;

    public required string SessionId { get; init; }
    public required StepResultGeneric Result { get; init; }
}