namespace TestFramework.DebugUI.PipeAdapter;

public enum SignalKind : ushort
{
    ArtifactUpdate,
    EntityTransition,
    InitTimelineRun,
    StageBegin,
    StepBegin,
    StepResultChange,
    TimelineRunFinished,
    ValueUpdate,
    LogEntry,
    Assertion,
    VariableUpdate,
    BreakpointHitRequest,
    BreakpointHitContinue
}