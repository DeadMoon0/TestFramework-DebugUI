using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one concrete execution attempt of a step.
/// It describes attempt-scoped data only: timing, status, log stream, and the aggregated debug text for that single attempt or retry.
/// </summary>
public class StepAttemptState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public int AttemptNumber { get => GetValue(AttemptNumberProperty); set => SetValue(AttemptNumberProperty, value); }
    public static StateProperty<int> AttemptNumberProperty { get; } = Property(nameof(AttemptNumber), 0);

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public static StateProperty<bool> IsActiveProperty { get; } = Property(nameof(IsActive), false);

    public DateTimeOffset? StartedAtUtc { get => GetValue(StartedAtUtcProperty); set => SetValue(StartedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> StartedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(StartedAtUtc), null);

    public DateTimeOffset? FinishedAtUtc { get => GetValue(FinishedAtUtcProperty); set => SetValue(FinishedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> FinishedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(FinishedAtUtc), null);

    public DebugLifecycleState LifecycleState { get => GetValue(LifecycleStateProperty); set => SetValue(LifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState> LifecycleStateProperty { get; } = Property(nameof(LifecycleState), DebugLifecycleState.Initialized);

    public StateDictionary<LogEntryState> LogEntries { get => GetValue(LogEntriesProperty); set => SetValue(LogEntriesProperty, value); }
    public static StateProperty<StateDictionary<LogEntryState>> LogEntriesProperty { get; } = Property(nameof(LogEntries), new StateDictionary<LogEntryState>());
}