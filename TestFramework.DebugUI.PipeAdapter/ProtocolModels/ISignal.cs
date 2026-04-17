using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.PipeAdapter.ProtocolModels;

public interface ISignal
{
    public SignalKind Kind { get; }
}