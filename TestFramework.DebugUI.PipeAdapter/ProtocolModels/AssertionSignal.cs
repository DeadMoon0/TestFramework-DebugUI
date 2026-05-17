using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record AssertionSignal : ISignal
{
    public SignalKind Kind { get; set; } = SignalKind.Assertion;

    public string SessionId { get; set; } = "";

    public DebugAssertionEntry Entry { get; set; } = new();
}