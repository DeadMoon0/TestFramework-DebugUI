using System;
using Newtonsoft.Json;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record EntityTransitionSignal : ISignal
{
    [JsonProperty]
    public SignalKind Kind => SignalKind.EntityTransition;

    public required string SessionId { get; init; }
    public required DebugEntityKind EntityKind { get; init; }
    public string? Stage { get; init; }
    public int? StepId { get; init; }
    public DebugLifecycleState? PreviousState { get; init; }
    public DebugLifecycleState? OutcomeState { get; init; }
    public required DebugLifecycleState State { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}