using Newtonsoft.Json;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record ArtifactUpdateSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.ArtifactUpdate;

    public required string SessionId { get; init; }
    public required string Name { get; init; }
    public required ArtifactState Artifact { get; init; }
}