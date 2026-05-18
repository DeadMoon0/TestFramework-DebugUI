using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one structured log entry emitted during a step attempt.
/// It describes a single message with its timing, severity, ownership, and indentation metadata.
/// </summary>
public class LogEntryState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public DateTimeOffset OccurredAtUtc { get => GetValue(OccurredAtUtcProperty); set => SetValue(OccurredAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset> OccurredAtUtcProperty { get; } = Property(nameof(OccurredAtUtc), default(DateTimeOffset));

    public DebugLogLevel Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public static StateProperty<DebugLogLevel> LevelProperty { get; } = Property(nameof(Level), DebugLogLevel.Information);

    public string EventName { get => GetValue(EventNameProperty); set => SetValue(EventNameProperty, value); }
    public static StateProperty<string> EventNameProperty { get; } = Property(nameof(EventName), "");

    public string Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public static StateProperty<string> MessageProperty { get; } = Property(nameof(Message), "");

    public int IndentLevel { get => GetValue(IndentLevelProperty); set => SetValue(IndentLevelProperty, value); }
    public static StateProperty<int> IndentLevelProperty { get; } = Property(nameof(IndentLevel), 0);

    public string StageName { get => GetValue(StageNameProperty); set => SetValue(StageNameProperty, value); }
    public static StateProperty<string> StageNameProperty { get; } = Property(nameof(StageName), "");

    public int? StepId { get => GetValue(StepIdProperty); set => SetValue(StepIdProperty, value); }
    public static StateProperty<int?> StepIdProperty { get; } = Property<int?>(nameof(StepId), null);

    public int? AttemptNumber { get => GetValue(AttemptNumberProperty); set => SetValue(AttemptNumberProperty, value); }
    public static StateProperty<int?> AttemptNumberProperty { get; } = Property<int?>(nameof(AttemptNumber), null);

    public string AssertionScope { get => GetValue(AssertionScopeProperty); set => SetValue(AssertionScopeProperty, value); }
    public static StateProperty<string> AssertionScopeProperty { get; } = Property(nameof(AssertionScope), "");
}