using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public record LogEntrySignal : ISignal
{
    public SignalKind Kind { get; set; } = SignalKind.LogEntry;

    public string SessionId { get; set; } = "";

    public DebugLogEntry Entry { get; set; } = new();
}