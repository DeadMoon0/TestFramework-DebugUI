using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.State.Shell;

/// <summary>
/// State that belongs to the shell rather than to any one run.
/// </summary>
public record struct ShellState()
{
    /// <summary>How the UI is connected.</summary>
    public TransportStatus Transport = TransportStatus.Idle;

    /// <summary>
    /// The test a re-run was asked for, until its run arrives.
    /// </summary>
    /// <remarks>
    /// Someone who pressed "re-run" is waiting to watch that run, so the run that answers is shown
    /// rather than filed silently behind the one already on screen. Held as the test's name because
    /// that is the only thing known about a run before it exists — the session it will announce
    /// itself under is decided by the test host, not here.
    /// </remarks>
    public string? AwaitedRerun = null;

    /// <summary>The running record of what every session is doing, including unselected ones.</summary>
    public FeedState Feed = new();
}
