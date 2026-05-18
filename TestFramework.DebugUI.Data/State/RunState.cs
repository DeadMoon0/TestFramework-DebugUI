using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one whole timeline run.
/// It describes run-level metadata, the shared variable and artifact stores, and the stage collection that owns all execution state.
/// </summary>
public class RunState : StateObject
{
    public string SessionId { get => GetValue(SessionIdProperty); set => SetValue(SessionIdProperty, value); }
    public static StateProperty<string> SessionIdProperty { get; } = Property(nameof(SessionId), "");

    public DebugLifecycleState LifecycleState { get => GetValue(LifecycleStateProperty); set => SetValue(LifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState> LifecycleStateProperty { get; } = Property(nameof(LifecycleState), DebugLifecycleState.Initialized);

    public DebugLifecycleState? PreviousLifecycleState { get => GetValue(PreviousLifecycleStateProperty); set => SetValue(PreviousLifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState?> PreviousLifecycleStateProperty { get; } = Property<DebugLifecycleState?>(nameof(PreviousLifecycleState), null);

    public DateTimeOffset? LastTransitionAtUtc { get => GetValue(LastTransitionAtUtcProperty); set => SetValue(LastTransitionAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastTransitionAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastTransitionAtUtc), null);

    public bool IsFinished { get => GetValue(IsFinishedProperty); set => SetValue(IsFinishedProperty, value); }
    public static StateProperty<bool> IsFinishedProperty { get; } = Property(nameof(IsFinished), false);

    public DateTimeOffset? FinishedAtUtc { get => GetValue(FinishedAtUtcProperty); set => SetValue(FinishedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> FinishedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(FinishedAtUtc), null);

    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public string ProjectPath { get => GetValue(ProjectPathProperty); set => SetValue(ProjectPathProperty, value); }
    public static StateProperty<string> ProjectPathProperty { get; } = Property(nameof(ProjectPath), "");

    public StateDictionary<AssertionEntryState> Assertions { get => GetValue(AssertionsProperty); set => SetValue(AssertionsProperty, value); }
    public static StateProperty<StateDictionary<AssertionEntryState>> AssertionsProperty { get; } = Property(nameof(Assertions), new StateDictionary<AssertionEntryState>());

    public StateDictionary<DebugValueState> Artifacts { get => GetValue(ArtifactsProperty); set => SetValue(ArtifactsProperty, value); }
    public static StateProperty<StateDictionary<DebugValueState>> ArtifactsProperty { get; } = Property(nameof(Artifacts), new StateDictionary<DebugValueState>());

    public StateDictionary<DebugValueState> Variables { get => GetValue(VariablesProperty); set => SetValue(VariablesProperty, value); }
    public static StateProperty<StateDictionary<DebugValueState>> VariablesProperty { get; } = Property(nameof(Variables), new StateDictionary<DebugValueState>());

    public StateDictionary<StageNodeState> Stages { get => GetValue(StagesProperty); set => SetValue(StagesProperty, value); }
    public static StateProperty<StateDictionary<StageNodeState>> StagesProperty { get; } = Property(nameof(Stages), new StateDictionary<StageNodeState>());
}