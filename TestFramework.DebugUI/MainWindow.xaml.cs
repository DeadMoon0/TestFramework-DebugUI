using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;
using static TestFramework.DebugUI.NativeMethods;

namespace TestFramework.DebugUI;

/// <summary>
/// The application window.
/// </summary>
/// <remarks>
/// The store is built here, before anything is loaded, and reached everywhere else as
/// <c>StateStore&lt;MainState&gt;.Default</c>. Views bind to it in their own constructors; there is
/// no DataContext and no view model, which is the convention the rest of these applications follow.
/// </remarks>
public partial class MainWindow : Window
{
    private static ShellController? shell;

    private readonly SettingsStore settings = new();
    private UiSettings saved = UiSettings.Defaults;
    private TrayIcon? tray;
    private WatchNotifier? notifier;
    private IDisposable? halts;

    /// <summary>
    /// Gets the controller the views drive.
    /// </summary>
    /// <remarks>
    /// Static for the same reason the store is: the window is a singleton, and threading it through
    /// every control's constructor buys nothing when there is only ever one.
    /// </remarks>
    public static ShellController Shell => shell ?? throw new InvalidOperationException("The window has not been created yet.");

    /// <summary>Creates the window and the store behind it.</summary>
    public MainWindow()
    {
        StateStore<MainState>.Create()
            .AddReducer(new MainReducer())
            .UseSynchronizationContext(SynchronizationContext.Current!)
            .BuildAndMakeDefault();

        shell = new ShellController(StateStore<MainState>.Default)
        {
            // Every step asks before it runs, so this is consulted constantly. It answers from the
            // breakpoints the user has set, and with none set nothing is ever held.
            PauseAtBreakpoint = Breakpoints.ShouldPause
        };

        // A crash must not take the window down. This tool exists to show things going wrong, and
        // the most likely moment for it to be handed something malformed is exactly when the run it
        // is watching is misbehaving.
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;

        InitializeComponent();

        // The summary is a foreground question, so whoever asks for it - the verdict on the board, the
        // title bar, a shortcut - asks, and the window decides where it goes. Neither of them has any
        // business knowing what else is on screen.
        ucBoard.SummaryRequested += ShowSummary;
        ucSummary.Closed += () => ucSummary.Visibility = Visibility.Collapsed;

        // The title bar acts on the run directly, but anything that moves the board is forwarded, because
        // the board owns its own zoom and selection.
        ucRunBar.SummaryRequested += ShowSummary;
        ucRunBar.FitRequested += ucBoard.FitToWindow;
        ucRunBar.FirstFailureRequested += ucBoard.GoToFirstFailure;

        ucValues.ValueOpened += (key, isArtifact) => ucValueInspector.Show(key, isArtifact);
        ucValueInspector.Closed += () => ucValueInspector.Visibility = Visibility.Collapsed;

        // The home page is shown on purpose and hidden on purpose. It is deliberately not tied to
        // whether a run is selected: the first live run selects itself, and having the page vanish
        // under the reader because a test started elsewhere would be the tool moving on its own.
        ucHome.Closed += () => ucHome.Visibility = Visibility.Collapsed;

        // A halt is the one exception. The step that stopped the run is marked on the board, the buttons
        // that answer it are in the title bar, and a run held up is waiting on the reader rather than
        // merely informing them — so this gets out of the way rather than leaving the mark behind a page.
        // Only on the transition into waiting, so closing the page and reopening it does not fight this.
        halts = StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Stages.Any(stage => stage.Steps.Any(step => step.IsWaitingAtBreakpoint)))
            .DistinctUntilChanged()
            .Subscribe(waiting =>
            {
                if (waiting)
                    ucHome.Visibility = Visibility.Collapsed;
            });

        // Read before the window is shown, so restoring geometry does not visibly move it.
        saved = settings.Load();
        Breakpoints.Restore(saved.Breakpoints);
        ApplyPlacement(saved.Window);

        // Saved on change rather than on exit. A tool that is killed - and this one is attached to
        // test hosts that get killed - would otherwise lose every breakpoint set in the session.
        Breakpoints.Changed += SaveBreakpoints;

        ucSettings.Closed += () => ucSettings.Visibility = Visibility.Collapsed;

        // The panel reports what the user changed; the window owns the file. Watch mode is applied
        // through the same path the title-bar eye uses, so the two can never disagree.
        ucSettings.Changed += watch =>
        {
            Persist(saved with { Watch = watch });

            if (notifier is not null)
                notifier.Settings = watch;

            ApplyWatchMode(watch.Enabled, announce: false);
        };

        BindShortcuts();

        btHome.ToolTip = Shortcuts.Describe("Every run, with what became of each", Shortcuts.Runs);
        btSettings.ToolTip = Shortcuts.Describe("Settings", Shortcuts.Settings);
        ApplyWatchMode(saved.Watch.Enabled, announce: false);

        shell.Start();
    }

    /// <summary>
    /// Puts the window back where it was, if that is still somewhere reachable.
    /// </summary>
    /// <remarks>
    /// Checked against the virtual screen rather than trusted: a window restored onto a monitor that
    /// has since been unplugged is a window nobody can find, and it reads as the application failing
    /// to start rather than as a misplaced window.
    /// </remarks>
    private void ApplyPlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
            return;

        if (!IsOnScreen(placement))
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;

        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private static bool IsOnScreen(WindowPlacement placement)
    {
        // Enough of the title bar has to land inside the virtual desktop to be grabbable.
        const double MinimumVisible = 120;

        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double right = left + SystemParameters.VirtualScreenWidth;
        double bottom = top + SystemParameters.VirtualScreenHeight;

        return placement.Left + MinimumVisible < right
            && placement.Left + placement.Width - MinimumVisible > left
            && placement.Top + 1 < bottom
            && placement.Top + placement.Height - 1 > top;
    }

    private void btSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ucSettings.Visibility == Visibility.Visible)
        {
            ucSettings.Visibility = Visibility.Collapsed;
            return;
        }

        ucSettings.Show(saved.Watch, settings.FilePath);
    }

    /// <summary>What each shortcut runs.</summary>
    private readonly Dictionary<RoutedUICommand, Action> shortcuts = [];

    /// <summary>
    /// Points every shortcut at the same method its button uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched on the window's key preview rather than through <c>InputBindings</c> and
    /// <c>CommandBindings</c>. That was tried first and silently did nothing: a routed command looks for
    /// its binding by walking up from whatever holds keyboard focus, and in this window that is often
    /// nothing at all — the board takes mouse focus for panning and the overlays are plain panels — so
    /// the walk never reached here. Previewing the key is not subject to any of that.
    /// </para>
    /// <para>
    /// The gestures still come from <see cref="Shortcuts"/>, so the keys matched here are the same ones
    /// the settings list and the tooltips display.
    /// </para>
    /// </remarks>
    private void BindShortcuts()
    {
        PreviewKeyDown += OnShortcutKey;

        Bind(Shortcuts.Runs, ShowHome);
        Bind(Shortcuts.Settings, () => btSettings_Click(this, new RoutedEventArgs()));
        Bind(Shortcuts.ToggleWatch, () => ApplyWatchMode(!saved.Watch.Enabled, announce: true));
        Bind(Shortcuts.CloseTopmost, CloseTopmost);

        Bind(Shortcuts.Rerun, () => _ = Shell.RerunSelectedAsync());
        Bind(Shortcuts.Stop, () => _ = Shell.CancelSelectedRunAsync());
        Bind(Shortcuts.Continue, () => _ = Shell.ContinueSelectedRunAsync());
        Bind(Shortcuts.StepForward, () => _ = Controls.Shell.UC_RunBar.StepAsync());
        Bind(Shortcuts.Refresh, Shell.RefreshRecordedRuns);

        Bind(Shortcuts.Fit, ucBoard.FitToWindow);
        Bind(Shortcuts.Summary, ucBoard.RequestSummary);
        Bind(Shortcuts.FirstFailure, ucBoard.GoToFirstFailure);

    }

    private void Bind(RoutedUICommand command, Action run) => shortcuts[command] = run;

    /// <summary>
    /// Runs whichever shortcut the key matches.
    /// </summary>
    /// <remarks>
    /// Typing is left alone: a gesture with no modifier — Escape, F5, F8 — would otherwise fire while
    /// someone was typing into a field. There is no text entry in this window today, and this is what
    /// stops the first one that appears from being broken by these shortcuts.
    /// </remarks>
    private void OnShortcutKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase { IsReadOnly: false })
            return;

        if (Shortcuts.Match(e.Key, Keyboard.Modifiers) is not { } command)
            return;

        if (!shortcuts.TryGetValue(command, out Action? run))
            return;

        run();
        e.Handled = true;
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
        if (ucSettings.Visibility == Visibility.Visible)
        {
            ucSettings.Visibility = Visibility.Collapsed;
            return;
        }

        if (ucValueInspector.Visibility == Visibility.Visible)
        {
            ucValueInspector.Visibility = Visibility.Collapsed;
            return;
        }

        if (ucSummary.Visibility == Visibility.Visible)
        {
            ucSummary.Visibility = Visibility.Collapsed;
            return;
        }

        if (ucHome.Visibility == Visibility.Visible)
            ucHome.Visibility = Visibility.Collapsed;
    }

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
        if (enabled && tray is null)
        {
            if (!TryStartWatching())
                enabled = false;
        }
        else if (!enabled)
        {
            StopWatching();
        }

        Persist(saved with { Watch = saved.Watch with { Enabled = enabled } });

        if (notifier is not null)
            notifier.Settings = saved.Watch;

        ShowWatchState(enabled);

        if (ucSettings.Visibility == Visibility.Visible)
            ucSettings.Show(saved.Watch, settings.FilePath);

        if (announce && enabled)
        {
            tray?.Notify(
                "Watching for test runs",
                "Close the window and it waits here. Runs that finish will be reported.",
                TrayIcon.Level.Information);
        }
    }

    /// <summary>Makes the title bar read as armed or not.</summary>
    /// <remarks>
    /// Minimise goes away while the mode is armed. There are two ways to put the window down and the tray
    /// is one of them, so leaving both would mean two buttons a pixel apart that look alike and do
    /// different things - and the taskbar one is the one that stops the tool being able to speak up.
    /// </remarks>
    private void ShowWatchState(bool enabled)
    {
        Brush ink = (Brush)FindResource(enabled ? "StateRunning" : "TextSecondary");

        pathWatchOutline.Stroke = ink;
        ellipseWatchPupil.Fill = ink;

        btMinimize.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        btWatch.ToolTip = Shortcuts.Describe(
            enabled ? "Watching. Closing puts it in the notification area" : "Watch for test runs",
            Shortcuts.ToggleWatch);
    }

    private void StopWatching()
    {
        if (notifier is not null)
        {
            notifier.Finished -= OnRunFinished;
            notifier.Dispose();
            notifier = null;
        }

        if (tray is not null)
        {
            tray.Activated -= ShowFromTray;
            tray.ContextRequested -= ShowTrayMenu;
            tray.Dispose();
            tray = null;
        }
    }

    private bool TryStartWatching()
    {
        try
        {
            tray = new TrayIcon("Test Framework Debugger - watching for test runs");
            tray.Activated += ShowFromTray;
            tray.ContextRequested += ShowTrayMenu;
        }
        catch (Exception e)
        {
            // No tray icon is a smaller problem than no window. Watch mode degrades to "minimises
            // like anything else".
            Debug.WriteLine(e);
            shell?.Report(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Warning,
                Source = FeedSource.App,
                Title = "The notification-area icon could not be created.",
                Detail = e.Message
            });

            return false;
        }

        notifier = new WatchNotifier { Settings = saved.Watch };
        notifier.Finished += OnRunFinished;

        return true;
    }

    private void OnRunFinished(RunFinishedNotice notice)
    {
        bool failed = notice.Health != RunHealth.Passed;

        tray?.Notify(
            failed ? "A test run needs looking at" : "A test run finished",
            $"{notice.Test} - {notice.Health.ToString().ToLowerInvariant()}",
            failed ? TrayIcon.Level.Warning : TrayIcon.Level.Information);

        // Held so that clicking the notification, or the icon, opens the run it was about rather than
        // whatever happened to be selected before the window was hidden.
        pendingRun = notice.SessionId;
    }

    private string? pendingRun;

    private void ShowFromTray()
    {
        Show();
        WindowState = saved.Window?.IsMaximized == true ? WindowState.Maximized : WindowState.Normal;
        Activate();

        if (pendingRun is { } sessionId)
        {
            pendingRun = null;
            shell?.SelectRun(sessionId);
        }

        if (notifier is not null)
            notifier.IsHidden = false;
    }

    /// <summary>
    /// The icon's own menu, which is the only way out while the window is hidden.
    /// </summary>
    /// <remarks>
    /// Not optional. A process with no window and no way to quit but Task Manager is the worst kind of
    /// tray application, and this one is deliberately allowed to outlive its window.
    /// </remarks>
    private void ShowTrayMenu()
    {
        ContextMenu menu = new();

        MenuItem show = new() { Header = "Show the debugger" };
        show.Click += (_, _) => ShowFromTray();

        MenuItem exit = new() { Header = "Stop watching and exit" };
        exit.Click += (_, _) => ExitFromTray();

        menu.Items.Add(show);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        // No placement target, so it opens at the cursor - which for a tray icon is where the icon is.
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void HideToTray()
    {
        Hide();

        if (notifier is not null)
            notifier.IsHidden = true;
    }

    private void SaveBreakpoints() => Persist(saved with { Breakpoints = Breakpoints.Snapshot() });

    private void Persist(UiSettings next)
    {
        saved = next;
        settings.Save(saved);
    }

    /// <summary>Where the window is now, for putting it back next time.</summary>
    private WindowPlacement CurrentPlacement() => new()
    {
        // RestoreBounds rather than Left/Top/Width/Height: while maximised those report the maximised
        // frame, so saving them would lose the size the window had before it was maximised.
        Left = RestoreBounds.Left,
        Top = RestoreBounds.Top,
        Width = RestoreBounds.Width,
        Height = RestoreBounds.Height,
        IsMaximized = WindowState == WindowState.Maximized
    };

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        shell?.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = FeedSeverity.Error,
            Source = FeedSource.App,
            Title = "Something in the window failed.",
            Detail = e.Exception.Message
        });
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        IntPtr mWindowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(mWindowHandle).AddHook(new HwndSourceHook(WindowProc));

        DWMWINDOWATTRIBUTE attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
        DWM_WINDOW_CORNER_PREFERENCE preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));

        ArcylicManager.EnableBlur(mWindowHandle, System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x0024:
                WmGetMinMaxInfo(hwnd, lParam);
                break;
        }

        return IntPtr.Zero;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        IntPtr mWindowHandle = new WindowInteropHelper(this).Handle;

        DWMWINDOWATTRIBUTE attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
        DWM_WINDOW_CORNER_PREFERENCE preference = WindowState == WindowState.Normal
            ? DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND
            : DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;

        DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        GetCursorPos(out POINT lMousePosition);

        IntPtr lPrimaryScreen = MonitorFromPoint(new POINT(0, 0), MonitorOptions.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO lPrimaryScreenInfo = new();
        if (GetMonitorInfo(lPrimaryScreen, lPrimaryScreenInfo) == false)
            return;

        IntPtr lCurrentScreen = MonitorFromPoint(lMousePosition, MonitorOptions.MONITOR_DEFAULTTONEAREST);

        MINMAXINFO lMmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

        if (lPrimaryScreen.Equals(lCurrentScreen))
        {
            lMmi.ptMaxPosition.X = lPrimaryScreenInfo.rcWork.Left;
            lMmi.ptMaxPosition.Y = lPrimaryScreenInfo.rcWork.Top;
            lMmi.ptMaxSize.X = lPrimaryScreenInfo.rcWork.Right - lPrimaryScreenInfo.rcWork.Left;
            lMmi.ptMaxSize.Y = lPrimaryScreenInfo.rcWork.Bottom - lPrimaryScreenInfo.rcWork.Top;
        }
        else
        {
            lMmi.ptMaxPosition.X = lPrimaryScreenInfo.rcMonitor.Left;
            lMmi.ptMaxPosition.Y = lPrimaryScreenInfo.rcMonitor.Top;
            lMmi.ptMaxSize.X = lPrimaryScreenInfo.rcMonitor.Right - lPrimaryScreenInfo.rcMonitor.Left;
            lMmi.ptMaxSize.Y = lPrimaryScreenInfo.rcMonitor.Bottom - lPrimaryScreenInfo.rcMonitor.Top;
        }

        Marshal.StructureToPtr(lMmi, lParam, true);
    }

    private void ShowSummary() => ucSummary.Visibility = Visibility.Visible;

    private void ShowHome() => ucHome.Visibility = Visibility.Visible;

    private void btHome_Click(object sender, RoutedEventArgs e) => ShowHome();

    private void btClose_Click(object sender, RoutedEventArgs e) => Close();

    private void btMaximizer_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;

    /// <summary>Minimises. Only reachable when watch mode is off, which is when the button exists.</summary>
    private void btMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

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
        if (!exiting && tray is not null && saved.Watch.Enabled)
        {
            e.Cancel = true;

            // Saved before hiding: from here the process may be killed with the window never shown again.
            Persist(saved with { Window = CurrentPlacement(), Breakpoints = Breakpoints.Snapshot() });
            HideToTray();

            return;
        }

        Persist(saved with { Window = CurrentPlacement(), Breakpoints = Breakpoints.Snapshot() });

        Breakpoints.Changed -= SaveBreakpoints;
        Application.Current.DispatcherUnhandledException -= OnDispatcherUnhandledException;

        // Before the store goes, since it is what is being observed.
        halts?.Dispose();
        halts = null;

        StopWatching();

        shell?.Dispose();
        shell = null;

        StateStore<MainState>.Default?.Dispose();

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
