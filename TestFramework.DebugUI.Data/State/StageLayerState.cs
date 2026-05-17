using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one dependency-ready execution layer inside a stage.
/// All steps in the same layer may run side-by-side, and the layer is only complete once every step in it has reached a final step state.
/// </summary>
public class StageLayerState : StateObject
{
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public static StateProperty<string> KeyProperty { get; } = Property(nameof(Key), "");

    public int Order { get => GetValue(OrderProperty); set => SetValue(OrderProperty, value); }
    public static StateProperty<int> OrderProperty { get; } = Property(nameof(Order), 0);

    public int[] StepIds { get => GetValue(StepIdsProperty); set => SetValue(StepIdsProperty, value); }
    public static StateProperty<int[]> StepIdsProperty { get; } = Property(nameof(StepIds), System.Array.Empty<int>());

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public static StateProperty<bool> IsActiveProperty { get; } = Property(nameof(IsActive), false);

    public bool IsComplete { get => GetValue(IsCompleteProperty); set => SetValue(IsCompleteProperty, value); }
    public static StateProperty<bool> IsCompleteProperty { get; } = Property(nameof(IsComplete), false);
}