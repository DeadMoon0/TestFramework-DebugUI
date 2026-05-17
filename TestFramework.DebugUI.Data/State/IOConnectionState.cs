using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Direction of a step IO connection.
/// </summary>
public enum IOConnectionDirection
{
    Input,
    Output
}

/// <summary>
/// Bindable state for one declared or observed IO connection on a step.
/// It describes a single variable or artifact slot together with its direction, requirement metadata, and latest observed value.
/// </summary>
public class IOConnectionState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public IOConnectionDirection Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public static StateProperty<IOConnectionDirection> DirectionProperty { get; } = Property(nameof(Direction), IOConnectionDirection.Input);

    public StepIOKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public static StateProperty<StepIOKind> KindProperty { get; } = Property(nameof(Kind), StepIOKind.Variable);

    public bool IsRequired { get => GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    public static StateProperty<bool> IsRequiredProperty { get; } = Property(nameof(IsRequired), false);

    public string DeclaredTypeName { get => GetValue(DeclaredTypeNameProperty); set => SetValue(DeclaredTypeNameProperty, value); }
    public static StateProperty<string> DeclaredTypeNameProperty { get; } = Property(nameof(DeclaredTypeName), "");

    public bool HasValue { get => GetValue(HasValueProperty); set => SetValue(HasValueProperty, value); }
    public static StateProperty<bool> HasValueProperty { get; } = Property(nameof(HasValue), false);

    public string DisplayText { get => GetValue(DisplayTextProperty); set => SetValue(DisplayTextProperty, value); }
    public static StateProperty<string> DisplayTextProperty { get; } = Property(nameof(DisplayText), "");

    public DebugValueEnvelope Envelope { get => GetValue(EnvelopeProperty); set => SetValue(EnvelopeProperty, value); }
    public static StateProperty<DebugValueEnvelope> EnvelopeProperty { get; } = Property<DebugValueEnvelope>(nameof(Envelope), null!);
}