using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// Everything about the selected step.
/// </summary>
/// <remarks>
/// Ordered by what a reader came for. A step that failed leads with the failure — including the
/// framework's own explanation and recovery steps, rendered as content rather than buried in log
/// text, which is the whole point of Core carrying them. Everything else follows underneath.
/// </remarks>
public partial class UC_StepDetail : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    private StepNode? step;
    private string? stageName;

    /// <summary>Creates the panel and binds it.</summary>
    public UC_StepDetail()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(Resolve)
            .Subscribe(Show));

        Unloaded += (_, _) => subscriptions.Dispose();
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
        StepSelection? selection = state.SelectedStep;
        if (selection is null)
            return (string.Empty, null);

        StageNode? stage = state.ActiveRun.Stages
            .FirstOrDefault(candidate => string.Equals(candidate.Name, selection.StageName, StringComparison.Ordinal));

        return (selection.StageName, stage?.Steps.FirstOrDefault(candidate => candidate.StepId == selection.StepId));
    }

    private void Show((string Stage, StepNode? Step) selection)
    {
        stageName = selection.Stage;
        step = selection.Step;

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

        ShowAttempts();
        ShowLog();
        ShowFailure();
    }

    private void ShowAttempts()
    {
        spAttempts.Children.Clear();

        if (step is null || step.Attempts.Count == 0)
        {
            spAttempts.Children.Add(new TextBlock { Text = "Not started.", Style = (Style)FindResource("MutedText") });
            return;
        }

        foreach (AttemptNode attempt in step.Attempts)
        {
            string outcome = attempt.Failure is null ? "ok" : attempt.Failure.ExceptionType;

            spAttempts.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("MutedText"),
                Text = $"#{attempt.Attempt} · {attempt.StartedAtUtc.ToLocalTime():HH:mm:ss} · {outcome}"
            });
        }
    }

    private void ShowLog()
    {
        if (step is null)
            return;

        LogNode[] entries = [.. step.Attempts.SelectMany(attempt => attempt.Logs)];

        tbLog.Text = entries.Length == 0
            ? "Nothing logged."
            : string.Join("\n", entries.Select(entry => $"{entry.OccurredAtUtc.ToLocalTime():HH:mm:ss} {entry.Level,-7} {entry.Message}"));
    }

    /// <summary>
    /// Shows the last attempt's failure, if there was one.
    /// </summary>
    /// <remarks>
    /// The last attempt rather than the first: a step that failed, retried and then succeeded is not
    /// broken, and leading with a failure it recovered from would be reporting a problem that no
    /// longer exists. The earlier attempts stay listed above.
    /// </remarks>
    private void ShowFailure()
    {
        DebugFailureDetail? failure = step?.Attempts.LastOrDefault()?.Failure;

        if (failure is null)
        {
            bFailure.Visibility = Visibility.Collapsed;
            btCopyFailure.Visibility = Visibility.Collapsed;
            return;
        }

        bFailure.Visibility = Visibility.Visible;
        btCopyFailure.Visibility = Visibility.Visible;

        tbFailureType.Text = failure.WasSuppressed
            ? failure.ExceptionType + "  (suppressed)"
            : failure.WillRetry
                ? failure.ExceptionType + "  (retried)"
                : failure.ExceptionType;

        tbFailureMessage.Text = failure.Message;

        Fill(tbFriendlyLabel, tbFriendly, failure.FriendlyMessage);
        Fill(tbRecoveryLabel, tbRecovery, Bullets(failure.RecoverySteps));
        Fill(tbOptionsLabel, tbOptions, Bullets(failure.AvailableOptions));

        if (string.IsNullOrWhiteSpace(failure.StackTrace))
        {
            exStack.Visibility = Visibility.Collapsed;
        }
        else
        {
            exStack.Visibility = Visibility.Visible;
            tbStack.Text = failure.StackTrace;
        }
    }

    private static void Fill(TextBlock label, TextBlock body, string? content)
    {
        bool has = !string.IsNullOrWhiteSpace(content);

        label.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        body.Text = content ?? string.Empty;
    }

    /// <summary>Lists a declared contract, naming the kind so a reader is not left guessing.</summary>
    private static string Describe(System.Collections.Immutable.ImmutableList<StepIO> declared)
        => declared.Count == 0
            ? "None declared."
            : string.Join("\n", declared.Select(entry => $"{entry.Key}  ({entry.Kind.ToString().ToLowerInvariant()})"));

    private static string? Bullets(System.Collections.Generic.IReadOnlyList<string>? lines)
        => lines is null || lines.Count == 0 ? null : string.Join("\n", lines.Select(line => "• " + line));

    private void btBreakpoint_Click(object sender, RoutedEventArgs e)
    {
        if (step is not null && stageName is not null)
            Breakpoints.Toggle(stageName, step.StepId);
    }

    /// <summary>
    /// Puts the failure on the clipboard, ready to hand to someone else.
    /// </summary>
    private void btCopyFailure_Click(object sender, RoutedEventArgs e)
    {
        DebugFailureDetail? failure = step?.Attempts.LastOrDefault()?.Failure;
        if (failure is null || step is null)
            return;

        StringBuilder text = new();
        text.AppendLine($"{step.DisplayName} ({stageName}, step {step.StepId})");
        text.AppendLine($"{failure.ExceptionType}: {failure.Message}");

        if (!string.IsNullOrWhiteSpace(failure.FriendlyMessage))
            text.AppendLine().AppendLine(failure.FriendlyMessage);

        if (Bullets(failure.RecoverySteps) is string recovery)
            text.AppendLine().AppendLine("What to do:").AppendLine(recovery);

        if (!string.IsNullOrWhiteSpace(failure.StackTrace))
            text.AppendLine().AppendLine(failure.StackTrace);

        try
        {
            Clipboard.SetText(text.ToString());
        }
        catch (Exception exception)
        {
            // The clipboard can be held by another process. Losing a copy is not worth a crash in
            // the window that is showing someone their failure.
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }
}
