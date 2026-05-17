using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one concrete execution attempt of a step.
/// It describes attempt-scoped data only: timing, status, log stream, and the aggregated debug text for that single iteration or retry.
/// </summary>
public class StepAttemptState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public int IterationNumber { get => GetValue(IterationNumberProperty); set => SetValue(IterationNumberProperty, value); }
    public static StateProperty<int> IterationNumberProperty { get; } = Property(nameof(IterationNumber), 0);

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public static StateProperty<bool> IsActiveProperty { get; } = Property(nameof(IsActive), false);

    public DateTimeOffset? StartedAtUtc { get => GetValue(StartedAtUtcProperty); set => SetValue(StartedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> StartedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(StartedAtUtc), null);

    public DateTimeOffset? FinishedAtUtc { get => GetValue(FinishedAtUtcProperty); set => SetValue(FinishedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> FinishedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(FinishedAtUtc), null);

    public DebugLifecycleState LifecycleState { get => GetValue(LifecycleStateProperty); set => SetValue(LifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState> LifecycleStateProperty { get; } = Property(nameof(LifecycleState), DebugLifecycleState.Initialized);

    public string DebugOut { get => GetValue(DebugOutProperty); set => SetValue(DebugOutProperty, value); }
    public static StateProperty<string> DebugOutProperty { get; } = Property(nameof(DebugOut), "");

    public StateDictionary<LogEntryState> LogEntries { get => GetValue(LogEntriesProperty); set => SetValue(LogEntriesProperty, value); }
    public static StateProperty<StateDictionary<LogEntryState>> LogEntriesProperty { get; } = Property(nameof(LogEntries), new StateDictionary<LogEntryState>());

    public LogEntryState LatestLogEntry { get => GetValue(LatestLogEntryProperty); set => SetValue(LatestLogEntryProperty, value); }
    public static StateProperty<LogEntryState> LatestLogEntryProperty { get; } = Property<LogEntryState>(nameof(LatestLogEntry), null!);
}