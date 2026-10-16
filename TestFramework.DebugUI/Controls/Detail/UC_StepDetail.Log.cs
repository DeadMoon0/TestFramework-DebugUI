using System.Linq;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

// The step's log, one entry per row, and copying it.
public partial class UC_StepDetail
{
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
            spLog.Children.Add(new TextBlock { Text = "Nothing logged.", Style = (Style)FindResource(ThemeKeys.MutedText) });
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
            Style = (Style)FindResource(ThemeKeys.CodeText),
            Foreground = (Brush)FindResource(ThemeKeys.TextFaint),
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(4, 1, 8, 1),
            Text = LogLines.Time(entry)
        };

        TextBlock level = new()
        {
            Style = (Style)FindResource(ThemeKeys.CodeText),
            Foreground = (Brush)FindResource(LogLines.Brush(entry.Level)),
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 1, 8, 1),
            Text = LogLines.Tag(entry.Level)
        };

        TextBlock message = new()
        {
            Style = (Style)FindResource(ThemeKeys.CodeText),
            Foreground = (Brush)FindResource(entry.Level == DebugLogLevel.Information ? ThemeKeys.TextSecondary : ThemeKeys.TextPrimary),
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
    /// Puts the whole log on the clipboard, in the form the panel shows it in.
    /// </summary>
    private void btCopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (entries.Length != 0 && Clipboards.Set(LogLines.Text(entries)))
            CopyGlyph.Confirm(pathCopyLog, this);
    }
}
