using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI;

// The bell in the title bar and the feed it opens.
public partial class MainWindow
{
    private void btNotifications_Click(object sender, RoutedEventArgs e)
    {
        ucFeed.Toggle();
        ShowNotificationState();
    }

    /// <summary>
    /// Puts the unread count on the bell, in the colour of the worst thing in it.
    /// </summary>
    /// <remarks>
    /// The colour is a lifecycle colour rather than the accent: red and amber here mean the same as they mean on
    /// a step, which is the whole reason one glance at the title bar is enough. A bell with nothing unread is as
    /// quiet as the rest of the caption.
    /// </remarks>
    private void ShowNotificationState()
    {
        bUnread.Visibility = unread.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        tbUnread.Text = unread.Count > 9 ? "9+" : unread.Count.ToString(CultureInfo.InvariantCulture);

        bUnread.Background = (Brush)FindResource(unread.Worst switch
        {
            FeedSeverity.Error => ThemeKeys.StateError,
            FeedSeverity.Warning => ThemeKeys.StateTimeout,
            _ => ThemeKeys.Accent
        });

        pathBell.Stroke = (Brush)FindResource(
            ucFeed.IsOpen ? ThemeKeys.Accent
            : unread.Count == 0 ? ThemeKeys.TextSecondary
            : unread.Worst switch
            {
                FeedSeverity.Error => ThemeKeys.StateError,
                FeedSeverity.Warning => ThemeKeys.StateTimeout,
                _ => ThemeKeys.TextSecondary
            });

        btNotifications.ToolTip = unread.Count == 0
            ? "What runs off screen have reported"
            : $"{unread.Count} unread";
    }
}
