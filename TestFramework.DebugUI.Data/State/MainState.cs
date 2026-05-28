using WpfStateService.Common;
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

    public bool HasPendingBreakpoint { get => GetValue(HasPendingBreakpointProperty); set => SetValue(HasPendingBreakpointProperty, value); }
    public static StateProperty<bool> HasPendingBreakpointProperty { get; } = Property(nameof(HasPendingBreakpoint), false);

    public StateDictionary<RunState> CompletedRuns { get => GetValue(CompletedRunsProperty); set => SetValue(CompletedRunsProperty, value); }
    public static StateProperty<StateDictionary<RunState>> CompletedRunsProperty { get; } = Property(nameof(CompletedRuns), new StateDictionary<RunState>());

    public PipeConnectionState PipeConnection { get => GetValue(PipeConnectionProperty); set => SetValue(PipeConnectionProperty, value); }
    public static StateProperty<PipeConnectionState> PipeConnectionProperty { get; } = Property(nameof(PipeConnection), new PipeConnectionState());

    /// <summary>
    /// Persistent user breakpoint configuration set keyed by Project+Test+Stage+Step.
    /// </summary>
    public StateDictionary<BreakpointConfig> BreakpointConfigs { get => GetValue(BreakpointConfigsProperty); set => SetValue(BreakpointConfigsProperty, value); }
    public static StateProperty<StateDictionary<BreakpointConfig>> BreakpointConfigsProperty { get; } = Property(nameof(BreakpointConfigs), new StateDictionary<BreakpointConfig>());
}

/// <summary>
/// Holds the persistent user breakpoint selection.
/// </summary>
public class BreakpointConfig
{
    public string Key { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int StepId { get; set; }

    public static string CreateKey(string projectPath, string testName, string stageName, int stepId)
    {
        return $"{projectPath}|{testName}|{stageName}|{stepId}";
    }
}