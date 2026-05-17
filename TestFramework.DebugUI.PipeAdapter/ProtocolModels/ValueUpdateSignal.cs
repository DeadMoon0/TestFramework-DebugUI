using System;
using Newtonsoft.Json;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record ValueUpdateSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.ValueUpdate;

    public required string SessionId { get; init; }
    public required string Name { get; init; }
    public required DebugValueKind ValueKind { get; init; }
    public string? Stage { get; init; }
    public int? StepId { get; init; }
    public required DebugValueEnvelope Envelope { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}