namespace TestFramework.DebugUI.State.Runs;

/// <summary>How a run stands, in the one word a list of runs needs.</summary>
public enum RunHealth
{
    /// <summary>The run is on disk but has not been read, so nothing is claimed about it.</summary>
    Unknown,

    /// <summary>The run is producing events.</summary>
    Running,

    /// <summary>A step is held at a breakpoint, waiting to be released.</summary>
    Waiting,

    /// <summary>The run stopped without reaching its finish, which is how a killed host looks.</summary>
    Aborted,

    /// <summary>Something failed: a step, an assertion, or both.</summary>
    Failed,

    /// <summary>Nothing failed, but the run asserted nothing, so it proved nothing.</summary>
    Unproven,

    /// <summary>Every step passed and every assertion held.</summary>
    Passed
}
