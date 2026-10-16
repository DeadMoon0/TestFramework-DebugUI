using System;
using System.Windows;
using System.Windows.Threading;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI;

// Surviving a failure in the window, and closing it: to the tray while watching, for real otherwise.
public partial class MainWindow
{
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        shell.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = FeedSeverity.Error,
            Source = FeedSource.App,
            Title = "Something in the window failed.",
            Detail = e.Exception.Message
        });
    }

    /// <summary>
    /// Whether this close is the real one.
    /// </summary>
    /// <remarks>
    /// Set only by the tray's own exit. Without it, the close that quitting performs would be caught by
    /// the same branch that hides the window, and the application could never be shut down at all.
    /// </remarks>
    private bool exiting;

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // While watching, closing puts the window away rather than ending the session: the point of the
        // mode is that the tool outlives the window and speaks up when a run finishes, and a close that
        // killed it would make the mode do nothing the moment anyone tidied their desktop. Quitting is on
        // the tray icon's own menu, which is why this can be allowed to swallow a close at all.
        if (!exiting && watch.IsWatching && saved.Watch.Enabled)
        {
            e.Cancel = true;

            // Saved before hiding: from here the process may be killed with the window never shown again.
            Persist(saved with { Window = WindowChromeInterop.Capture(this), Breakpoints = breakpoints.Snapshot() });
            HideToTray();

            return;
        }

        Persist(saved with { Window = WindowChromeInterop.Capture(this), Breakpoints = breakpoints.Snapshot() });

        breakpoints.Changed -= SaveBreakpoints;
        Application.Current.DispatcherUnhandledException -= OnDispatcherUnhandledException;

        // Before the store goes, since it is what is being observed.
        halts?.Dispose();
        picks?.Dispose();
        halts = null;

        notifications?.Dispose();
        notifications = null;

        watch.Stop();

        shell.Dispose();
        breakpoints.Dispose();

        store.Dispose();

        // Hiding rather than closing leaves the process alive with no window, so the shutdown has to
        // be explicit once the last window really does go.
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Quits, from the tray rather than from the title bar.
    /// </summary>
    /// <remarks>
    /// Shown first, then closed. Closing a hidden window works, but it means the last thing the user
    /// saw was a notification and the next thing is nothing at all - and if the close is going to
    /// prompt or stall, it has to be somewhere they can see it.
    /// </remarks>
    public void ExitFromTray()
    {
        exiting = true;

        Show();
        Close();
    }
}
