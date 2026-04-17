using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

public class MainState : StateObject
{
    public RunState? ActiveRun { get => GetValue(ActiveRunProperty); set => SetValue(ActiveRunProperty, value); }
    public static StateProperty<RunState?> ActiveRunProperty { get; } = Property<RunState?>(nameof(ActiveRun), null);

    //TODO: RunSessions, Projects/Runs
}