using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

public class StageUpdateState : StateObject
{
    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public bool HasStarted { get => GetValue(HasStartedProperty); set => SetValue(HasStartedProperty, value); }
    public static StateProperty<bool> HasStartedProperty { get; } = Property(nameof(HasStarted), false);

    public StepUpdateState ActiveStep { get => GetValue(ActiveStepProperty); set => SetValue(ActiveStepProperty, value); }
    public static StateProperty<StepUpdateState> ActiveStepProperty { get; } = Property<StepUpdateState>(nameof(ActiveStep), null!);

    public StateDictionary<StepUpdateState> StepUpdates { get => GetValue(StepUpdatesProperty); set => SetValue(StepUpdatesProperty, value); }
    public static StateProperty<StateDictionary<StepUpdateState>> StepUpdatesProperty { get; } = Property(nameof(StepUpdates), new StateDictionary<StepUpdateState>());
}