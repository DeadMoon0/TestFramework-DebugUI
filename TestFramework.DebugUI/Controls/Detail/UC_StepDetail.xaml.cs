using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Transport;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// Everything about the selected step.
/// </summary>
/// <remarks>
/// Ordered by what a reader came for. A step that failed leads with the failure — including the
/// framework's own explanation and recovery steps, rendered as content rather than buried in log
/// text, which is the whole point of Core carrying them. Everything else follows underneath.
/// </remarks>
public partial class UC_StepDetail : UserControl, IDisposable
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>The store whose selected step this panel shows, handed on to every widget it frames.</summary>
    private readonly ShellContext shell;

    /// <summary>
    /// The wash behind every other row, which is what separates one long entry from two.
    /// </summary>
    /// <remarks>
    /// A trace of the text colour rather than a fixed white one. The row behind has to be lighter than
    /// its surface on a dark theme and darker on a light one, and the ink is what already knows which.
    /// </remarks>
    private static Brush Odd => ThemeFollow.Wash(ThemeKeys.TextPrimary, 0x0A);

    private StepNode? step;
    private string? stageName;

    /// <summary>How this run's steps timed against the last run of the test that passed.</summary>
    private TimingDiff timing = TimingDiff.None;
    private LogNode[] entries = [];

    /// <summary>Which widgets the filmstrip is currently drawn from, so it is rebuilt only when they change.</summary>
    /// <remarks>
    /// Null means nothing is drawn for the current step yet. Not the empty string: that is also the signature
    /// of a step with no widgets, so resetting to it let such a step keep the previous step's pictures.
    /// </remarks>
    private string? shownWidgets;

    /// <summary>Raised when the reader asks to see one of this step's widgets on its own.</summary>
    /// <remarks>
    /// The panel holds the widgets but not the inspector, so it says what was asked for and the
    /// window decides where the answer goes — the same split every other surface here keeps.
    /// </remarks>
    public event Action<WidgetNode>? WidgetOpened;

    /// <summary>Creates the panel and binds it.</summary>
    /// <param name="shell">The store whose selected step this panel shows, and what it may ask the shell to do.</param>
    public UC_StepDetail(ShellContext shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        this.shell = shell;

        InitializeComponent();

        // Three fields, each of them something a reader ends up wanting somewhere else: an identity to
        // paste into a filter, a message to paste into a ticket, a trace to read in an editor that wraps.
        // Everything else on this panel is a label, a count or a list of names already on the board, and a
        // button under the pointer on all twelve of them made the panel feel like a form.
        Copyable.Enable(tbName, tbFailureMessage, tbStack);

        subscriptions.Add(shell.Store
            .Bind(Resolve)
            .Subscribe(Show));

        // Arrives later than the step does, because a baseline means reading an earlier run's journal. Kept
        // so the line can be filled in when it lands rather than only on the next selection.
        subscriptions.Add(shell.Store
            .Bind(ComparisonSelectors.SelectTiming)
            .Subscribe(compared =>
            {
                timing = compared;
                ShowTiming();
            }));

        // Its own subscription because a widget arrives without the step changing: a picture taken
        // during a step that is still running would otherwise not appear until something else moved.
        subscriptions.Add(shell.Store
            .Bind(BoardSelectors.SelectActiveRun)
            .Subscribe(ShowWidgets));
    }

    /// <summary>
    /// Finds the selected step in the current graph.
    /// </summary>
    /// <remarks>
    /// Resolved on every change rather than captured once, because the graph is replaced as events
    /// arrive: holding the node would freeze the panel at whatever the step looked like when it was
    /// clicked, which is usually before it had done anything.
    /// </remarks>
    private static (string Stage, StepNode? Step) Resolve(MainState state)
    {
        StepSelection? selection = state.Board.SelectedStep;
        if (selection is null)
            return (string.Empty, null);

        StageNode? stage = state.Board.ActiveRun.Stages
            .FirstOrDefault(candidate => string.Equals(candidate.Name, selection.StageName, StringComparison.Ordinal));

        return (selection.StageName, stage?.Steps.FirstOrDefault(candidate => candidate.StepId == selection.StepId));
    }

    private void Show((string Stage, StepNode? Step) selection)
    {
        stageName = selection.Stage;
        step = selection.Step;

        // A different step has different evidence, so what is drawn belongs to the step before this
        // one until it is rebuilt. Cleared rather than compared, because the signature of two steps'
        // widgets can legitimately match.
        shownWidgets = null;

        if (step is null)
        {
            svBody.Visibility = Visibility.Collapsed;
            tbEmpty.Visibility = Visibility.Visible;
            return;
        }

        tbEmpty.Visibility = Visibility.Collapsed;
        svBody.Visibility = Visibility.Visible;

        tbName.Text = step.DisplayName;
        tbKind.Text = $"{step.Name} · {selection.Stage} · step {step.StepId} · {step.Lifecycle.ToString().ToLowerInvariant()}";
        tbDescription.Text = step.Description;

        tbInputs.Text = Describe(step.Inputs);
        tbOutputs.Text = Describe(step.Outputs);

        ShowTiming();
        ShowPolicy();
        ShowWidgets(shell.Store.GetValue(state => state.Board.ActiveRun));
        ShowAttempts();
        ShowLog();
        ShowFailure();
    }

    /// <summary>
    /// Says what the step cost, and whether that is what it normally costs.
    /// </summary>
    /// <remarks>
    /// One line rather than a section. A step that took as long as it always takes still says how long that
    /// was, because the number is worth having; the comparison is only added when there is one, so the line
    /// does not grow a clause reading "no earlier run to compare against" on every step of every first run.
    /// </remarks>
    private void ShowTiming()
    {
        if (step?.Duration is not { } took)
        {
            tbTiming.Visibility = Visibility.Collapsed;
            return;
        }

        tbTiming.Visibility = Visibility.Visible;

        StepTiming? compared = stageName is null ? null : timing.ForStep(stageName, step.StepId);

        if (compared?.Then is not { } before)
        {
            tbTiming.Text = $"took {DurationText.Compact(took)}";
            tbTiming.Foreground = (Brush)FindResource(ThemeKeys.TextSecondary);
            return;
        }

        string ratio = compared.Ratio is { } times ? $", {times:0.#}×" : string.Empty;

        tbTiming.Text = compared.Change switch
        {
            StepTimingChange.Slower => $"took {DurationText.Compact(took)} — {DurationText.Compact(compared.Delta)} longer than when this test last passed{ratio}",
            StepTimingChange.Faster => $"took {DurationText.Compact(took)} — {DurationText.Compact(compared.Delta.Duration())} quicker than when this test last passed{ratio}",
            _ => $"took {DurationText.Compact(took)}, about the same as the {DurationText.Compact(before)} it took when this test last passed"
        };

        tbTiming.Foreground = (Brush)FindResource(compared.Change switch
        {
            StepTimingChange.Slower => ThemeKeys.StateTimeout,
            StepTimingChange.Faster => ThemeKeys.StateComplete,
            _ => ThemeKeys.TextSecondary
        });
    }

    /// <summary>
    /// States the policies the step runs under, as chips.
    /// </summary>
    /// <remarks>
    /// Hidden entirely for a step that declared none, which is most of them: a section reading "no retries, no
    /// timeout" for every step would push the failure and the log further down for nothing. A policy a test
    /// pinned to a variable names the variable, because that is what the run said.
    /// </remarks>
    private void ShowPolicy()
    {
        wpPolicy.Children.Clear();

        StepPolicy policy = step?.Policy ?? StepPolicy.None;

        tbPolicyLabel.Visibility = policy.IsStated ? Visibility.Visible : Visibility.Collapsed;

        if (!policy.IsStated)
            return;

        if (policy.MaxRetries is int retries)
            wpPolicy.Children.Add(Chip(retries == 1 ? "1 retry" : $"{retries} retries"));
        else if (policy.MaxRetriesVariable is string retriesFrom)
            wpPolicy.Children.Add(Chip($"retries from {retriesFrom}"));

        if (policy.TimeOut is TimeSpan timeout)
            wpPolicy.Children.Add(Chip($"times out after {DurationText.Compact(timeout)}"));
        else if (policy.TimeOutVariable is string timeoutFrom)
            wpPolicy.Children.Add(Chip($"timeout from {timeoutFrom}"));

        if (policy.RunsAlone)
            wpPolicy.Children.Add(Chip("runs alone"));

        foreach (string ignored in policy.IgnoredExceptions)
            wpPolicy.Children.Add(Chip($"ignores {ignored}"));
    }

    /// <summary>A policy as a chip, which is how the value rail already states a fact about a value.</summary>
    private Border Chip(string text) => new()
    {
        CornerRadius = new CornerRadius(3),
        Background = (Brush)FindResource(ThemeKeys.SurfaceRaised),
        Margin = new Thickness(0, 0, 4, 4),
        Padding = new Thickness(6, 2, 6, 2),
        Child = new TextBlock
        {
            Style = (Style)FindResource(ThemeKeys.MutedText),
            FontSize = 10.5,
            Text = text
        }
    };

    /// <summary>Lists every attempt the step made, and what became of each.</summary>
    private void ShowAttempts()
    {
        spAttempts.Children.Clear();

        if (step is null || step.Attempts.Count == 0)
        {
            spAttempts.Children.Add(new TextBlock { Text = "Not started.", Style = (Style)FindResource(ThemeKeys.MutedText) });
            return;
        }

        foreach (AttemptNode attempt in step.Attempts)
        {
            string outcome = attempt.Failure is null ? "ok" : attempt.Failure.ExceptionType;

            spAttempts.Children.Add(new TextBlock
            {
                Style = (Style)FindResource(ThemeKeys.MutedText),
                Text = $"#{attempt.Attempt} · {attempt.StartedAtUtc.ToLocalTime():HH:mm:ss} · {outcome}"
            });
        }
    }

    /// <summary>
    /// Lists a declared contract, naming the kind so a reader is not left guessing.
    /// </summary>
    /// <remarks>
    /// The declared type is included because a key on its own says what the step reads and not what it
    /// expects to find there — which is the half that matters when a value arrives and the step rejects it.
    /// Only optionality is spelled out: required is the protocol's default and almost every entry, so
    /// stating it on every line would spend the reader's attention on the unremarkable half.
    /// </remarks>
    private static string Describe(System.Collections.Immutable.ImmutableList<StepIO> declared)
    {
        if (declared.Count == 0)
            return "None declared.";

        return string.Join("\n", declared.Select(entry =>
        {
            string named = entry.DeclaredType is { Length: > 0 } type ? $"{entry.Key} : {type}" : entry.Key;
            string kind = entry.Kind.ToString().ToLowerInvariant();

            return entry.Required ? $"{named}  ({kind})" : $"{named}  ({kind}, optional)";
        }));
    }

    /// <summary>
    /// Lets go of the store.
    /// </summary>
    /// <remarks>
    /// Called by the host when the window closes, not when the panel leaves the visual tree. Moving a panel to
    /// another dock takes it out of one parent and puts it in another, and WPF raises <c>Unloaded</c> in
    /// between — so disposing there would kill a panel the first time it was ever dragged. A closed panel
    /// keeping its subscriptions also means it reopens showing whatever the reader left in it.
    /// </remarks>
    public void Dispose()
    {
        subscriptions.Dispose();
        GC.SuppressFinalize(this);
    }
}
