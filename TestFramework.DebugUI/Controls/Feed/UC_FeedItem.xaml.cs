using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State;


namespace TestFramework.DebugUI.Controls.Feed;

/// <summary>
/// One line in the feed.
/// </summary>
/// <remarks>
/// Entries that name a run are clickable: picking one selects that run and, when the entry names a
/// step, opens it. That is what makes the feed a way of getting somewhere rather than a log to
/// scroll — "run 3 of 4 failed" becomes one click to the step that failed.
/// </remarks>
public partial class UC_FeedItem : UserControl
{
    private readonly FeedEntry entry;

    /// <summary>Creates a row for one entry.</summary>
    public UC_FeedItem(FeedEntry entry)
    {
        this.entry = entry;

        InitializeComponent();

        // Feed entries carry the transport's own diagnostics, which is exactly what gets pasted into a
        // bug report about the transport.
        Copyable.Enable(tbTitle, tbDetail);

        tbWhen.Text = entry.AtUtc.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
        tbTitle.Text = entry.Title;

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            tbDetail.Text = entry.Detail;
            tbDetail.Visibility = Visibility.Visible;
        }

        bSeverity.Background = entry.Severity switch
        {
            FeedSeverity.Error => (Brush)FindResource("StateError"),
            FeedSeverity.Warning => (Brush)FindResource("StateTimeout"),
            _ => (Brush)FindResource("TextFaint")
        };

        if (entry.SessionId is not null)
        {
            Cursor = Cursors.Hand;
            ToolTip = "Open this run.";
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (entry.SessionId is null)
            return;

        MainWindow.Shell.SelectRun(entry.SessionId);

        if (entry.Stage is not null && entry.StepId is not null)
            MainWindow.Shell.SelectStep(entry.Stage, entry.StepId.Value);
    }
}
