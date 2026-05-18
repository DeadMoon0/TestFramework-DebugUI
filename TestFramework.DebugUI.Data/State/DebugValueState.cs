using TestFramework.Core.Debugger;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

public class DebugValueState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public DebugValueEnvelope Envelope { get => GetValue(EnvelopeProperty); set => SetValue(EnvelopeProperty, value); }
    public static StateProperty<DebugValueEnvelope> EnvelopeProperty { get; } = Property<DebugValueEnvelope>(nameof(Envelope), null!);
}