using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Controls.Settings;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Bundles;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Transport;
using TestFramework.DebugUI.State.Diagnostics;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;

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
    private readonly ShellController shell;

    private readonly SettingsStore settings = new();

    /// <summary>
    /// Which theme the window is in.
    /// </summary>
    /// <remarks>
    /// Owned here rather than reached for, and there is exactly one: the brushes it moves are shared by
    /// the whole process, so a second service would be a second answer to a question with one answer.
    /// </remarks>
    private readonly ThemeService theme;

    /// <summary>
    /// The steps the user has asked to stop at.
    /// </summary>
    /// <remarks>
    /// Owned here and handed to the surfaces that need it, rather than reached for. It follows the
    /// store's selected run by itself, so a mark is filed against the test in view whether or not any
    /// particular control happens to exist.
    /// </remarks>
    private readonly BreakpointService breakpoints;

    /// <summary>What is unread, and the worst of it — which is what the bell shows without being opened.</summary>
    private readonly record struct Unread(int Count, FeedSeverity Worst);

    private IDisposable? notifications;
    private Unread unread;
    private UiSettings saved = UiSettings.Defaults;
    /// <summary>The notification-area icon and the notices, which outlive the window while watching.</summary>
    private readonly WatchModeController watch = new();
    private IDisposable? halts;

    /// <summary>Brings the step panel out whenever a step is picked, wherever it was picked from.</summary>
    private IDisposable? picks;

    /// <summary>What fraction of the window the pinned wells have reserved.</summary>
    private DockInsets reserved = DockInsets.None;

    /// <summary>The row of panel icons in the title bar, which keeps itself in step with the arrangement.</summary>
    private readonly PanelStrip panelStrip;

    /// <summary>
    /// Gets the controller this window drives.
    /// </summary>
    /// <remarks>
    /// The window's own, not the application's. It used to be a static every control reached for, which
    /// threw until this constructor had run — so each of twenty-six call sites depended on a window
    /// existing first, and no control could be built in a test at all. What the panels need is handed to
    /// them as <see cref="IShellCommands"/>; what the window's own chrome needs, it asks for here.
    /// </remarks>
    private ShellController Shell => shell;

    private Controls.Runs.UC_Runs Runs => ucDock.Get<Controls.Runs.UC_Runs>(PanelId.Runs);

    private Controls.Detail.UC_ValueRail Values => ucDock.Get<Controls.Detail.UC_ValueRail>(PanelId.Values);

    private Controls.Detail.UC_RunSummary Summary => ucDock.Get<Controls.Detail.UC_RunSummary>(PanelId.Summary);

    private Controls.Detail.UC_ValueInspector Inspector => ucDock.Get<Controls.Detail.UC_ValueInspector>(PanelId.Inspector);

    private Controls.Detail.UC_StepDetail StepPanel => ucDock.Get<Controls.Detail.UC_StepDetail>(PanelId.Step);

    private Controls.Home.UC_Home HomePage => ucDock.Get<Controls.Home.UC_Home>(PanelId.Home);

    /// <summary>Creates the window and the store behind it.</summary>
    public MainWindow()
    {
        // The reducers are listed by MainStore, so this window and the tests build the same store.
        // The effects are given their collaborators rather than reaching for them: the comparison
        // needs to read earlier runs off disk, and the re-run needs to build and start a test host.
        // Both are constructed before the store because the store is what they report into.
        string? runsDirectory = ShellController.DefaultRunsDirectory();
        TestRerunner rerunner = new(entry => StateStore<MainState>.Default.Dispatch(FeedActions.AppendEntry, entry));

        MainStore.Create(new BaselineResolver(runsDirectory), rerunner)
            .UseSynchronizationContext(SynchronizationContext.Current!)
            .BuildAndMakeDefault();

        breakpoints = new BreakpointService(StateStore<MainState>.Default);

        shell = new ShellController(StateStore<MainState>.Default)
        {
            // Every step asks before it runs, so this is consulted constantly. It answers from the
            // breakpoints the user has set, and with none set nothing is ever held.
            PauseAtBreakpoint = breakpoints.ShouldPause,

            // A run cannot be paused at the moment a step fails — a step is only ever asked before it
            // starts — so the failure arms a stop at whatever comes next. After a failure that is the
            // first step of the following stage, which is normally teardown: the run holds there with
            // everything the test built still standing, instead of tearing it down and finishing.
            StepEndedBadly = notice =>
            {
                if (breakpoints.BreakOnFailure)
                    breakpoints.ArmNextStep(notice.SessionId);
            }
        };

        // A crash must not take the window down. This tool exists to show things going wrong, and
        // the most likely moment for it to be handed something malformed is exactly when the run it
        // is watching is misbehaving.
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;

        InitializeComponent();

        // Before anything reaches for a panel: the dock host builds them, and each of them is built with
        // what it may ask of the shell.
        ucDock.UseCommands(shell);

        // The summary is a foreground question, so whoever asks for it - the verdict on the board, the
        // title bar, a shortcut - asks, and the window decides where it goes. Neither of them has any
        // business knowing what else is on screen.
        ucBoard.SummaryRequested += ShowSummary;
        ucBoard.StepSelected += Shell.SelectStep;
        ucBoard.UseBreakpoints(breakpoints);

        // The title bar acts on the run directly, but anything that moves the board is forwarded, because
        // the board owns its own zoom and selection.
        ucRunBar.SummaryRequested += ShowSummary;
        ucRunBar.ShareRequested += ShareSelectedRun;
        HomePage.ShareRequested += ShowExport;
        ucExport.Closed += () => ucExport.Visibility = Visibility.Collapsed;
        ucExport.Exported += ReportExport;
        ucRunBar.FitRequested += ucBoard.FitToWindow;
        ucRunBar.FirstFailureRequested += ucBoard.GoToFirstFailure;

        // What acts on the run is done here, where the controller and the breakpoints both live. The bar
        // says what was asked for; it holds neither.
        ucRunBar.ContinueRequested += () => _ = Shell.ContinueSelectedRunAsync();
        ucRunBar.StepRequested += () => _ = StepSelectedRunAsync();
        ucRunBar.StopRequested += () => _ = Shell.CancelSelectedRunAsync();

        // Awaited rather than dropped, unlike the three above it: the bar disables its own button
        // until the run answers, and it can only know when that is by being handed the task.
        ucRunBar.LookRequested += Shell.CaptureWidgetsForSelectedRunAsync;
        ucRunBar.RerunRequested += Shell.RerunSelected;

        // The bar chooses; the board draws. Nothing about a stroke is decided in the window.
        ucAnnotate.ToolChosen += ucBoard.SetAnnotationTool;
        ucAnnotate.InkChosen += ucBoard.SetAnnotationInk;
        ucAnnotate.WeightChosen += ucBoard.SetAnnotationWeight;
        ucAnnotate.UndoRequested += ucBoard.UndoAnnotation;
        ucAnnotate.VisibilityChanged += ucBoard.SetAnnotationsVisible;
        ucAnnotate.Closed += StopAnnotating;

        Values.ValueOpened += OpenValue;

        // A widget goes to the same panel a value does, which is what lets one screenshot be set
        // against the last passing run's without a second inspector to maintain.
        StepPanel.WidgetOpened += OpenWidget;

        // The home page is shown on purpose and hidden on purpose. It is deliberately not tied to
        // whether a run is selected: the first live run selects itself, and having the page vanish
        // under the reader because a test started elsewhere would be the tool moving on its own.
        HomePage.Closed += () => Put(PanelId.Home);

        // The rail answers "which run"; the page answers "how is everything doing". Its root row is the way
        // across, because the whole journal is what the page is about.
        Runs.OverviewRequested += ShowHome;

        // A halt is the one exception. The step that stopped the run is marked on the board, the buttons
        // that answer it are in the title bar, and a run held up is waiting on the reader rather than
        // merely informing them — so this gets out of the way rather than leaving the mark behind a page.
        // Only on the transition into waiting, so closing the page and reopening it does not fight this.
        halts = StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectIsWaitingAtBreakpoint)
            .DistinctUntilChanged()
            .Subscribe(waiting =>
            {
                if (waiting)
                    Put(PanelId.Home);
            });

        // Clicking something is asking to see it, so the panel that shows it comes out: opened if it
        // was away, brought to the front of its well if it was behind another. Kept here, on the
        // selection itself, rather than at the four places that make one - the board, a search hit, a
        // failure in the summary, an entry in the feed. A rule kept at four call sites is a rule that
        // holds at three of them the day a fifth is added, and until now it held at none: picking a
        // step with the panel closed changed nothing a reader could see.
        picks = StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectSelectedStep)
            .DistinctUntilChanged()
            .Subscribe(step =>
            {
                // Only a real pick. Selecting another run clears the step, and a panel appearing
                // because something was cleared would be the tool moving on its own.
                if (step is not null)
                    Reveal(PanelId.Step);
            });

        // Read before the window is shown, so restoring geometry does not visibly move it.
        saved = settings.Load();

        // Applied here, in the constructor, so the first frame the window paints is already in the
        // right theme. Without the fade, because there is no previous colour to fade from and a window
        // that cross-fades into existence reads as a window that has not finished loading.
        theme = new ThemeService(Application.Current.Resources, new ThemeStore(ReportTheme), ReportTheme);
        theme.Changed += definition =>
        {
            ucBackground.Show(definition.Backdrop, theme.BackdropInk);

            // The ground the whole window lies on. Not a brush anything resolves, so nothing follows it for
            // free — and this is the only place it is set, which is what a see-through theme depends on: the
            // window is layered, so how much of the desktop shows is exactly this colour's alpha.
            bTint.Background = new SolidColorBrush(theme.WindowTint);
        };
        theme.Use(saved.ThemeId, animate: false);
        breakpoints.Restore(saved.Breakpoints);
        breakpoints.BreakOnFailure = saved.BreakOnFailure;
        WindowChromeInterop.Restore(this, saved.Window);

        // Where every panel is, as the reader last left it. Restored before the window is shown so it does
        // not visibly rearrange itself, and repaired on the way in so a file from another build is survivable.
        Arrangement.Restore(saved.Layout);
        Arrangement.Changed += SaveLayout;

        // A pinned well reserves its room, and the board is the one thing that has to be told: its own
        // fit-to-window measures the area it actually has, not the area the window has. Given as fractions and
        // turned into a margin here, because the host must not read a size — doing that inside its own layout is
        // what made an early version inflate itself past the window.
        ucDock.InsetsChanged += insets =>
        {
            reserved = insets;
            ApplyBoardInsets();
        };

        SizeChanged += (_, _) => ApplyBoardInsets();

        panelStrip = new PanelStrip(spPanels);

        // The icon says what was asked for and the window does it: bringing the window back needs the
        // window, and so does leaving.
        watch.ShowRequested += ShowFromTray;
        watch.ExitRequested += ExitFromTray;

        Arrangement.Changed += panelStrip.Rebuild;
        panelStrip.Rebuild();

        // Saved on change rather than on exit. A tool that is killed - and this one is attached to
        // test hosts that get killed - would otherwise lose every breakpoint set in the session.
        breakpoints.Changed += SaveBreakpoints;

        ucSettings.Closed += () => ucSettings.Visibility = Visibility.Collapsed;
        ucSettings.UseBreakpoints(breakpoints);

        // A value found by searching opens where a value opened from the rail does. The bar stays up, so a
        // reader can work through several hits without retyping the query.
        ucSearch.Opened += OpenValue;
        ucSearch.StepPicked += Shell.SelectStep;

        // The bell reports what is unread and how bad it is; the panel it opens is only the list.
        ucFeed.Closed += () => ShowNotificationState();

        // An entry names where it happened; going there is the window's to do.
        ucFeed.EntryPicked += entry =>
        {
            if (entry.SessionId is null)
                return;

            Shell.SelectRun(entry.SessionId);

            if (entry.Stage is not null && entry.StepId is not null)
                Shell.SelectStep(entry.Stage, entry.StepId.Value);
        };

        notifications = StateStore<MainState>.Default
            .Bind(FeedSelectors.SelectFeed)
            .Select(feed => new Unread(
                feed.UnreadCount,
                FeedSelectors.UnreadOf(feed)
                    .Select(entry => entry.Severity)
                    .DefaultIfEmpty(FeedSeverity.Info)
                    .Max()))
            .DistinctUntilChanged()
            .Subscribe(unread =>
            {
                this.unread = unread;
                ShowNotificationState();
            });

        // The panel reports what the user changed; the window owns the file. Watch mode is applied
        // through the same path the title-bar eye uses, so the two can never disagree.
        ucSettings.Changed += watch =>
        {
            Persist(saved with { Watch = watch });

            this.watch.Settings = watch;

            ApplyWatchMode(watch.Enabled, announce: false);
        };

        // The id is what is remembered, never the colours: a theme that is corrected in a later build
        // should arrive for the people who already chose it, not only for new ones.
        ucSettings.ThemeChosen += id =>
        {
            Persist(saved with { ThemeId = theme.Use(id, animate: true).Id });
            ShowSettings();
        };

        ucSettings.ThemesReloadRequested += () =>
        {
            theme.Reload();
            ShowSettings();
        };

        ucSettings.ThemesFolderRequested += OpenThemesFolder;

        // Held in the static so the reader thread can consult it without a dispatch, and in the file so a
        // person who works this way finds it still armed next time.
        ucSettings.BreakOnFailureChanged += value =>
        {
            breakpoints.BreakOnFailure = value;
            Persist(saved with { BreakOnFailure = value });
        };

        BindShortcuts();

        btAnnotate.ToolTip = "Draw on this run";
        btSettings.ToolTip = Shortcuts.Describe("Settings", Shortcuts.Settings);
        ApplyWatchMode(saved.Watch.Enabled, announce: false);

        shell.Start();
    }

    private void btSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ucSettings.Visibility == Visibility.Visible)
        {
            ucSettings.Visibility = Visibility.Collapsed;
            return;
        }

        ShowSettings();
    }

    /// <summary>Fills the settings panel in and brings it out.</summary>
    private void ShowSettings()
        => ucSettings.Show(
            saved.Watch,
            settings.FilePath,
            saved.BreakOnFailure,
            theme.Available,
            theme.Current.Id,
            theme.Store.DirectoryPath);

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
        string? sessionId = StateStore<MainState>.Default.GetValue(state => state.Runs.SelectedSessionId);

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

        if (Arrangement.Current.IsOpen(PanelId.Inspector))
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

        if (Arrangement.Current.IsOpen(PanelId.Summary))
        {
            Put(PanelId.Summary);
            return;
        }

        // Last, and only what is in the centre: a page is the thing in front, and the rails down the sides are
        // not something the reader opened, so there is nothing there for Escape to put away.
        if (Arrangement.Current.At(DockSide.Center).Active is { } page)
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

    private void SaveBreakpoints() => Persist(saved with { Breakpoints = breakpoints.Snapshot() });

    private void Persist(UiSettings next)
    {
        saved = next;
        settings.Save(saved);
    }

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
    /// Hooks the window once it has a handle.
    /// </summary>
    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        WindowChromeInterop.Attach(this);

        WindowChromeInterop.Ground(this);

    }

    /// <summary>Keeps the window's corners in step with its state.</summary>
    private void Window_StateChanged(object sender, EventArgs e) => WindowChromeInterop.FollowState(this);

    public void OpenFromAnotherLaunch(string? bundlePath)
    {
        Surface();

        if (bundlePath is null)
            return;

        if (BundleImport.Open(bundlePath, Shell))
            Put(PanelId.Home);
    }

    /// <summary>
    /// Brings the window out of wherever it was put.
    /// </summary>
    /// <remarks>
    /// The topmost nudge is deliberate. A process cannot take the foreground from another one, so raising a
    /// window on request means briefly asking to be above everything and then giving that up again - which is
    /// what every application that can be summoned by a second launch ends up doing.
    /// </remarks>
    private void Surface()
    {
        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = saved.Window?.IsMaximized == true ? WindowState.Maximized : WindowState.Normal;

        Activate();

        Topmost = true;
        Topmost = false;

        watch.MarkShown();
    }

    /// <summary>
    /// Opens or closes the drawing instruments.
    /// </summary>
    /// <remarks>
    /// Closing hands the mouse back to the board explicitly rather than leaving a tool armed behind a hidden bar,
    /// which would leave the next click drawing a line nobody asked for.
    /// </remarks>
    private void btAnnotate_Click(object sender, RoutedEventArgs e)
    {
        if (ucAnnotate.Visibility == Visibility.Visible)
        {
            StopAnnotating();
            return;
        }

        ucAnnotate.Open();

        // The one thing a reader cannot see for themselves: these marks were drawn on a board that has since
        // been arranged differently, so they may no longer point where they were aimed.
        ucAnnotate.Note(ucBoard.AnnotationsPredateThisLayout()
            ? "These marks were drawn on an earlier layout"
            : null);

        ShowAnnotateState(annotating: true);
    }

    private void StopAnnotating()
    {
        ucBoard.SetAnnotationTool(null);
        ucAnnotate.Visibility = Visibility.Collapsed;

        ShowAnnotateState(annotating: false);
    }

    /// <summary>
    /// Lights the pen while the toolbar is open.
    /// </summary>
    /// <remarks>
    /// The accent rather than the ink being drawn with. It says the tool is armed, which is the same thing
    /// the eye beside it says about watching - and lighting one cyan and the other blue made two toggles a
    /// pixel apart look like two different kinds of switch. Which colour the pen draws in is the toolbar's
    /// business, and it is shown there.
    /// </remarks>
    private void ShowAnnotateState(bool annotating)
        => pathAnnotate.Stroke = (Brush)FindResource(annotating ? ThemeKeys.Accent : ThemeKeys.TextSecondary);

    private void ShowSummary() => Reveal(PanelId.Summary);

    /// <summary>Offers to share the run being watched.</summary>
    private void ShareSelectedRun()
    {
        RunSummary? run = StateStore<MainState>.Default.GetValue(RunsSelectors.SelectedRunOf);

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

    private void ShowHome() => Reveal(PanelId.Home);

    /// <summary>Opens a value in the inspector, wherever the reader has put the inspector.</summary>
    /// <remarks>
    /// Two steps because they are two things: which value is the panel's own business, and whether the panel is
    /// on screen is the arrangement's. Asked for from the value rail and from a search hit alike.
    /// </remarks>
    private void OpenValue(string key, bool isArtifact)
    {
        Inspector.Show(key, isArtifact);
        Reveal(PanelId.Inspector);
    }

    /// <summary>Opens a widget in the inspector, where it can be compared with an earlier run's.</summary>
    private void OpenWidget(WidgetNode widget)
    {
        Inspector.ShowWidget(widget);
        Reveal(PanelId.Inspector);
    }

    /// <summary>Brings a panel out, at its own default place if it was put away.</summary>
    private static void Reveal(PanelId panel)
        => Arrangement.Apply(layout => layout.Reveal(panel, PanelRegistry.DefaultSideOf(panel)));

    /// <summary>Puts a panel away.</summary>
    private static void Put(PanelId panel) => Arrangement.Apply(layout => layout.Close(panel));

    private void SaveLayout() => Persist(saved with { Layout = Arrangement.Current });

    /// <summary>Keeps the board out of whatever the pinned wells have reserved.</summary>
    private void ApplyBoardInsets()
        => ucBoard.Margin = reserved.Against(ucDock.ActualWidth, ucDock.ActualHeight);

    private void btNotifications_Click(object sender, RoutedEventArgs e)
    {
        ucFeed.Toggle();
        ShowNotificationState();
    }

    /// <summary>
    /// Puts the unread count on the bell, in the colour of the worst thing in it.
    /// </summary>
    /// <remarks>
    /// The colour is a lifecycle colour rather than the accent: red and amber here mean the same as they mean on
    /// a step, which is the whole reason one glance at the title bar is enough. A bell with nothing unread is as
    /// quiet as the rest of the caption.
    /// </remarks>
    private void ShowNotificationState()
    {
        bUnread.Visibility = unread.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        tbUnread.Text = unread.Count > 9 ? "9+" : unread.Count.ToString(CultureInfo.InvariantCulture);

        bUnread.Background = (Brush)FindResource(unread.Worst switch
        {
            FeedSeverity.Error => ThemeKeys.StateError,
            FeedSeverity.Warning => ThemeKeys.StateTimeout,
            _ => ThemeKeys.Accent
        });

        pathBell.Stroke = (Brush)FindResource(
            ucFeed.IsOpen ? ThemeKeys.Accent
            : unread.Count == 0 ? ThemeKeys.TextSecondary
            : unread.Worst switch
            {
                FeedSeverity.Error => ThemeKeys.StateError,
                FeedSeverity.Warning => ThemeKeys.StateTimeout,
                _ => ThemeKeys.TextSecondary
            });

        btNotifications.ToolTip = unread.Count == 0
            ? "What runs off screen have reported"
            : $"{unread.Count} unread";
    }

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
