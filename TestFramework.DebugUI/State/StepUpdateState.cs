using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;
namespace TestFramework.DebugUI.State;

public class StepUpdateState : StateObject
{
    public StepState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public static StateProperty<StepState> StateProperty { get; } = Property(nameof(State), StepState.NotRun);

    public bool HasStarted { get => GetValue(HasStartedProperty); set => SetValue(HasStartedProperty, value); }
    public static StateProperty<bool> HasStartedProperty { get; } = Property(nameof(HasStarted), false);

    public string DebugOut { get => GetValue(DebugOutProperty); set => SetValue(DebugOutProperty, value); }
    public static StateProperty<string> DebugOutProperty { get; } = Property(nameof(DebugOut), "");

    public StateDictionary<ArtifactState> OutputArtifacts { get => GetValue(OutputArtifactsProperty); set => SetValue(OutputArtifactsProperty, value); }
    public static StateProperty<StateDictionary<ArtifactState>> OutputArtifactsProperty { get; } = Property(nameof(OutputArtifacts), new StateDictionary<ArtifactState>());
    public StateDictionary<VariableState> OutputVariables { get => GetValue(OutputVariablesProperty); set => SetValue(OutputVariablesProperty, value); }
    public static StateProperty<StateDictionary<VariableState>> OutputVariablesProperty { get; } = Property(nameof(OutputVariables), new StateDictionary<VariableState>());
    
    public StateDictionary<ArtifactState> InputArtifacts { get => GetValue(InputArtifactsProperty); set => SetValue(InputArtifactsProperty, value); }
    public static StateProperty<StateDictionary<ArtifactState>> InputArtifactsProperty { get; } = Property(nameof(InputArtifacts), new StateDictionary<ArtifactState>());
    public StateDictionary<VariableState> InputVariables { get => GetValue(InputVariablesProperty); set => SetValue(InputVariablesProperty, value); }
    public static StateProperty<StateDictionary<VariableState>> InputVariablesProperty { get; } = Property(nameof(InputVariables), new StateDictionary<VariableState>());
}