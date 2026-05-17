using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one assertion result captured during the run.
/// It describes assertion payload only and stays separate from step ownership because assertion signals are intentionally not faked as step events.
/// </summary>
public class AssertionEntryState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public DateTimeOffset OccurredAtUtc { get => GetValue(OccurredAtUtcProperty); set => SetValue(OccurredAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset> OccurredAtUtcProperty { get; } = Property(nameof(OccurredAtUtc), default(DateTimeOffset));

    public DebugAssertionTargetKind TargetKind { get => GetValue(TargetKindProperty); set => SetValue(TargetKindProperty, value); }
    public static StateProperty<DebugAssertionTargetKind> TargetKindProperty { get; } = Property(nameof(TargetKind), DebugAssertionTargetKind.Value);

    public string Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public static StateProperty<string> TargetProperty { get; } = Property(nameof(Target), "");

    public string AssertionName { get => GetValue(AssertionNameProperty); set => SetValue(AssertionNameProperty, value); }
    public static StateProperty<string> AssertionNameProperty { get; } = Property(nameof(AssertionName), "");

    public string AssertionDisplay { get => GetValue(AssertionDisplayProperty); set => SetValue(AssertionDisplayProperty, value); }
    public static StateProperty<string> AssertionDisplayProperty { get; } = Property(nameof(AssertionDisplay), "");

    public bool Succeeded { get => GetValue(SucceededProperty); set => SetValue(SucceededProperty, value); }
    public static StateProperty<bool> SucceededProperty { get; } = Property(nameof(Succeeded), false);

    public string Expected { get => GetValue(ExpectedProperty); set => SetValue(ExpectedProperty, value); }
    public static StateProperty<string> ExpectedProperty { get; } = Property(nameof(Expected), "");

    public string Actual { get => GetValue(ActualProperty); set => SetValue(ActualProperty, value); }
    public static StateProperty<string> ActualProperty { get; } = Property(nameof(Actual), "");

    public string FailureReason { get => GetValue(FailureReasonProperty); set => SetValue(FailureReasonProperty, value); }
    public static StateProperty<string> FailureReasonProperty { get; } = Property(nameof(FailureReason), "");

    public string AssertionScope { get => GetValue(AssertionScopeProperty); set => SetValue(AssertionScopeProperty, value); }
    public static StateProperty<string> AssertionScopeProperty { get; } = Property(nameof(AssertionScope), "");
}