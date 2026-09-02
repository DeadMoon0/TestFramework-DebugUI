using System.Threading.Tasks;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// What a surface can ask the shell to do.
/// </summary>
/// <remarks>
/// <para>
/// This is what the panels are given instead of reaching for the window. Before it, every control
/// read a static that threw until the window's constructor had run — so each of them carried the
/// unwritten rule "a window exists by now", true only because a control never happens to be built
/// any earlier. A rule kept by luck is a rule, and a control that needed one could not be built in a
/// test at all.
/// </para>
/// <para>
/// Deliberately only the verbs. Reading is what the store and its selectors are for, and a surface
/// that could reach the controller itself would be able to reach the transport underneath it.
/// </para>
/// </remarks>
public interface IShellCommands
{
    /// <summary>Gets where recorded runs are read from, when there is somewhere.</summary>
    string? RunsDirectory { get; }

    /// <summary>Lists what is recorded on disk again, picking up anything new.</summary>
    void RefreshRecordedRuns();

    /// <summary>Shows one run, replaying it from whatever record of it survives.</summary>
    void SelectRun(string sessionId);

    /// <summary>Shows one step's detail.</summary>
    void SelectStep(string stageName, int stepId);

    /// <summary>Releases the selected run from its breakpoint, reporting whether it was held.</summary>
    Task<bool> ContinueSelectedRunAsync();

    /// <summary>Asks the selected run to stop, reporting whether the request was delivered.</summary>
    Task<bool> CancelSelectedRunAsync(string? reason = null);

    /// <summary>
    /// Asks the selected run for a fresh look at itself, reporting whether anything came of it.
    /// </summary>
    /// <remarks>
    /// What the run captures is not returned here. It arrives as an ordinary widget signal and
    /// appears wherever widgets appear, so a surface that calls this waits only to learn whether to
    /// expect anything.
    /// </remarks>
    Task<bool> CaptureWidgetsForSelectedRunAsync();

    /// <summary>Runs the selected run's test again.</summary>
    void RerunSelected();

    /// <summary>Puts something in the message feed.</summary>
    void Report(FeedEntry entry);
}
