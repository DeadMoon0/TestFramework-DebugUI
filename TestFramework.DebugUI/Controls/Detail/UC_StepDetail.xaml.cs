using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
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

    /// <summary>The wash behind every other row, which is what separates one long entry from two.</summary>
    private static readonly Brush Odd = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));

    /// <summary>The resize handle when nobody is near it.</summary>
    private static readonly Brush Resting = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    private StepNode? step;
    private string? stageName;
    private LogNode[] entries = [];

    private bool sizing;
    private double sizingFrom;
    private double sizingWidth;

    /// <summary>Creates the panel and binds it.</summary>
    public UC_StepDetail()
    {
        InitializeComponent();

        // Three fields, each of them something a reader ends up wanting somewhere else: an identity to
        // paste into a filter, a message to paste into a ticket, a trace to read in an editor that wraps.
        // Everything else on this panel is a label, a count or a list of names already on the board, and a
        // button under the pointer on all twelve of them made the panel feel like a form.
        Copyable.Enable(tbName, tbFailureMessage, tbStack);

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(Resolve)
            .Subscribe(Show));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>
    /// Raised when the reader has finished dragging the panel wider or narrower.
    /// </summary>
    /// <remarks>
    /// On finishing rather than on every pixel of the drag. The width is remembered in the settings file, and
    /// a file written a hundred times while somebody drags a handle is a file being written for no reason.
    /// </remarks>
    public event Action<double>? Resized;

    /// <summary>
    /// Opens the panel at a remembered width.
    /// </summary>
    /// <remarks>
    /// Clamped against the window it is actually opening in, not the one it was saved on: a width from a wide
    /// desktop would otherwise cover a laptop's whole board.
    /// </remarks>
    public void SetWidth(double width)
    {
        Width = PanelWidth.Clamp(width, Available());
    }

    /// <summary>Brings the panel back inside the window after the window itself was made smaller.</summary>
    public void Reclamp()
    {
        if (!sizing)
            Width = PanelWidth.Clamp(Width, Available());
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

        ShowPolicy();
        ShowAttempts();
        ShowLog();
        ShowFailure();
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
            wpPolicy.Children.Add(Chip($"times out after {Duration(timeout)}"));
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
        Background = (Brush)FindResource("SurfaceRaised"),
        Margin = new Thickness(0, 0, 4, 4),
        Padding = new Thickness(6, 2, 6, 2),
        Child = new TextBlock
        {
            Style = (Style)FindResource("MutedText"),
            FontSize = 10.5,
            Text = text
        }
    };

    /// <summary>A duration as a reader would say it, rather than as 00:00:30.</summary>
    private static string Duration(TimeSpan value)
    {
        if (value.TotalSeconds < 1)
            return $"{value.TotalMilliseconds:0} ms";

        if (value.TotalMinutes < 1)
            return $"{value.TotalSeconds:0.#} s";

        return value.TotalHours < 1
            ? $"{value.TotalMinutes:0.#} min"
            : $"{value.TotalHours:0.#} h";
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

    /// <summary>
    /// Lays the step's log out one entry per row.
    /// </summary>
    /// <remarks>
    /// Built rather than bound, like the rest of this window, and rebuilt whole on every change: a log grows
    /// only at its end, and is short enough per step that appending would be more code than it saves.
    /// </remarks>
    private void ShowLog()
    {
        spLog.Children.Clear();

        if (step is null)
            return;

        entries = [.. step.Attempts.SelectMany(attempt => attempt.Logs)];

        tbLogCount.Text = entries.Length == 0 ? string.Empty : LogLines.Count(entries.Length);
        btCopyLog.Visibility = entries.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (entries.Length == 0)
        {
            spLog.Children.Add(new TextBlock { Text = "Nothing logged.", Style = (Style)FindResource("MutedText") });
            return;
        }

        for (int index = 0; index < entries.Length; index++)
            spLog.Children.Add(Row(entries[index], index));
    }

    /// <summary>
    /// One log entry: when, how bad, and what it said.
    /// </summary>
    /// <remarks>
    /// The message is the only column that wraps, and it wraps inside its own column, so a long entry stays
    /// under itself instead of running back beneath the timestamps. Every other row is washed a shade
    /// lighter, which is what tells one entry over two lines apart from two entries.
    /// </remarks>
    private Grid Row(LogNode entry, int index)
    {
        Grid row = new()
        {
            Background = index % 2 == 1 ? Odd : Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "LogTime" },
                new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "LogLevel" },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        TextBlock time = new()
        {
            Style = (Style)FindResource("CodeText"),
            Foreground = (Brush)FindResource("TextFaint"),
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(4, 1, 8, 1),
            Text = LogLines.Time(entry)
        };

        TextBlock level = new()
        {
            Style = (Style)FindResource("CodeText"),
            Foreground = (Brush)FindResource(LogLines.Brush(entry.Level)),
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 1, 8, 1),
            Text = LogLines.Tag(entry.Level)
        };

        TextBlock message = new()
        {
            Style = (Style)FindResource("CodeText"),
            Foreground = (Brush)FindResource(entry.Level == DebugLogLevel.Information ? "TextSecondary" : "TextPrimary"),
            Margin = new Thickness(0, 1, 4, 1),
            Text = entry.Render()
        };

        Grid.SetColumn(level, 1);
        Grid.SetColumn(message, 2);

        row.Children.Add(time);
        row.Children.Add(level);
        row.Children.Add(message);

        return row;
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

    /// <summary>
    /// Puts the whole log on the clipboard, in the form the panel shows it in.
    /// </summary>
    private void btCopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (entries.Length != 0 && Clipboards.Set(LogLines.Text(entries)))
            CopyGlyph.Confirm(pathCopyLog, this);
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

        // The clipboard can be held by another process, and Clip is where that is dealt with once for every
        // copy in the application rather than here for this one.
        Clipboards.Set(text.ToString());
    }

    /// <summary>How much room the panel has to grow into.</summary>
    private double Available()
    {
        FrameworkElement? host = Parent as FrameworkElement;

        return host?.ActualWidth ?? double.NaN;
    }

    private void bGrip_MouseEnter(object sender, MouseEventArgs e)
        => bGripBar.Background = (Brush)FindResource("Accent");

    private void bGrip_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!sizing)
            bGripBar.Background = Resting;
    }

    /// <summary>
    /// Starts a drag.
    /// </summary>
    /// <remarks>
    /// The pointer is measured against the window rather than against this panel, because this panel is the
    /// thing being resized: every position taken inside it would be measured from an edge that had just moved.
    /// </remarks>
    private void bGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Parent is not IInputElement host)
            return;

        sizing = true;
        sizingFrom = e.GetPosition(host).X;
        sizingWidth = ActualWidth;

        bGrip.CaptureMouse();
        e.Handled = true;
    }

    private void bGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!sizing || Parent is not IInputElement host)
            return;

        // Leftwards is wider: the panel is pinned to the right edge, so dragging its left edge away from that
        // edge is asking for more of the window.
        Width = PanelWidth.Clamp(sizingWidth + (sizingFrom - e.GetPosition(host).X), Available());
    }

    private void bGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sizing)
            bGrip.ReleaseMouseCapture();
    }

    /// <summary>
    /// Ends a drag, however it ended.
    /// </summary>
    /// <remarks>
    /// On losing capture rather than only on the button coming up, so a drag interrupted by anything else -
    /// another window taking focus, a dialog opening - leaves the panel at a width and not mid-drag.
    /// </remarks>
    private void bGrip_LostCapture(object sender, MouseEventArgs e)
    {
        if (!sizing)
            return;

        sizing = false;

        if (!bGrip.IsMouseOver)
            bGripBar.Background = Resting;

        Resized?.Invoke(Width);
    }
}
