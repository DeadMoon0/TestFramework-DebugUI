using System;
using Axiom.State.Effects;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// Starts the work that a change to the run catalogue needs done outside the store.
/// </summary>
/// <remarks>
/// Only the re-run lives here so far. Listing the runs on disk is still the shell controller's, and
/// deliberately: it is a directory read that belongs with the journal transport rather than an
/// action-triggered chain, and the window expects the list to be there when the call returns.
/// </remarks>
public sealed class RunsEffects : Effects<MainState>
{
    /// <summary>
    /// Registers the effect against the runner that starts a test again.
    /// </summary>
    /// <param name="rerunner">Builds and runs the test a run came from.</param>
    public RunsEffects(TestRerunner rerunner)
    {
        ArgumentNullException.ThrowIfNull(rerunner);

        On(MainActions.RerunSelected, state => Rerun(rerunner, state));
    }

    /// <summary>
    /// Runs the selected run's test again.
    /// </summary>
    /// <remarks>
    /// Nothing here waits for the new run or tracks it: it arrives over the pipe like any other. The
    /// intent was already recorded by <see cref="MainReducer"/> when the action reduced, which is
    /// before this starts, so the run cannot arrive before the UI knows to show it.
    /// </remarks>
    private EffectResult<MainState> Rerun(TestRerunner rerunner, MainState state)
    {
        RunSummary? run = state.Runs.SelectedSessionId is { } sessionId
            ? state.Runs.All.Find(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal))
            : null;

        if (run is null || !RerunCommand.IsAvailableFor(run))
            return DoNothing();

        return DoAction(
            () => rerunner.RerunAsync(run),

            // The runner reports the cases it knows about — a test that cannot be built, a host that
            // will not start — through the feed itself, so a false answer has already been explained.
            _ => DoNothing(),

            // A throw has not been explained by anyone, so it is said here rather than swallowed.
            error => Do(FeedActions.AppendEntry, new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Error,
                Source = FeedSource.Rerun,
                Title = "The test could not be run again.",
                Detail = error.Message,
                SessionId = run.SessionId
            }));
    }
}
