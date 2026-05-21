using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Root bindable state for the DebugUI application.
/// It tracks the currently active run snapshot plus the current pipe transport state.
/// </summary>
public class MainState : StateObject
{
    public RunState? ActiveRun { get => GetValue(ActiveRunProperty); set => SetValue(ActiveRunProperty, value); }
    public static StateProperty<RunState?> ActiveRunProperty { get; } = Property<RunState?>(nameof(ActiveRun), null);

    public PipeConnectionState PipeConnection { get => GetValue(PipeConnectionProperty); set => SetValue(PipeConnectionProperty, value); }
    public static StateProperty<PipeConnectionState> PipeConnectionProperty { get; } = Property(nameof(PipeConnection), new PipeConnectionState());
}