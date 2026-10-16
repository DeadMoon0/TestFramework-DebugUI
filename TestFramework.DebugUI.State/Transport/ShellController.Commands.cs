using System;
using System.Threading.Tasks;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.State.Transport;

// What the reader can ask of the selected run: release it, stop it, take a fresh look at it, or run its
// test again.
public sealed partial class ShellController
{
    /// <summary>
    /// Releases the selected run from its breakpoint.
    /// </summary>
    public async Task<bool> ContinueSelectedRunAsync()
    {
        string? sessionId = store.GetValue(state => state.Runs.SelectedSessionId);
        if (sessionId is null)
            return false;

        bool released = await pipe.ContinueAsync(sessionId);

        if (!released)
        {
            Report(Notice(
                FeedSeverity.Warning,
                "The run could not be released.",
                "It is not attached, or it is no longer waiting at a breakpoint.",
                FeedSource.Transport,
                sessionId));
        }

        return released;
    }

    /// <summary>
    /// Asks the selected run to stop.
    /// </summary>
    /// <remarks>
    /// The run stops itself and unwinds through its cleanup stage. Nothing here kills a process:
    /// that would strand the containers, temp files and database rows cleanup exists to remove.
    /// </remarks>
    public async Task<bool> CancelSelectedRunAsync(string? reason = null)
    {
        string? sessionId = store.GetValue(state => state.Runs.SelectedSessionId);
        if (sessionId is null)
            return false;

        bool asked = await pipe.CancelAsync(sessionId, reason ?? "Stopped from the debugger.");

        Report(asked
            ? Notice(FeedSeverity.Info, "The run was asked to stop.", "It will unwind through its cleanup stage.", FeedSource.Transport, sessionId)
            : Notice(FeedSeverity.Warning, "The run could not be asked to stop.", "It is no longer attached.", FeedSource.Transport, sessionId));

        return asked;
    }

    /// <summary>
    /// Asks the selected run for a fresh look at itself.
    /// </summary>
    /// <remarks>
    /// Worth asking only while the run is standing still, which is why the surface offering it is
    /// shown with the breakpoint controls. Every outcome is reported, including the boring one: a
    /// reader who pressed a button and saw nothing appear is owed the reason, and "the run had
    /// nothing to show" is a different answer from "the run never replied".
    /// </remarks>
    public async Task<bool> CaptureWidgetsForSelectedRunAsync()
    {
        string? sessionId = store.GetValue(state => state.Runs.SelectedSessionId);
        if (sessionId is null)
            return false;

        WidgetCaptureReport report = await pipe.CaptureWidgetsAsync(sessionId);

        if (report.Captured > 0)
        {
            Report(Notice(
                FeedSeverity.Info,
                report.Captured == 1 ? "The run recorded a fresh widget." : $"The run recorded {report.Captured} fresh widgets.",
                "It is on the step the run is holding.",
                FeedSource.Transport,
                sessionId));

            return true;
        }

        Report(Notice(
            FeedSeverity.Warning,
            report.Answered ? "The run had nothing fresh to show." : "The run could not be asked for a fresh look.",
            report.Detail,
            FeedSource.Transport,
            sessionId));

        return false;
    }

    /// <summary>
    /// Runs the selected run's test again.
    /// </summary>
    /// <remarks>
    /// One dispatch, and everything else follows from it: <see cref="MainReducer"/> records which
    /// test is being waited for, and <see cref="RunsEffects"/> starts the process. That ordering is
    /// guaranteed rather than arranged — a dispatch reduces before its effects are queued — which is
    /// what the old hand-ordered version here was trying to achieve by dispatching first and starting
    /// the process second.
    /// </remarks>
    public void RerunSelected()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        store.Dispatch(MainActions.RerunSelected);
    }
}
