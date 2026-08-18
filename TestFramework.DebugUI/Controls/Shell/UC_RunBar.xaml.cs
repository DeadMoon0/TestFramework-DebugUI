using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.Editors;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Shell;

/// <summary>
/// The run being watched, and the actions that apply to it, in the title bar.
/// </summary>
/// <remarks>
/// <para>
/// A control of its own rather than markup in the window, because these buttons are the only things in
/// the chrome with state: three of them are enabled or shown by what the run is doing, and that wiring
/// belongs next to them.
/// </para>
/// <para>
/// The three that change the run call the controller directly, as the plate on the board did. The three
/// that change the view are raised as events, because what they act on is the board, and reaching across
/// to it from here would make two controls responsible for one canvas.
/// </para>
/// </remarks>
public partial class UC_RunBar : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    private ExternalEditor? code;
    private ExternalEditor? visualStudio;

    /// <summary>Creates the bar and binds it.</summary>
    public UC_RunBar()
    {
        InitializeComponent();

        // Left in the casing the test author wrote: a test name is PascalCase and nothing else, so
        // upper-casing it removes the only thing separating its words.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => Selected(state)?.Name ?? string.Empty)
            .BindToDependencyProperty(tbRunName, TextBlock.TextProperty));

        // Capped and trimmed, so a long name cannot push the actions off the bar. The qualified name is on
        // the tooltip for when the trimmed one is not enough — and it is what the field copies, because a
        // method name on its own is not what anyone pastes into a filter or a search.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => Selected(state)?.Test ?? string.Empty)
            .Subscribe(test =>
            {
                tbRunName.ToolTip = string.IsNullOrWhiteSpace(test) ? null : test;
                Copyable.SetText(tbRunName, test);
            }));

        Copyable.Enable(tbRunName);

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.SelectedSessionId is null ? Visibility.Collapsed : Visibility.Visible)
            .BindToDependencyProperty(this, VisibilityProperty));

        // Shown only when there is something to release. A button that is always there and almost always
        // does nothing teaches people not to look at it.
        IObservable<Visibility> held = StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Stages.Any(stage => stage.Steps.Any(step => step.IsWaitingAtBreakpoint)))
            .Select(waiting => waiting ? Visibility.Visible : Visibility.Collapsed);

        subscriptions.Add(held.BindToDependencyProperty(btContinue, VisibilityProperty));
        subscriptions.Add(held.BindToDependencyProperty(btStep, VisibilityProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.SelectedSessionId is not null && !state.ActiveRun.IsFinished)
            .BindToDependencyProperty(btStop, IsEnabledProperty));

        // Offered only where it can actually be done. A button that explains itself only after being
        // pressed is a button that wastes the press.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => RerunCommand.IsAvailableFor(
                state.Runs.Find(run => string.Equals(run.SessionId, state.SelectedSessionId, StringComparison.Ordinal))))
            .BindToDependencyProperty(btRerun, IsEnabledProperty));

        // Dead unless there is a failure to jump to.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Stages.Any(stage => stage.Steps.Any(
                step => step.Lifecycle is DebugLifecycleState.Error or DebugLifecycleState.Timeout)))
            .BindToDependencyProperty(btFirstFailure, IsEnabledProperty));

        // Looked for in the background: finding Visual Studio runs vswhere, and the title bar must not wait
        // on a process launch to appear. The buttons stay hidden until the answer arrives, which for an
        // editor that is not installed is where they stay.
        _ = ShowEditorsAsync();

        btContinue.ToolTip = Shortcuts.Describe("Release the breakpoint", Shortcuts.Continue);
        btStep.ToolTip = Shortcuts.Describe("Run on to the next step and stop there", Shortcuts.StepForward);
        btStop.ToolTip = Shortcuts.Describe("Ask the run to stop", Shortcuts.Stop);
        btRerun.ToolTip = Shortcuts.Describe("Run this test again", Shortcuts.Rerun);
        btFirstFailure.ToolTip = Shortcuts.Describe("Jump to the first failure", Shortcuts.FirstFailure);
        btSummary.ToolTip = Shortcuts.Describe("Can I trust this run", Shortcuts.Summary);
        btFit.ToolTip = Shortcuts.Describe("Fit the whole run on screen", Shortcuts.Fit);
        btShare.ToolTip = "Share this run as a file";

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader asks for the run's summary.</summary>
    public event Action? SummaryRequested;

    /// <summary>Raised when the reader asks for the board to be fitted to the window.</summary>
    public event Action? FitRequested;

    /// <summary>Raised when the reader asks to be taken to the first failed step.</summary>
    public event Action? FirstFailureRequested;

    /// <summary>Raised when the reader asks to share the selected run.</summary>
    public event Action? ShareRequested;

    private static RunSummary? Selected(MainState state)
        => state.Runs.Find(run => string.Equals(run.SessionId, state.SelectedSessionId, StringComparison.Ordinal));

    private async void btContinue_Click(object sender, RoutedEventArgs e)
        => await MainWindow.Shell.ContinueSelectedRunAsync();

    private async void btStep_Click(object sender, RoutedEventArgs e) => await StepAsync();

    /// <summary>
    /// Releases the run and has it stop again at its next step.
    /// </summary>
    /// <remarks>
    /// Armed before the release, not after: a run let go first can reach its next step and ask about it
    /// before this side has said anything, and would then run to the end. If the release fails the arming
    /// is withdrawn, so a run that was never let go does not stop unbidden later.
    /// </remarks>
    internal static async Task StepAsync()
    {
        string? sessionId = StateStore<MainState>.Default.GetValue(state => state.SelectedSessionId);

        if (sessionId is null)
            return;

        Breakpoints.StepOnce(sessionId);

        if (!await MainWindow.Shell.ContinueSelectedRunAsync())
            Breakpoints.CancelStep(sessionId);
    }

    private async void btStop_Click(object sender, RoutedEventArgs e)
        => await MainWindow.Shell.CancelSelectedRunAsync();

    private async void btRerun_Click(object sender, RoutedEventArgs e)
        => await MainWindow.Shell.RerunSelectedAsync();

    /// <summary>
    /// Shows a button for each editor that is actually installed.
    /// </summary>
    /// <remarks>
    /// The rule is the same for both: found means a button, not found means nothing at all. A disabled
    /// button for an editor the machine does not have would be permanent furniture advertising something
    /// the reader cannot use.
    /// </remarks>
    private async Task ShowEditorsAsync()
    {
        EditorSet editors = await ExternalEditors.ResolveAsync().ConfigureAwait(true);

        code = editors.Code;
        visualStudio = editors.VisualStudio;

        Show(btCode, imgCode, code);
        Show(btVisualStudio, imgVisualStudio, visualStudio);

        bEditorRule.Visibility = code is null && visualStudio is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Show(Button button, Image image, ExternalEditor? editor)
    {
        if (editor is null)
        {
            button.Visibility = Visibility.Collapsed;
            return;
        }

        image.Source = editor.Icon;
        button.ToolTip = $"Open this test's solution in {editor.Name}";
        button.Visibility = Visibility.Visible;
    }

    private void btCode_Click(object sender, RoutedEventArgs e) => Open(code);

    private void btVisualStudio_Click(object sender, RoutedEventArgs e) => Open(visualStudio);

    /// <summary>
    /// Opens the selected run's solution in an editor.
    /// </summary>
    /// <remarks>
    /// The project file is the run's own — the one recorded so the test could be run again — so this opens
    /// the code that produced what is on screen rather than whatever the reader last had open. A run that
    /// never reported one says so instead of opening something arbitrary.
    /// </remarks>
    private void Open(ExternalEditor? editor)
    {
        if (editor is null)
            return;

        RunSummary? run = StateStore<MainState>.Default.GetValue(Selected);

        string? target = EditorPaths.TargetFor(run?.ProjectFilePath, editor.WantsFolder, ExternalEditors.SolutionsIn);

        if (target is null)
        {
            MainWindow.Shell.Report(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Warning,
                Source = FeedSource.App,
                Title = $"There is nothing to open in {editor.Name}.",
                Detail = "This run did not record which project file it came from."
            });

            return;
        }

        if (!ExternalEditors.TryOpen(editor, target))
        {
            MainWindow.Shell.Report(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Warning,
                Source = FeedSource.App,
                Title = $"{editor.Name} could not be started.",
                Detail = editor.ExecutablePath
            });
        }
    }

    private void btFirstFailure_Click(object sender, RoutedEventArgs e) => FirstFailureRequested?.Invoke();

    private void btSummary_Click(object sender, RoutedEventArgs e) => SummaryRequested?.Invoke();

    private void btFit_Click(object sender, RoutedEventArgs e) => FitRequested?.Invoke();

    private void btShare_Click(object sender, RoutedEventArgs e) => ShareRequested?.Invoke();
}
