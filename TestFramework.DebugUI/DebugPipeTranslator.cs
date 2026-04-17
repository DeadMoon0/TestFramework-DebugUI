using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.PipeAdapter.ProtocolModels;
using TestFrameworkDebugUI;

namespace TestFramework.DebugUI;

internal class DebugPipeTranslator : RunDebuggerHostPiped
{
    public override async Task OnArtifactUpdateAsync(ArtifactUpdateSignal signal)
    {
        var step = MainWindow.State.ActiveRun?.ActiveStage?.ActiveStep;
        if (step is null) return;
        var old = step.OutputArtifacts.FirstOrDefault(x => x.Key == signal.Name);
        if (old is { } nnOld) step.OutputArtifacts.Remove(nnOld);
        step.OutputArtifacts.Add(signal.Artifact.Key, signal.Artifact);
    }

    public override async Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal)
    {
        await SendSignalAsync(new BreakpointHitContinueSignal());
    }

    public override async Task OnInitTimelineRunAsync(InitTimelineRunSignal signal)
    {
        MainWindow.State.ActiveRun = new State.RunState
        {
            Name = signal.Name,
            ProjectPath = signal.ProjectPath,
            Structure = signal.RunStructure
        };
        foreach (DebugStageState stage in signal.RunStructure.Stages)
        {
            MainWindow.State.ActiveRun.StageUpdates.Add(stage.Name, new State.StageUpdateState
            {
                HasStarted = false,
                Name = stage.Name
            });
            int id = 0;
            foreach (DebugStepState step in stage.Steps)
            {
                MainWindow.State.ActiveRun.StageUpdates[stage.Name].StepUpdates.Add(id + "", new State.StepUpdateState
                {
                    State = TestFramework.Core.Steps.StepState.NotRun,
                    DebugOut = "",
                    HasStarted = false
                });
                id++;
            }
        }
    }

    public override async Task OnStageBeginAsync(StageBeginSignal signal)
    {
        MainWindow.State.ActiveRun!.StageUpdates[signal.Name].HasStarted = true;
        MainWindow.State.ActiveRun.ActiveStage = MainWindow.State.ActiveRun!.StageUpdates[signal.Name];
    }

    public override async Task OnStepBeginAsync(StepBeginSignal signal)
    {
        VariableState FindOldVarState(string key)
        {
            foreach (var stage in MainWindow.State.ActiveRun!.StageUpdates.Reverse())
            {
                if (stage.Value == null) continue; //TODO: Find out wy I have to do this.
                foreach (var step in stage.Value.StepUpdates.Reverse())
                {
                    if (step.Value == null) continue; //TODO: Find out wy I have to do this.
                    if (step.Value.OutputVariables.FirstOrDefault(x => x.Key == key) is { } found) return found.Value;
                }
            }

            return MainWindow.State.ActiveRun.Structure.Variables.First(x => x.Key == key).Value;
        }

        ArtifactState FindOldArtState(string key)
        {
            foreach (var stage in MainWindow.State.ActiveRun!.StageUpdates.Reverse())
            {
                if (stage.Value == null) continue; //TODO: Find out wy I have to do this.
                foreach (var step in stage.Value.StepUpdates.Reverse())
                {
                    if (step.Value == null) continue; //TODO: Find out wy I have to do this.
                    if (step.Value.OutputArtifacts.FirstOrDefault(x => x.Key == key) is { } found) return found.Value;
                }
            }

            return MainWindow.State.ActiveRun.Structure.Artifacts.First(x => x.Key == key).Value;
        }

        DebugStepState step = MainWindow.State.ActiveRun!.Structure.Stages.First(x => x.Name == MainWindow.State.ActiveRun.ActiveStage.Name).Steps[signal.StepId];
        var stepState = MainWindow.State.ActiveRun!.ActiveStage.StepUpdates[signal.StepId + ""];

        foreach (StepIOEntry ioEntry in step.IOContract.Inputs)
        {
            switch (ioEntry.Kind)
            {
                case StepIOKind.Variable:
                    var varState = FindOldVarState(ioEntry.Key);
                    stepState.InputVariables.Add(varState.Key, varState);
                    break;
                case StepIOKind.Artifact:
                    var artState = FindOldArtState(ioEntry.Key);
                    stepState.InputArtifacts.Add(artState.Key, artState);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(ioEntry.Kind), ioEntry.Kind, null);
            }
        }

        stepState.HasStarted = true;
        MainWindow.State.ActiveRun!.ActiveStage.ActiveStep = stepState;
    }

    public override async Task OnStepResultChangeAsync(StepResultChangeSignal signal)
    {
        MainWindow.State.ActiveRun!.ActiveStage.ActiveStep.State = signal.Result.State;
    }

    public override async Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal)
    {
        //TODO: IDK. Remove?
    }

    public override async Task OnVariableUpdateAsync(VariableUpdateSignal signal)
    {
        var step = MainWindow.State.ActiveRun?.ActiveStage?.ActiveStep;
        if (step is null) return;
        var old = step.OutputVariables.FirstOrDefault(x => x.Key == signal.Name);
        if (old is { } nnOld) step.OutputVariables.Remove(nnOld);
        step.OutputVariables.Add(signal.Variable.Key, signal.Variable);
    }
}