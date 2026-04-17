namespace TestFramework.DebugUI.PipeAdapter;

public enum SignalKind : ushort
{
    ArtifactUpdate,
    InitTimelineRun,
    StageBegin,
    StepBegin,
    StepResultChange,
    TimelineRunFinished,
    VariableUpdate,
    BreakpointHitRequest,
    BreakpointHitContinue
}