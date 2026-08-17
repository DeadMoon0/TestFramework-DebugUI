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

    /// <summary>Creates the bar and binds it.</summary>
    public UC_RunBar()
    {
        InitializeComponent();

        // Left in the casing the test author wrote: a test name is PascalCase and nothing else, so
        // upper-casing it removes the only thing separating its words.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => Selected(state)?.Name ?? string.Empty)
            .BindToDependencyProperty(tbRunName, TextBlock.TextProperty));

        // Capped and trimmed, so a long name cannot push the actions off the bar. The qualified name is
        // on the tooltip for when the trimmed one is not enough.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => Selected(state)?.Test ?? string.Empty)
            .Subscribe(test => tbRunName.ToolTip = string.IsNullOrWhiteSpace(test) ? null : test));

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

        btContinue.ToolTip = Shortcuts.Describe("Release the breakpoint", Shortcuts.Continue);
        btStep.ToolTip = Shortcuts.Describe("Run on to the next step and stop there", Shortcuts.StepForward);
        btStop.ToolTip = Shortcuts.Describe("Ask the run to stop", Shortcuts.Stop);
        btRerun.ToolTip = Shortcuts.Describe("Run this test again", Shortcuts.Rerun);
        btFirstFailure.ToolTip = Shortcuts.Describe("Jump to the first failure", Shortcuts.FirstFailure);
        btSummary.ToolTip = Shortcuts.Describe("Can I trust this run", Shortcuts.Summary);
        btFit.ToolTip = Shortcuts.Describe("Fit the whole run on screen", Shortcuts.Fit);

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader asks for the run's summary.</summary>
    public event Action? SummaryRequested;

    /// <summary>Raised when the reader asks for the board to be fitted to the window.</summary>
    public event Action? FitRequested;

    /// <summary>Raised when the reader asks to be taken to the first failed step.</summary>
    public event Action? FirstFailureRequested;

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

    private void btFirstFailure_Click(object sender, RoutedEventArgs e) => FirstFailureRequested?.Invoke();

    private void btSummary_Click(object sender, RoutedEventArgs e) => SummaryRequested?.Invoke();

    private void btFit_Click(object sender, RoutedEventArgs e) => FitRequested?.Invoke();
}
