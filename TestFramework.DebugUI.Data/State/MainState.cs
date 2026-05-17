using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Root bindable state for the DebugUI application.
/// It only tracks the currently active run snapshot that the UI should render.
/// </summary>
public class MainState : StateObject
{
    public RunState? ActiveRun { get => GetValue(ActiveRunProperty); set => SetValue(ActiveRunProperty, value); }
    public static StateProperty<RunState?> ActiveRunProperty { get; } = Property<RunState?>(nameof(ActiveRun), null);
}