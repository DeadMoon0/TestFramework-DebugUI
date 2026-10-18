using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Axiom.State;
using TestFramework.DebugUI.Controls.Board;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Controls.Feed;
using TestFramework.DebugUI.Controls.Search;
using TestFramework.DebugUI.Controls.Settings;
using TestFramework.DebugUI.Controls.Share;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Transport;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI;

/// <summary>
/// The application window.
/// </summary>
/// <remarks>
/// <para>
/// The store is built here, before any view, and it is this window's own: nothing publishes it as a
/// process-wide default. Every view is handed it through its constructor, together with what it may
/// ask the shell to do, as one <see cref="ShellContext"/> - so a view cannot exist without the store it
/// shows, and a test builds the same view against a store of its own. Views bind to it in their own
/// constructors; there is no DataContext and no view model, which is the convention the rest of these
/// applications follow.
/// </para>
/// <para>
/// That is also why the views that show the store are built in code rather than declared in the
/// markup. WPF builds a declared control with no arguments, so the markup holds a named slot for each
/// of them, carrying its place in the layout, and the constructor puts the view in it.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>The state behind everything on screen, built by this window and handed to every view.</summary>
    private readonly StateStore<MainState> store;

    private readonly ShellController shell;

    /// <summary>The run drawn as a board, in <c>bBoardSlot</c>.</summary>
    private readonly UC_Board ucBoard;

    /// <summary>Every arrangeable panel, in <c>bDockSlot</c>.</summary>
    private readonly UC_DockHost ucDock;

    /// <summary>The run and what acts on it, in the title bar's <c>bRunBarSlot</c>.</summary>
    private readonly Controls.Shell.UC_RunBar ucRunBar;

    /// <summary>What the runs off screen have reported, in <c>bFeedSlot</c>.</summary>
    private readonly UC_Feed ucFeed;

    /// <summary>The find bar over the run, in <c>bSearchSlot</c>.</summary>
    private readonly UC_Search ucSearch;

    /// <summary>What travels with a shared run, in <c>bExportSlot</c>.</summary>
    private readonly UC_Export ucExport;

    /// <summary>The tool's own settings and the machine switches, in <c>bSettingsSlot</c>.</summary>
    private readonly UC_Settings ucSettings;

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
    /// Keeps the subscription to Windows' "am I drawing effects" flag alive.
    /// </summary>
    /// <remarks>
    /// Never read. It is held only so the object the event lives on is not collected, which would end
    /// the subscription silently and leave the picker showing a stale answer.
    /// </remarks>
    private readonly object? blurWatch;

    /// <summary>
    /// The steps the user has asked to stop at.
    /// </summary>
    /// <remarks>
    /// Owned here and handed to the surfaces that need it, rather than reached for. It follows the
    /// store's selected run by itself, so a mark is filed against the test in view whether or not any
    /// particular control happens to exist.
    /// </remarks>
    private readonly BreakpointService breakpoints;

    /// <summary>Whether this machine records test runs; null when its journal root cannot be told.</summary>
    private readonly RecordingSwitch? recording = RecordingSwitch.ForThisMachine();

    /// <summary>What is unread, and the worst of it — which is what the bell shows without being opened.</summary>
    private readonly record struct Unread(int Count, FeedSeverity Worst);

    private IDisposable? notifications;
    private Unread unread;
    private UiSettings saved = UiSettings.Defaults;
    /// <summary>The notification-area icon and the notices, which outlive the window while watching.</summary>
    private readonly WatchModeController watch;
    private IDisposable? halts;

    /// <summary>Brings the step panel out whenever a step is picked, wherever it was picked from.</summary>
    private IDisposable? picks;

    /// <summary>What fraction of the window the pinned wells have reserved.</summary>
    private DockInsets reserved = DockInsets.None;

    /// <summary>The row of panel icons in the title bar, which keeps itself in step with the arrangement.</summary>
    private readonly PanelStrip panelStrip;

    /// <summary>Where the panels are: this window's, shared with its dock, its floating windows and its panel strip.</summary>
    private readonly DockArrangement arrangement = new();

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
    /// <param name="palette">
    /// The application's brushes, made movable. Asked for rather than reached for, because this
    /// window's markup resolves them while it is built, and a window built before they were prepared
    /// would never follow a theme change.
    /// </param>
    internal MainWindow(PreparedTheme palette)
    {
        store = CreateStore();

        breakpoints = new BreakpointService(store);
        watch = new WatchModeController(store);

        shell = CreateShell();

        // A crash must not take the window down. This tool exists to show things going wrong, and
        // the most likely moment for it to be handed something malformed is exactly when the run it
        // is watching is misbehaving.
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;

        InitializeComponent();

        // One context for every view, so they all show the same store and ask the same shell. Each view is
        // built with it and put into the slot the markup keeps for it; the dock host builds its panels with
        // it in its own constructor, so there is no moment at which a panel can be reached for and not exist.
        ShellContext context = new(store, shell);

        ucRunBar = new Controls.Shell.UC_RunBar(context);
        bRunBarSlot.Child = ucRunBar;

        bTransportSlot.Child = new Controls.Shell.UC_TransportStatus(context);

        ucBoard = new UC_Board(context, breakpoints);
        bBoardSlot.Child = ucBoard;

        ucDock = new UC_DockHost(context, arrangement);
        bDockSlot.Child = ucDock;

        // Built closed, as the markup used to declare them: each of these is brought out on request.
        ucFeed = new UC_Feed(context) { Visibility = Visibility.Collapsed };
        bFeedSlot.Child = ucFeed;

        ucSearch = new UC_Search(context) { Visibility = Visibility.Collapsed };
        bSearchSlot.Child = ucSearch;

        ucExport = new UC_Export(context) { Visibility = Visibility.Collapsed };
        bExportSlot.Child = ucExport;

        ucSettings = new UC_Settings(breakpoints, recording) { Visibility = Visibility.Collapsed };
        bSettingsSlot.Child = ucSettings;

        WireBoard();
        WireRunBar();
        WireAnnotation();
        WirePanels();
        FollowSelection();

        // Read before the window is shown, so restoring geometry does not visibly move it.
        saved = settings.Load();

        // Applied here, in the constructor, so the first frame the window paints is already in the
        // right theme. Without the fade, because there is no previous colour to fade from and a window
        // that cross-fades into existence reads as a window that has not finished loading.
        theme = new ThemeService(palette, new ThemeStore(ReportTheme), ReportTheme);
        WireTheme();
        RestoreSaved();
        RestoreLayout();
        WireBoardInsets();

        panelStrip = new PanelStrip(arrangement, spPanels);

        WireTray();
        WirePanelStrip();
        WireSettings();
        WireSearch();
        WireFeed();
        WireSettingsChanges();

        // Held for as long as the window is, because the subscription lives on this object and a
        // collected one stops calling back. Windows raises it from its own thread.
        blurWatch = BlurSupport.WhenChanged(() => Dispatcher.BeginInvoke(RefreshThemes));

        WireBreakOnFailure();
        BindShortcuts();
        PrepareTitleBar();

        shell.Start();
    }

    /// <summary>Builds the store this window owns, with the collaborators its effects report through.</summary>
    private StateStore<MainState> CreateStore()
    {
        // The reducers are listed by MainStore, so this window and the tests build the same store.
        // The effects are given their collaborators rather than reaching for them: the comparison
        // needs to read earlier runs off disk, and the re-run needs to build and start a test host.
        // Both are constructed before the store because the store is what they report into.
        //
        // The re-run reports into the store it is built before, so it reads the field when it reports - which is
        // only ever after a re-run has been asked for, long after this constructor has finished. The compiler
        // cannot see that from here, hence the null check it will never need.
        string? runsDirectory = ShellController.DefaultRunsDirectory();
        TestRerunner rerunner = new(entry => store?.Dispatch(FeedActions.AppendEntry, entry));

        // This context is the whole of the window's thread marshalling. Runs arrive on the pipe reader
        // and the ingest timer; the store reduces each dispatch on whichever thread made it, then posts
        // every binding's notification here, so the controls - Axiom.Wpf's Bind* helpers included,
        // which set properties directly - only ever see the UI thread. RunIngestServiceTests pins it.
        return MainStore.Create(new BaselineResolver(runsDirectory), rerunner)
            .UseSynchronizationContext(SynchronizationContext.Current!)
            .Build();
    }

    /// <summary>Builds the controller, answering its questions from the breakpoints.</summary>
    private ShellController CreateShell()
    {
        return new ShellController(store)
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
    }

    /// <summary>Hands the board's requests to the window.</summary>
    private void WireBoard()
    {
        // The summary is a foreground question, so whoever asks for it - the verdict on the board, the
        // title bar, a shortcut - asks, and the window decides where it goes. Neither of them has any
        // business knowing what else is on screen.
        ucBoard.SummaryRequested += ShowSummary;
        ucBoard.StepSelected += Shell.SelectStep;
    }

    /// <summary>Acts on what the title bar asks for, and on the sharing it and the home page start.</summary>
    private void WireRunBar()
    {
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
    }

    /// <summary>Hands what the drawing bar chooses to the board.</summary>
    private void WireAnnotation()
    {
        // The bar chooses; the board draws. Nothing about a stroke is decided in the window.
        ucAnnotate.ToolChosen += ucBoard.SetAnnotationTool;
        ucAnnotate.InkChosen += ucBoard.SetAnnotationInk;
        ucAnnotate.WeightChosen += ucBoard.SetAnnotationWeight;
        ucAnnotate.UndoRequested += ucBoard.UndoAnnotation;
        ucAnnotate.VisibilityChanged += ucBoard.SetAnnotationsVisible;
        ucAnnotate.Closed += StopAnnotating;
    }

    /// <summary>Takes what the docked panels ask to open or put away.</summary>
    private void WirePanels()
    {
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
    }

    /// <summary>Moves panels when the run halts or a step is picked.</summary>
    private void FollowSelection()
    {
        // A halt is the one exception. The step that stopped the run is marked on the board, the buttons
        // that answer it are in the title bar, and a run held up is waiting on the reader rather than
        // merely informing them — so this gets out of the way rather than leaving the mark behind a page.
        // Only on the transition into waiting, so closing the page and reopening it does not fight this.
        halts = store
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
        picks = store
            .Bind(BoardSelectors.SelectSelectedStep)
            .DistinctUntilChanged()
            .Subscribe(step =>
            {
                // Only a real pick. Selecting another run clears the step, and a panel appearing
                // because something was cleared would be the tool moving on its own.
                if (step is not null)
                    Reveal(PanelId.Step);
            });
    }

    /// <summary>Repaints the backdrop and the window's ground whenever the theme changes.</summary>
    private void WireTheme()
    {
        theme.Changed += definition =>
        {
            ucBackground.Show(definition.Backdrop, theme.BackdropInk);
            Ground(definition);
        };
    }

    /// <summary>Puts back the theme, the breakpoints and the window's geometry as they were saved.</summary>
    private void RestoreSaved()
    {
        theme.Use(saved.ThemeId, animate: false);
        breakpoints.Restore(saved.Breakpoints);
        breakpoints.BreakOnFailure = saved.BreakOnFailure;
        WindowChromeInterop.Restore(this, saved.Window);
    }

    /// <summary>Puts every panel back where it was saved, and saves the arrangement whenever it changes.</summary>
    private void RestoreLayout()
    {
        // Where every panel is, as the reader last left it. Restored before the window is shown so it does
        // not visibly rearrange itself, and repaired on the way in so a file from another build is survivable.
        arrangement.Restore(saved.Layout);
        arrangement.Changed += SaveLayout;
    }

    /// <summary>Keeps the board clear of the pinned wells as they and the window change size.</summary>
    private void WireBoardInsets()
    {
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
    }

    /// <summary>Does what the notification-area icon asks for.</summary>
    private void WireTray()
    {
        // The icon says what was asked for and the window does it: bringing the window back needs the
        // window, and so does leaving.
        watch.ShowRequested += ShowFromTray;
        watch.ExitRequested += ExitFromTray;
    }

    /// <summary>Keeps the title bar's panel icons in step with the arrangement, starting now.</summary>
    private void WirePanelStrip()
    {
        arrangement.Changed += panelStrip.Rebuild;
        panelStrip.Rebuild();
    }

    /// <summary>Saves the breakpoints as they change, and follows what the settings panel switches.</summary>
    private void WireSettings()
    {
        // Saved on change rather than on exit. A tool that is killed - and this one is attached to
        // test hosts that get killed - would otherwise lose every breakpoint set in the session.
        breakpoints.Changed += SaveBreakpoints;

        ucSettings.Closed += () => ucSettings.Visibility = Visibility.Collapsed;

        // The panel flips the marker; the state is what every other surface reads, so they change together.
        ucSettings.RecordingChanged += now => store.Dispatch(ShellActions.SetRecording, now);
        PublishRecording();
    }

    /// <summary>Opens what the find bar found.</summary>
    private void WireSearch()
    {
        // A value found by searching opens where a value opened from the rail does. The bar stays up, so a
        // reader can work through several hits without retyping the query.
        ucSearch.Opened += OpenValue;
        ucSearch.StepPicked += Shell.SelectStep;
    }

    /// <summary>Goes where a feed entry points, and keeps the bell showing what is unread.</summary>
    private void WireFeed()
    {
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

        notifications = store
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
    }

    /// <summary>Applies and saves what the reader changes in the settings panel.</summary>
    private void WireSettingsChanges()
    {
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
    }

    /// <summary>Arms or disarms stopping after a failure, and remembers the choice.</summary>
    private void WireBreakOnFailure()
    {
        // Held in the static so the reader thread can consult it without a dispatch, and in the file so a
        // person who works this way finds it still armed next time.
        ucSettings.BreakOnFailureChanged += value =>
        {
            breakpoints.BreakOnFailure = value;
            Persist(saved with { BreakOnFailure = value });
        };
    }

    /// <summary>Gives the title bar's buttons their tooltips, and puts watch mode back as it was saved.</summary>
    private void PrepareTitleBar()
    {
        btAnnotate.ToolTip = "Draw on this run";
        btSettings.ToolTip = Shortcuts.Describe("Settings", Shortcuts.Settings);
        ApplyWatchMode(saved.Watch.Enabled, announce: false);
    }
}
