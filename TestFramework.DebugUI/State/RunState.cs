using TestFramework.Core.Debugger;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

public class RunState : StateObject
{
    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public string ProjectPath { get => GetValue(ProjectPathProperty); set => SetValue(ProjectPathProperty, value); }
    public static StateProperty<string> ProjectPathProperty { get; } = Property(nameof(ProjectPath), "");

    public TimelineRunStructure Structure { get => GetValue(StructureProperty); set => SetValue(StructureProperty, value); }
    public static StateProperty<TimelineRunStructure> StructureProperty { get; } = Property<TimelineRunStructure>(nameof(Structure), null!);

    public StageUpdateState ActiveStage { get => GetValue(ActiveStageProperty); set => SetValue(ActiveStageProperty, value); }
    public static StateProperty<StageUpdateState> ActiveStageProperty { get; } = Property<StageUpdateState>(nameof(ActiveStage), null!);

    public StateDictionary<StageUpdateState> StageUpdates { get => GetValue(StageUpdatesProperty); set => SetValue(StageUpdatesProperty, value); }
    public static StateProperty<StateDictionary<StageUpdateState>> StageUpdatesProperty { get; } = Property(nameof(StageUpdates), new StateDictionary<StageUpdateState>());
}