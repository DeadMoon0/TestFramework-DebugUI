using Newtonsoft.Json;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record InitTimelineRunSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.InitTimelineRun;

    public required string SessionId { get; init; }
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
    public required TimelineRunStructure RunStructure { get; init; }
}