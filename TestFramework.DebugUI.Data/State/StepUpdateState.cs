using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using WpfStateService.Common;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for one logical step definition inside a stage.
/// It describes the step across the whole run: lifecycle, retries, breakpoint state, declared and observed IO, and the collection of execution attempts.
/// This object models the logical step node, not a single execution attempt.
/// </summary>
public class StepNodeState : StateObject
{
    public string StageName { get => GetValue(StageNameProperty); set => SetValue(StageNameProperty, value); }
    public static StateProperty<string> StageNameProperty { get; } = Property(nameof(StageName), "");

    public int StepId { get => GetValue(StepIdProperty); set => SetValue(StepIdProperty, value); }
    public static StateProperty<int> StepIdProperty { get; } = Property(nameof(StepId), 0);

    public int Order { get => GetValue(OrderProperty); set => SetValue(OrderProperty, value); }
    public static StateProperty<int> OrderProperty { get; } = Property(nameof(Order), 0);

    public string ExecutionLayerKey { get => GetValue(ExecutionLayerKeyProperty); set => SetValue(ExecutionLayerKeyProperty, value); }
    public static StateProperty<string> ExecutionLayerKeyProperty { get; } = Property(nameof(ExecutionLayerKey), "");

    public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");

    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public static StateProperty<string> DescriptionProperty { get; } = Property(nameof(Description), "");

    public bool DoesReturn { get => GetValue(DoesReturnProperty); set => SetValue(DoesReturnProperty, value); }
    public static StateProperty<bool> DoesReturnProperty { get; } = Property(nameof(DoesReturn), false);

    public StepParallelizationMode ParallelizationMode { get => GetValue(ParallelizationModeProperty); set => SetValue(ParallelizationModeProperty, value); }
    public static StateProperty<StepParallelizationMode> ParallelizationModeProperty { get; } = Property(nameof(ParallelizationMode), StepParallelizationMode.Parallelizable);

    public DebugLifecycleState LifecycleState { get => GetValue(LifecycleStateProperty); set => SetValue(LifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState> LifecycleStateProperty { get; } = Property(nameof(LifecycleState), DebugLifecycleState.Initialized);

    public DebugLifecycleState? PreviousLifecycleState { get => GetValue(PreviousLifecycleStateProperty); set => SetValue(PreviousLifecycleStateProperty, value); }
    public static StateProperty<DebugLifecycleState?> PreviousLifecycleStateProperty { get; } = Property<DebugLifecycleState?>(nameof(PreviousLifecycleState), null);

    public DateTimeOffset? LastTransitionAtUtc { get => GetValue(LastTransitionAtUtcProperty); set => SetValue(LastTransitionAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastTransitionAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastTransitionAtUtc), null);

    public int AttemptCount { get => GetValue(AttemptCountProperty); set => SetValue(AttemptCountProperty, value); }
    public static StateProperty<int> AttemptCountProperty { get; } = Property(nameof(AttemptCount), 0);

    public int BreakpointHitCount { get => GetValue(BreakpointHitCountProperty); set => SetValue(BreakpointHitCountProperty, value); }
    public static StateProperty<int> BreakpointHitCountProperty { get; } = Property(nameof(BreakpointHitCount), 0);

    public bool IsWaitingAtBreakpoint { get => GetValue(IsWaitingAtBreakpointProperty); set => SetValue(IsWaitingAtBreakpointProperty, value); }
    public static StateProperty<bool> IsWaitingAtBreakpointProperty { get; } = Property(nameof(IsWaitingAtBreakpoint), false);

    public DateTimeOffset? LastBreakpointAtUtc { get => GetValue(LastBreakpointAtUtcProperty); set => SetValue(LastBreakpointAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastBreakpointAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastBreakpointAtUtc), null);

    public StepState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public static StateProperty<StepState> StateProperty { get; } = Property(nameof(State), StepState.NotRun);

    public StateDictionary<StepAttemptState> Iterations { get => GetValue(IterationsProperty); set => SetValue(IterationsProperty, value); }
    public static StateProperty<StateDictionary<StepAttemptState>> IterationsProperty { get; } = Property(nameof(Iterations), new StateDictionary<StepAttemptState>());

    public StateDictionary<IOConnectionState> Inputs { get => GetValue(InputsProperty); set => SetValue(InputsProperty, value); }
    public static StateProperty<StateDictionary<IOConnectionState>> InputsProperty { get; } = Property(nameof(Inputs), new StateDictionary<IOConnectionState>());

    public StateDictionary<IOConnectionState> Outputs { get => GetValue(OutputsProperty); set => SetValue(OutputsProperty, value); }
    public static StateProperty<StateDictionary<IOConnectionState>> OutputsProperty { get; } = Property(nameof(Outputs), new StateDictionary<IOConnectionState>());
}