using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TestFramework.DebugUI;

/// <summary>
/// A row of a list that can be picked, and lights under the pointer.
/// </summary>
/// <remarks>
/// <para>
/// The chrome only — the wash, the corner, the hand, the hover, the click. What goes inside it is each
/// list's own business, and none of them agree about that: a rail row is a health edge, a name and a
/// tally, and a tree row is a chevron and a name.
/// </para>
/// <para>
/// One rule for the part they do share, because the hover has a rule that is easy to get subtly wrong —
/// a selected row keeps its wash and must not be repainted transparent when the pointer leaves it.
/// </para>
/// </remarks>
internal static class InteractiveRow
{
    /// <summary>How far a row's content sits from its edges.</summary>
    private static readonly Thickness Padding = new(6, 5, 8, 5);

    /// <summary>The gap under a row, which is what separates one from the next without a line.</summary>
    private const double Gap = 2;

    /// <summary>
    /// Wraps content as a row that can be picked.
    /// </summary>
    /// <param name="owner">Whose resources the colours are read from.</param>
    /// <param name="content">What the row shows.</param>
    /// <param name="indent">How far in the row starts, which is what shows the tree's depth.</param>
    /// <param name="selected">Whether this is the row currently being read.</param>
    /// <param name="tip">What the row says on hover, when there is more to say than fits.</param>
    /// <param name="pick">What picking it does.</param>
    public static Border Wrap(FrameworkElement owner, UIElement content, double indent, bool selected, string? tip, Action pick)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(pick);

        Brush raised = (Brush)owner.FindResource("SurfaceRaised");

        Border row = new()
        {
            CornerRadius = new CornerRadius(4),
            Background = selected ? raised : Brushes.Transparent,
            Padding = Padding,
            Margin = new Thickness(indent, 0, 0, Gap),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content
        };

        row.MouseLeftButtonUp += (_, _) => pick();

        // A selected row is already lit and stays lit, so only an unselected one follows the pointer.
        row.MouseEnter += (_, _) =>
        {
            if (!selected)
                row.Background = raised;
        };

        row.MouseLeave += (_, _) =>
        {
            if (!selected)
                row.Background = Brushes.Transparent;
        };

        return row;
    }
}
