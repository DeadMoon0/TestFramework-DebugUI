using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI;

// Bringing panels out and putting them away, keeping the board clear of the pinned wells, and the drawing bar.
public partial class MainWindow
{
    /// <summary>
    /// Opens or closes the drawing instruments.
    /// </summary>
    /// <remarks>
    /// Closing hands the mouse back to the board explicitly rather than leaving a tool armed behind a hidden bar,
    /// which would leave the next click drawing a line nobody asked for.
    /// </remarks>
    private void btAnnotate_Click(object sender, RoutedEventArgs e)
    {
        if (ucAnnotate.Visibility == Visibility.Visible)
        {
            StopAnnotating();
            return;
        }

        ucAnnotate.Open();

        // The one thing a reader cannot see for themselves: these marks were drawn on a board that has since
        // been arranged differently, so they may no longer point where they were aimed.
        ucAnnotate.Note(ucBoard.AnnotationsPredateThisLayout()
            ? "These marks were drawn on an earlier layout"
            : null);

        ShowAnnotateState(annotating: true);
    }

    private void StopAnnotating()
    {
        ucBoard.SetAnnotationTool(null);
        ucAnnotate.Visibility = Visibility.Collapsed;

        ShowAnnotateState(annotating: false);
    }

    /// <summary>
    /// Lights the pen while the toolbar is open.
    /// </summary>
    /// <remarks>
    /// The accent rather than the ink being drawn with. It says the tool is armed, which is the same thing
    /// the eye beside it says about watching - and lighting one cyan and the other blue made two toggles a
    /// pixel apart look like two different kinds of switch. Which colour the pen draws in is the toolbar's
    /// business, and it is shown there.
    /// </remarks>
    private void ShowAnnotateState(bool annotating)
        => pathAnnotate.Stroke = (Brush)FindResource(annotating ? ThemeKeys.Accent : ThemeKeys.TextSecondary);

    private void ShowSummary() => Reveal(PanelId.Summary);

    private void ShowHome() => Reveal(PanelId.Home);

    /// <summary>Opens a value in the inspector, wherever the reader has put the inspector.</summary>
    /// <remarks>
    /// Two steps because they are two things: which value is the panel's own business, and whether the panel is
    /// on screen is the arrangement's. Asked for from the value rail and from a search hit alike.
    /// </remarks>
    private void OpenValue(string key, bool isArtifact)
    {
        Inspector.Show(key, isArtifact);
        Reveal(PanelId.Inspector);
    }

    /// <summary>Opens a widget in the inspector, where it can be compared with an earlier run's.</summary>
    private void OpenWidget(WidgetNode widget)
    {
        Inspector.ShowWidget(widget);
        Reveal(PanelId.Inspector);
    }

    /// <summary>Brings a panel out, at its own default place if it was put away.</summary>
    private static void Reveal(PanelId panel)
        => Arrangement.Apply(layout => layout.Reveal(panel, PanelRegistry.DefaultSideOf(panel)));

    /// <summary>Puts a panel away.</summary>
    private static void Put(PanelId panel) => Arrangement.Apply(layout => layout.Close(panel));

    /// <summary>Keeps the board out of whatever the pinned wells have reserved.</summary>
    private void ApplyBoardInsets()
        => ucBoard.Margin = reserved.Against(ucDock.ActualWidth, ucDock.ActualHeight);
}
