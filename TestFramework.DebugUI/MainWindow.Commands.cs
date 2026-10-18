using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Windows;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.State.Bundles;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI;

// What the keyboard and the title bar ask of the run in view: the shortcuts, stepping, Escape, search and sharing.
public partial class MainWindow
{
    /// <summary>
    /// Points every shortcut at the same method its button uses.
    /// </summary>
    /// <remarks>
    /// The table is the window's, because what a key does is a decision about this window; the
    /// matching underneath it is <see cref="ShortcutRouter"/>'s. Every line here has a button
    /// somewhere that calls the same thing, which is what keeps the two from drifting apart.
    /// </remarks>
    private void BindShortcuts()
    {
        new ShortcutRouter(this)
            .Bind(Shortcuts.Runs, ShowHome)
            .Bind(Shortcuts.Settings, () => btSettings_Click(this, new RoutedEventArgs()))
            .Bind(Shortcuts.ToggleWatch, () => ApplyWatchMode(!saved.Watch.Enabled, announce: true))
            .Bind(Shortcuts.CloseTopmost, CloseTopmost)

            .Bind(Shortcuts.Rerun, Shell.RerunSelected)
            .Bind(Shortcuts.Stop, () => _ = Shell.CancelSelectedRunAsync())
            .Bind(Shortcuts.Continue, () => _ = Shell.ContinueSelectedRunAsync())
            .Bind(Shortcuts.StepForward, () => _ = StepSelectedRunAsync())
            .Bind(Shortcuts.Refresh, Shell.RefreshRecordedRuns)

            .Bind(Shortcuts.Search, ShowSearch)
            .Bind(Shortcuts.Fit, ucBoard.FitToWindow)
            .Bind(Shortcuts.Summary, ucBoard.RequestSummary)
            .Bind(Shortcuts.FirstFailure, ucBoard.GoToFirstFailure);
    }

    /// <summary>
    /// Runs the selected run on to its next step and stops it there.
    /// </summary>
    /// <remarks>
    /// Here rather than in the bar because it takes both halves of the tool — the breakpoints that arm
    /// the stop and the controller that lets the run go — and the order between them is not something a
    /// second caller should have to know. <see cref="BreakpointService.StepThroughAsync"/> owns it.
    /// </remarks>
    private Task<bool> StepSelectedRunAsync()
    {
        string? sessionId = store.GetValue(state => state.Runs.SelectedSessionId);

        return sessionId is null
            ? Task.FromResult(false)
            : breakpoints.StepThroughAsync(sessionId, Shell.ContinueSelectedRunAsync);
    }

    /// <summary>
    /// Closes the overlay in front, and only that one.
    /// </summary>
    /// <remarks>
    /// Ordered by what is actually on top, so Escape peels one layer at a time instead of clearing the
    /// screen. Settings sits above the inspector, which sits above the summary, which sits above the
    /// runs page.
    /// </remarks>
    private void CloseTopmost()
    {
        if (ucFeed.IsOpen)
        {
            ucFeed.Close();
            return;
        }

        if (ucSearch.IsOpen)
        {
            ucSearch.Close();
            return;
        }

        if (ucSettings.Visibility == Visibility.Visible)
        {
            ucSettings.Visibility = Visibility.Collapsed;
            return;
        }

        if (arrangement.Current.IsOpen(PanelId.Inspector))
        {
            Put(PanelId.Inspector);
            return;
        }

        if (ucAnnotate.Visibility == Visibility.Visible)
        {
            StopAnnotating();
            return;
        }

        if (ucExport.Visibility == Visibility.Visible)
        {
            ucExport.Visibility = Visibility.Collapsed;
            return;
        }

        if (arrangement.Current.IsOpen(PanelId.Summary))
        {
            Put(PanelId.Summary);
            return;
        }

        // Last, and only what is in the centre: a page is the thing in front, and the rails down the sides are
        // not something the reader opened, so there is nothing there for Escape to put away.
        if (arrangement.Current.At(DockSide.Center).Active is { } page)
            Put(page);
    }

    /// <summary>
    /// Opens the search bar over the run.
    /// </summary>
    /// <remarks>
    /// The runs page is put away first. Someone searching a run has decided which run they are looking at, and
    /// leaving the picker covering the board would mean every result opened something they could not see.
    /// </remarks>
    private void ShowSearch()
    {
        Put(PanelId.Home);
        ucSearch.Open();
    }

    /// <summary>Offers to share the run being watched.</summary>
    private void ShareSelectedRun()
    {
        RunSummary? run = store.GetValue(RunsSelectors.SelectedRunOf);

        // A live run has nothing on disk yet. Sharing it would send a journal that is still being written.
        if (run?.JournalPath is not { Length: > 0 } journal)
        {
            Shell.Report(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Warning,
                Source = FeedSource.App,
                Title = "This run cannot be shared yet.",
                Detail = "Only a run that has been recorded can be sent; this one is still being written."
            });

            return;
        }

        ShowExport([journal], run.ShortName);
    }

    private void ShowExport(ImmutableList<string> journals, string subject)
        => ucExport.Show(journals, subject, BundleFormat.SuggestedFileName(subject, journals.Count, DateTimeOffset.Now));

    /// <summary>
    /// Says what an export did, in the feed rather than in a box that has to be dismissed.
    /// </summary>
    private void ReportExport(RunBundleWriter.Result result)
    {
        List<string> parts = [$"{result.RunCount} run(s) and {result.FileCount} file(s) written to {System.IO.Path.GetFileName(result.Path)}."];

        if (result.MissingFiles.Count > 0)
            parts.Add($"{result.MissingFiles.Count} file(s) the runs referred to were no longer on disk.");

        if (result.Manifest.IsAnonymous)
            parts.Add("Exported anonymously.");

        // Said either way, because both are things the sender should know without opening the bundle:
        // that pictures were held back, or that pictures went along with nothing having checked them.
        if (result.ImagesExcluded > 0)
            parts.Add($"{result.ImagesExcluded} picture(s) were left out; nothing can check what is in them.");
        else if (result.ImagesIncluded > 0 && result.Manifest.IsAnonymous)
            parts.Add($"{result.ImagesIncluded} picture(s) were included and their contents were not inspected.");

        Shell.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = result.MissingFiles.Count > 0 ? FeedSeverity.Warning : FeedSeverity.Info,
            Source = FeedSource.App,
            Title = "Runs shared.",
            Detail = string.Join(" ", parts)
        });
    }
}
