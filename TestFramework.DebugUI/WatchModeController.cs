using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TestFramework.DebugUI.State.Diagnostics;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Settings;

namespace TestFramework.DebugUI;

/// <summary>
/// The half of watch mode that outlives the window: the notification-area icon and the notices.
/// </summary>
/// <remarks>
/// <para>
/// Watch mode does not switch the listening on. The transport runs for as long as the process does and
/// journalling is independent of it; what the mode adds is somewhere for the window to go that is not
/// "closed", and a way for the tool to speak up while it is there. This owns that somewhere.
/// </para>
/// <para>
/// The window keeps what is about the window — how the title bar reads, where it goes when it is shown
/// again, which run it lands on. Those need the window and are decisions about it. What is here needs
/// only the icon and the notifier, and both have a lifetime that is exactly the mode's.
/// </para>
/// </remarks>
internal sealed class WatchModeController
{
    private TrayIcon? tray;
    private WatchNotifier? notifier;

    /// <summary>
    /// The run a notice was about, kept until the window is shown again.
    /// </summary>
    /// <remarks>
    /// Held so that clicking the notification, or the icon, opens the run it was about rather than
    /// whatever happened to be selected before the window was hidden.
    /// </remarks>
    private string? pendingRun;

    /// <summary>Raised when the icon is asked to bring the window back.</summary>
    public event Action? ShowRequested;

    /// <summary>Raised when the icon's menu is used to leave.</summary>
    public event Action? ExitRequested;

    /// <summary>Whether the mode is armed, which is exactly whether the icon is there.</summary>
    public bool IsWatching => tray is not null;

    /// <summary>Passes on what the reader has chosen to be told about.</summary>
    public WatchSettings Settings
    {
        set
        {
            if (notifier is not null)
                notifier.Settings = value;
        }
    }

    /// <summary>
    /// Arms the mode, reporting why it could not be armed.
    /// </summary>
    /// <remarks>
    /// The icon exists only while the mode is on. An icon that sits there whether or not it means
    /// anything teaches people to ignore it; this way its presence is the indicator.
    /// </remarks>
    /// <param name="settings">What the reader has chosen to be told about.</param>
    /// <param name="problem">Why it could not be armed, when it could not.</param>
    /// <returns>Whether the mode is now on.</returns>
    public bool TryStart(WatchSettings settings, out string? problem)
    {
        try
        {
            tray = new TrayIcon("Test Framework Debugger - watching for test runs");
            tray.Activated += OnActivated;
            tray.ContextRequested += ShowMenu;
        }
        catch (Exception e)
        {
            // No tray icon is a smaller problem than no window. Watch mode degrades to "minimises
            // like anything else".
            Log.Write(e);

            problem = e.Message;
            return false;
        }

        notifier = new WatchNotifier { Settings = settings };
        notifier.Finished += OnRunFinished;

        problem = null;
        return true;
    }

    /// <summary>Disarms the mode, taking the icon away with it.</summary>
    public void Stop()
    {
        if (notifier is not null)
        {
            notifier.Finished -= OnRunFinished;
            notifier.Dispose();
            notifier = null;
        }

        if (tray is not null)
        {
            tray.Activated -= OnActivated;
            tray.ContextRequested -= ShowMenu;
            tray.Dispose();
            tray = null;
        }
    }

    /// <summary>Says something through the icon, when there is one.</summary>
    public void Notify(string title, string detail, TrayIcon.Level level) => tray?.Notify(title, detail, level);

    /// <summary>Tells the notifier the window has gone, which is what makes it speak up.</summary>
    public void MarkHidden() => SetHidden(true);

    /// <summary>Tells the notifier the window is back, so it stops speaking up.</summary>
    public void MarkShown() => SetHidden(false);

    private void SetHidden(bool hidden)
    {
        if (notifier is not null)
            notifier.IsHidden = hidden;
    }

    /// <summary>The run the last notice was about, given up as it is read.</summary>
    public string? TakePendingRun()
    {
        string? run = pendingRun;
        pendingRun = null;

        return run;
    }

    private void OnActivated() => ShowRequested?.Invoke();

    private void OnRunFinished(RunFinishedNotice notice)
    {
        bool failed = notice.Health != RunHealth.Passed;

        Notify(
            failed ? "A test run needs looking at" : "A test run finished",
            $"{notice.Test} - {notice.Health.ToString().ToLowerInvariant()}",
            failed ? TrayIcon.Level.Warning : TrayIcon.Level.Information);

        pendingRun = notice.SessionId;
    }

    /// <summary>
    /// The icon's own menu, which is the only way out while the window is hidden.
    /// </summary>
    /// <remarks>
    /// Not optional. A process with no window and no way to quit but Task Manager is the worst kind of
    /// tray application, and this one is deliberately allowed to outlive its window.
    /// </remarks>
    private void ShowMenu()
    {
        ContextMenu menu = new();

        MenuItem show = new() { Header = "Show the debugger" };
        show.Click += (_, _) => ShowRequested?.Invoke();

        MenuItem exit = new() { Header = "Stop watching and exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(show);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        // No placement target, so it opens at the cursor - which for a tray icon is where the icon is.
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }
}
