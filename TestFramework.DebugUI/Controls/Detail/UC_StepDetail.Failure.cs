using System;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

// The last attempt's failure, the chain of causes under it, and copying it.
public partial class UC_StepDetail
{
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
                // Indented by how deep the cause actually sat, not by its place in the list. Two exceptions
                // aggregated by the same parent are siblings, and stepping the second one further in than the
                // first would say the first had caused it.
                Margin = new Thickness(Math.Min(link.Depth, 3) * 12, depth == 0 ? 0 : 6, 0, 0)
            };

            row.Children.Add(new TextBlock
            {
                Style = (Style)FindResource(ThemeKeys.MutedText),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = (Brush)FindResource(ThemeKeys.TextSecondary),
                Text = ShortTypeName(link.ExceptionType),
                ToolTip = link.ExceptionType
            });

            row.Children.Add(new TextBlock
            {
                Style = (Style)FindResource(ThemeKeys.MutedText),
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

    private static string? Bullets(System.Collections.Generic.IReadOnlyList<string>? lines)
        => lines is null || lines.Count == 0 ? null : string.Join("\n", lines.Select(line => "• " + line));

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

        // Indented by depth, the way the panel steps them, because the nesting is part of what is handed over.
        foreach (DebugExceptionLink link in failure.InnerExceptions)
            text.AppendLine($"{new string(' ', 2 + (link.Depth * 2))}---> {link.ExceptionType}: {link.Message}");

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
}
