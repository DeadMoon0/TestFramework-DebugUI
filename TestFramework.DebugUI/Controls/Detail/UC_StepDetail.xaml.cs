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
public partial class UC_StepDetail : UserControl, IDisposable
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>The wash behind every other row, which is what separates one long entry from two.</summary>
    private static readonly Brush Odd = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));

    private StepNode? step;
    private string? stageName;

    /// <summary>How this run's steps timed against the last run of the test that passed.</summary>
    private TimingDiff timing = TimingDiff.None;
    private LogNode[] entries = [];

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

        // Arrives later than the step does, because a baseline means reading an earlier run's journal. Kept
        // so the line can be filled in when it lands rather than only on the next selection.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveTiming)
            .Subscribe(compared =>
            {
                timing = compared;
                ShowTiming();
            }));

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

        ShowTiming();
        ShowPolicy();
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
            tbTiming.Text = $"took {Duration(took)}";
            tbTiming.Foreground = (Brush)FindResource("TextSecondary");
            return;
        }

        string ratio = compared.Ratio is { } times ? $", {times:0.#}×" : string.Empty;

        tbTiming.Text = compared.Change switch
        {
            StepTimingChange.Slower => $"took {Duration(took)} — {Duration(compared.Delta)} longer than when this test last passed{ratio}",
            StepTimingChange.Faster => $"took {Duration(took)} — {Duration(compared.Delta.Duration())} quicker than when this test last passed{ratio}",
            _ => $"took {Duration(took)}, about the same as the {Duration(before)} it took when this test last passed"
        };

        tbTiming.Foreground = (Brush)FindResource(compared.Change switch
        {
            StepTimingChange.Slower => "StateTimeout",
            StepTimingChange.Faster => "StateComplete",
            _ => "TextSecondary"
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

        // The event that emitted the line, on the pointer rather than in a column. It is the same handful of
        // names down the whole log, so a column of it would be a column of repetition — but it is the thing
        // to search on when a reader wants every line one kind of event produced, and a tooltip is where
        // they can find out what to type.
        if (entry.EventName is { Length: > 0 } emitter)
            row.ToolTip = ShortTypeName(emitter);

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

        ShowCauses(failure);

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

    /// <summary>
    /// Lists what the failure was wrapped around, outermost first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each link is stepped further in than the one above it, so the nesting is visible without a label
    /// saying "depth 2". Capped after a few levels — a chain deep enough to run out of indent is deep
    /// enough that the exact level has stopped mattering.
    /// </para>
    /// <para>
    /// The type is shown short with the full name on the pointer. Fully qualified is what the wire carries
    /// and what a reader wants when they go looking for it; it is also sixty characters of namespace in a
    /// panel this wide, which pushes the message — the part that says what happened — onto a third line.
    /// </para>
    /// </remarks>
    private void ShowCauses(DebugFailureDetail failure)
    {
        spCausedBy.Children.Clear();

        tbCausedByLabel.Visibility = failure.InnerExceptions.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        for (int depth = 0; depth < failure.InnerExceptions.Count; depth++)
        {
            DebugExceptionLink link = failure.InnerExceptions[depth];

            StackPanel row = new()
            {
                Margin = new Thickness(Math.Min(depth, 3) * 10, depth == 0 ? 0 : 6, 0, 0)
            };

            row.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("MutedText"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondary"),
                Text = ShortTypeName(link.ExceptionType),
                ToolTip = link.ExceptionType
            });

            row.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 1, 0, 0),
                Text = link.Message
            });

            spCausedBy.Children.Add(row);
        }
    }

    /// <summary>
    /// An exception type without its namespace.
    /// </summary>
    /// <remarks>
    /// Split on the last dot rather than trimming a known prefix: the chain mixes framework types with the
    /// consumer's own, and there is no one namespace to strip.
    /// </remarks>
    private static string ShortTypeName(string typeName)
    {
        int lastDot = typeName.LastIndexOf('.');

        return lastDot >= 0 && lastDot < typeName.Length - 1 ? typeName[(lastDot + 1)..] : typeName;
    }

    private static void Fill(TextBlock label, TextBlock body, string? content)
    {
        bool has = !string.IsNullOrWhiteSpace(content);

        label.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        body.Text = content ?? string.Empty;
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

        // Indented the way the panel steps them, because the nesting is part of what is being handed over.
        foreach (DebugExceptionLink link in failure.InnerExceptions)
            text.AppendLine($"  ---> {link.ExceptionType}: {link.Message}");

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
