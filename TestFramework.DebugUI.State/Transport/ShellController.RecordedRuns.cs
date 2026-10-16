using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

// Listing the runs already recorded, and saying which of them this build cannot read.
public sealed partial class ShellController
{
    /// <summary>
    /// Says once, at startup, that some recordings on disk are not this build's to read.
    /// </summary>
    /// <remarks>
    /// The debug protocol is versioned and this build reads one version of it. A recording from another is
    /// left out of the picker rather than opened as an empty board — and saying so once is the difference
    /// between a deliberate omission and a tool that appears to have lost somebody's runs. The files are not
    /// touched.
    /// </remarks>
    private void ReportRecordingsFromOtherBuilds()
    {
        if (runsDirectory is null)
            return;

        try
        {
            int count = JournalRunEventSource.CountFromOtherBuilds(runsDirectory);

            if (count == 0)
                return;

            Report(Notice(
                FeedSeverity.Info,
                count == 1
                    ? "One recorded run was made by another build and is not listed."
                    : $"{count} recorded runs were made by another build and are not listed.",
                $"This build reads debug protocol v{DebugProtocol.Version}. The recordings are still in {runsDirectory}.",
                FeedSource.Journal));
        }
        catch (Exception e)
        {
            Report(Notice(FeedSeverity.Error, "Recorded runs could not be counted.", e.Message, FeedSource.Journal));
        }
    }

    /// <summary>
    /// Re-reads the recorded runs and adds any that are new to the picker.
    /// </summary>
    public void RefreshRecordedRuns()
    {
        if (runsDirectory is null)
            return;

        try
        {
            ImmutableList<RunSummary> recorded =
            [
                .. JournalRunEventSource.ListRuns(runsDirectory).Select(run => new RunSummary
                {
                    SessionId = run.SessionId,
                    Name = run.Name,
                    StartedAtUtc = run.StartedAtUtc,
                    IsLive = false,

                    // A recorded run that never reached its finish signal is one whose host died.
                    // It is listed rather than hidden: a run that stopped unexpectedly is usually
                    // the one worth looking at.
                    IsFinished = run.IsFinished,
                    FinishedAtUtc = run.FinishedAtUtc,
                    FullyQualifiedName = run.FullyQualifiedName,
                    ProjectPath = run.ProjectPath,

                    // Carried through from the sidecar so a run can be repeated straight from the
                    // list. Waiting for someone to open it first made the button's availability
                    // depend on what they had happened to click.
                    ProjectFilePath = run.ProjectFilePath,
                    CanRerun = run.CanRerun,
                    Source = run.Source,
                    EventCount = run.EventCount,
                    JournalPath = run.JournalPath
                })
            ];

            store.Dispatch(RunsActions.AddRecorded, Located(recorded));
        }
        catch (Exception e)
        {
            Log.Write(e);
            Report(Notice(FeedSeverity.Error, "Recorded runs could not be listed.", e.Message, FeedSource.Journal));
        }
    }


    /// <summary>
    /// Points each run at a project that exists on this machine, where the recorded one does not.
    /// </summary>
    /// <remarks>
    /// A run that arrived in a bundle records the sender's paths, so re-run and the editor buttons would both be
    /// unavailable on it even though everything needed to find the project locally is present. Translated here,
    /// once, at the point the picker is built, so nothing downstream has to know a run came from elsewhere.
    /// The journal itself is untouched: this is the projection, not the record.
    /// </remarks>
    private static ImmutableList<RunSummary> Located(ImmutableList<RunSummary> runs)
    {
        ImmutableDictionary<string, string> resolved = ProjectResolution.ResolveAll(runs);

        if (resolved.Count == 0)
            return runs;

        return
        [
            .. runs.Select(run => resolved.TryGetValue(run.SessionId, out string? local)
                ? run with { ProjectFilePath = local }
                : run)
        ];
    }
}
