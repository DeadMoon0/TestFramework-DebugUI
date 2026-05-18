using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one stage inside a run.
/// It describes stage lifecycle, the logical step collection, and the dependency layers that may run side-by-side inside the stage.
/// </summary>
public class StageNodeState : StateObject
{
    public DebugLifecycleState LifecycleState { get => GetValue(LifecycleStateProperty); set => SetValue(LifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState> LifecycleStateProperty { get; } = Property(nameof(LifecycleState), DebugLifecycleState.Initialized);

    public DebugLifecycleState? PreviousLifecycleState { get => GetValue(PreviousLifecycleStateProperty); set => SetValue(PreviousLifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState?> PreviousLifecycleStateProperty { get; } = Property<DebugLifecycleState?>(nameof(PreviousLifecycleState), null);

    public DateTimeOffset? LastTransitionAtUtc { get => GetValue(LastTransitionAtUtcProperty); set => SetValue(LastTransitionAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastTransitionAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastTransitionAtUtc), null);

    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public static StateProperty<string> DescriptionProperty { get; } = Property(nameof(Description), "");

    public int Order { get => GetValue(OrderProperty); set => SetValue(OrderProperty, value); }
    public static StateProperty<int> OrderProperty { get; } = Property(nameof(Order), 0);

    public StateDictionary<StageLayerState> ExecutionLayers { get => GetValue(ExecutionLayersProperty); set => SetValue(ExecutionLayersProperty, value); }
    public static StateProperty<StateDictionary<StageLayerState>> ExecutionLayersProperty { get; } = Property(nameof(ExecutionLayers), new StateDictionary<StageLayerState>());

    public StateDictionary<StepNodeState> Steps { get => GetValue(StepsProperty); set => SetValue(StepsProperty, value); }
    public static StateProperty<StateDictionary<StepNodeState>> StepsProperty { get; } = Property(nameof(Steps), new StateDictionary<StepNodeState>());
}