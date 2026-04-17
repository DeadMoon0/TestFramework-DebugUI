using Newtonsoft.Json;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record VariableUpdateSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.VariableUpdate;

    public required string SessionId { get; init; }
    public required string Name { get; init; }
    public required VariableState Variable { get; init; }
}