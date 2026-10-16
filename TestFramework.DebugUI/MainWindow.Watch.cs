using System;
using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI;

// Watch mode: the title-bar eye, the notification-area icon, and putting the window away to it and back.
public partial class MainWindow
{
    private void btWatch_Click(object sender, RoutedEventArgs e)
        => ApplyWatchMode(!saved.Watch.Enabled, announce: true);

    /// <summary>
    /// Arms or disarms watch mode, and makes the title bar say which it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The listening is not what is being switched - the transport runs for as long as the process does,
    /// and journalling is independent of it. What this switches is whether there is somewhere for the
    /// window to go that is not "closed", and whether the tool speaks up while it is there.
    /// </para>
    /// <para>
    /// The notification-area icon exists only while the mode is on. An icon that sits there whether or
    /// not it means anything teaches people to ignore it; this way its presence is the indicator.
    /// </para>
    /// </remarks>
    /// <param name="announce">
    /// Whether to say what just happened. False when restoring the saved state at start-up: the user did
    /// not just ask for this, and a notification on every launch is exactly the noise that gets an
    /// application muted.
    /// </param>
    private void ApplyWatchMode(bool enabled, bool announce)
    {
        if (enabled && !watch.IsWatching)
        {
            if (!watch.TryStart(saved.Watch, out string? problem))
            {
                enabled = false;
                ReportWatchFailure(problem);
            }
        }
        else if (!enabled)
        {
            watch.Stop();
        }

        Persist(saved with { Watch = saved.Watch with { Enabled = enabled } });

        watch.Settings = saved.Watch;

        ShowWatchState(enabled);

        if (ucSettings.Visibility == Visibility.Visible)
            ShowSettings();

        if (announce && enabled)
        {
            watch.Notify(
                "Watching for test runs",
                "Close the window and it waits here. Runs that finish will be reported.",
                TrayIcon.Level.Information);
        }
    }

    /// <summary>Says why watch mode could not be armed, and carries on without it.</summary>
    private void ReportWatchFailure(string? problem)
        => shell?.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = FeedSeverity.Warning,
            Source = FeedSource.App,
            Title = "The notification-area icon could not be created.",
            Detail = problem
        });

    /// <summary>Makes the title bar read as armed or not.</summary>
    /// <remarks>
    /// Minimise goes away while the mode is armed. There are two ways to put the window down and the tray
    /// is one of them, so leaving both would mean two buttons a pixel apart that look alike and do
    /// different things - and the taskbar one is the one that stops the tool being able to speak up.
    /// </remarks>
    private void ShowWatchState(bool enabled)
    {
        Brush ink = (Brush)FindResource(enabled ? ThemeKeys.Accent : ThemeKeys.TextSecondary);

        pathWatchOutline.Stroke = ink;
        ellipseWatchPupil.Fill = ink;

        btMinimize.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        btWatch.ToolTip = Shortcuts.Describe(
            enabled ? "Watching. Closing puts it in the notification area" : "Watch for test runs",
            Shortcuts.ToggleWatch);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = saved.Window?.IsMaximized == true ? WindowState.Maximized : WindowState.Normal;

        Surface();

        if (watch.TakePendingRun() is { } sessionId)
            shell?.SelectRun(sessionId);
    }

    private void HideToTray()
    {
        Hide();

        watch.MarkHidden();
    }
}
