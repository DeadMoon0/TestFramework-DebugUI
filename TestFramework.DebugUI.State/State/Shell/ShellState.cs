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
    /// What the transport is doing, in the detail the status popup reports.
    /// </summary>
    /// <remarks>
    /// Put in the state rather than read off the transport when the popup opens. A surface that reached
    /// through to the pipe would be a surface that only works while a shell exists, and it would show
    /// whatever happened to be true at the instant it was asked instead of following the run count.
    /// </remarks>
    public TransportDetails Details = TransportDetails.Unknown;

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

    /// <summary>
    /// How many events arrived but could not be read.
    /// </summary>
    /// <remarks>
    /// An event whose payload will not decode is dropped, because one bad record must not cost the
    /// run that is already on screen. Counted rather than passed over in silence: the transport says
    /// so for what it reads itself, and a journal replayed from disk has no such voice — so without
    /// this a recorded run could quietly be missing part of what it did.
    /// </remarks>
    public int UnreadableEvents = 0;
}
