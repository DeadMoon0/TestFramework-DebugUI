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
using TestFramework.DebugUI.Editors;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Diagnostics;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell.Feed;

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
/// Every button says what was asked for and nothing else. What acts on the run lives with whoever holds
/// the run — the window — and what moves the board lives with the board; either way, a bar that reached
/// across to do the work itself would make two controls responsible for one thing.
/// </para>
/// </remarks>
public partial class UC_RunBar : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    private ExternalEditor? code;
    private ExternalEditor? visualStudio;
    private SourceLocation? source;

    /// <summary>Creates the bar and binds it.</summary>
    public UC_RunBar()
    {
        InitializeComponent();

        // Left in the casing the test author wrote: a test name is PascalCase and nothing else, so
        // upper-casing it removes the only thing separating its words.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectSelectedRun)
            .Select(run => run?.Name ?? string.Empty)
            .BindToDependencyProperty(tbRunName, TextBlock.TextProperty));

        // Capped and trimmed, so a long name cannot push the actions off the bar. The qualified name is on
        // the tooltip for when the trimmed one is not enough. No copy button on it: this is the title bar,
        // and a button that appears under the pointer in the strip you grab the window by is a button in
        // the way. The same name is copyable from the step panel, where reading it is the point.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectSelectedTest)
            .Subscribe(test => tbRunName.ToolTip = string.IsNullOrWhiteSpace(test) ? null : test));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectHasSelection)
            .Select(selected => selected ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(this, VisibilityProperty));

        // Shown only when there is something to release. A button that is always there and almost always
        // does nothing teaches people not to look at it.
        IObservable<Visibility> held = StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectIsWaitingAtBreakpoint)
            .Select(waiting => waiting ? Visibility.Visible : Visibility.Collapsed);

        subscriptions.Add(held.BindToDependencyProperty(btContinue, VisibilityProperty));
        subscriptions.Add(held.BindToDependencyProperty(btStep, VisibilityProperty));
        subscriptions.Add(held.BindToDependencyProperty(btLook, VisibilityProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectIsRunning)
            .BindToDependencyProperty(btStop, IsEnabledProperty));

        // Offered only where it can actually be done. A button that explains itself only after being
        // pressed is a button that wastes the press.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectCanRerunSelected)
            .BindToDependencyProperty(btRerun, IsEnabledProperty));

        // Dead unless there is a failure to jump to.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectHasFailure)
            .BindToDependencyProperty(btFirstFailure, IsEnabledProperty));

        // The tooltip names the file and line the button will land on, because "open in VS Code" and "open
        // OrderTests.cs at line 42" are different offers and only the second one is worth crossing the
        // window for. Re-read on selection because the answer belongs to the run, not to the editor.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectSelectedRun)
            .Select(run => run?.Source)
            .Subscribe(location =>
            {
                source = location;
                ShowEditorTargets();
            }));

        // Looked for in the background: finding Visual Studio runs vswhere, and the title bar must not wait
        // on a process launch to appear. The buttons stay hidden until the answer arrives, which for an
        // editor that is not installed is where they stay.
        _ = ShowEditorsAsync();

        btLook.ToolTip = "Ask the run to show what it is looking at now";
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

    /// <summary>Raised when the reader asks to release the run from its breakpoint.</summary>
    public event Action? ContinueRequested;

    /// <summary>Raised when the reader asks the run to take one step.</summary>
    public event Action? StepRequested;

    /// <summary>
    /// Raised when the reader asks the run for a fresh look at what it is holding.
    /// </summary>
    /// <remarks>
    /// A task, unlike the events beside it, because this one is a question with an answer and the
    /// button has to reflect the wait: photographing a live page takes long enough to press again,
    /// and a second press only asks something that is already outstanding.
    /// </remarks>
    public event Func<Task>? LookRequested;

    /// <summary>Raised when the reader asks the run to stop.</summary>
    public event Action? StopRequested;

    /// <summary>Raised when the reader asks for the run's test to be run again.</summary>
    public event Action? RerunRequested;

    private void btContinue_Click(object sender, RoutedEventArgs e) => ContinueRequested?.Invoke();

    private void btStep_Click(object sender, RoutedEventArgs e) => StepRequested?.Invoke();

    /// <summary>
    /// Asks for the look, and stays dead until the answer comes back.
    /// </summary>
    /// <remarks>
    /// The one button here that waits, so the one that has to say so. Anything thrown is logged
    /// rather than escaping: an unhandled exception from an async void handler takes the window with
    /// it, and a screenshot that failed is not worth the tool.
    /// </remarks>
    private async void btLook_Click(object sender, RoutedEventArgs e)
    {
        Func<Task>? asked = LookRequested;

        if (asked is null)
            return;

        btLook.IsEnabled = false;

        try
        {
            await asked();
        }
        catch (Exception exception)
        {
            Log.Write(exception);
        }
        finally
        {
            btLook.IsEnabled = true;
        }
    }

    private void btStop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void btRerun_Click(object sender, RoutedEventArgs e) => RerunRequested?.Invoke();

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

        ShowEditorTargets();

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
        button.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Says what each editor button will actually open.
    /// </summary>
    /// <remarks>
    /// Visual Studio is named without a line even when the run reported one, because devenv cannot be told a
    /// line and promising one the reader will not get is worse than promising less.
    /// </remarks>
    private void ShowEditorTargets()
    {
        if (code is not null)
            btCode.ToolTip = Target(code, withLine: true);

        if (visualStudio is not null)
            btVisualStudio.ToolTip = Target(visualStudio, withLine: false);
    }

    private string Target(ExternalEditor editor, bool withLine)
    {
        if (source is null)
            return $"Open this test's solution in {editor.Name}";

        return withLine && source.Line > 0
            ? $"Open {source.FileName} line {source.Line} in {editor.Name}"
            : $"Open {source.FileName} in {editor.Name}";
    }

    private void btCode_Click(object sender, RoutedEventArgs e) => Open(code);

    private void btVisualStudio_Click(object sender, RoutedEventArgs e) => Open(visualStudio);

    /// <summary>
    /// Opens the selected run in an editor, at the line that started it when the run recorded one.
    /// </summary>
    /// <remarks>
    /// The project file and the source line are the run's own, so this opens the code that produced what is
    /// on screen rather than whatever the reader last had open. Core has captured the call site at compile
    /// time all along; until now the button ignored it and opened the solution, leaving the reader to find
    /// one test among a suite. A run that reported nothing at all says so instead of opening something
    /// arbitrary.
    /// </remarks>
    private void Open(ExternalEditor? editor)
    {
        if (editor is null)
            return;

        RunSummary? run = StateStore<MainState>.Default.GetValue(RunsSelectors.SelectedRunOf);

        System.Collections.Immutable.ImmutableList<string> arguments = EditorPaths.ArgumentsFor(
            run?.ProjectFilePath,
            editor.WantsFolder,
            run?.Source,
            ExternalEditors.SolutionsIn);

        if (arguments.Count == 0)
        {
            StateStore<MainState>.Default.Dispatch(FeedActions.AppendEntry, new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Warning,
                Source = FeedSource.App,
                Title = $"There is nothing to open in {editor.Name}.",
                Detail = "This run recorded neither a project file nor the source it was started from."
            });

            return;
        }

        if (!ExternalEditors.TryOpen(editor, arguments))
        {
            StateStore<MainState>.Default.Dispatch(FeedActions.AppendEntry, new FeedEntry
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
