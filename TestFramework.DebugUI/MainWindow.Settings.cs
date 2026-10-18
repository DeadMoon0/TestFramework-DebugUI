using System;
using System.Windows;
using TestFramework.DebugUI.Controls.Settings;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI;

// The settings panel, the themes it offers, and the settings file behind both.
public partial class MainWindow
{
    private void btSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ucSettings.Visibility == Visibility.Visible)
        {
            ucSettings.Visibility = Visibility.Collapsed;
            return;
        }

        ShowSettings();
    }

    /// <summary>
    /// Redraws the theme picker after Windows has changed its mind about compositor effects.
    /// </summary>
    /// <remarks>
    /// Only while the panel is open. A picker nobody is looking at is rebuilt when it is next shown,
    /// which is what <see cref="ShowSettings"/> does anyway.
    /// </remarks>
    private void RefreshThemes()
    {
        if (ucSettings.Visibility == Visibility.Visible)
            ucSettings.ShowThemes(theme.Available, theme.Current.Id, BlurSupport.Detect());
    }

    /// <summary>Fills the settings panel in and brings it out.</summary>
    /// <remarks>
    /// Blur support is asked for here rather than kept, because it is not the tool's to decide and can
    /// change between one opening of the panel and the next — a laptop crossing a battery threshold
    /// turns energy saver on with no window involved.
    /// </remarks>
    private void ShowSettings()
    {
        PublishRecording();

        ucSettings.Show(
            saved.Watch,
            settings.FilePath,
            saved.BreakOnFailure,
            theme.Available,
            theme.Current.Id,
            theme.Store.DirectoryPath,
            BlurSupport.Detect());
    }

    /// <summary>
    /// Reads the recording marker into the state.
    /// </summary>
    /// <remarks>
    /// At start and whenever the settings open, rather than once: the launcher, or a person in Explorer, can
    /// change the marker without this window being involved.
    /// </remarks>
    private void PublishRecording()
        => store.Dispatch(ShellActions.SetRecording, recording?.Read() ?? RecordingState.Unknown);

    /// <summary>
    /// Shows the themes folder, having first put something in it.
    /// </summary>
    /// <remarks>
    /// The example is written on the way, so the folder a first-time reader opens explains the format
    /// instead of being empty. It is never overwritten — the one file somebody is most likely to have
    /// edited is the one the tool put there.
    /// </remarks>
    private void OpenThemesFolder()
    {
        theme.Store.WriteExample();

        UC_Settings.Reveal(theme.Store.DirectoryPath);
    }

    /// <summary>Says why a theme file could not be used, and carries on with the ones that could.</summary>
    private void ReportTheme(string problem)
        => shell?.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = FeedSeverity.Warning,
            Source = FeedSource.App,
            Title = "A theme could not be used.",
            Detail = problem
        });

    private void SaveBreakpoints() => Persist(saved with { Breakpoints = breakpoints.Snapshot() });

    private void Persist(UiSettings next)
    {
        saved = next;
        settings.Save(saved);
    }

    private void SaveLayout() => Persist(saved with { Layout = arrangement.Current });
}
