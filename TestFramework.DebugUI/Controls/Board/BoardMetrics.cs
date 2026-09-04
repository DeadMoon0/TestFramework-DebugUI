using TestFramework.DebugUI.Layout;

namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// The measurements a board is drawn to.
/// </summary>
/// <remarks>
/// Shared rather than repeated, because building a card and repainting one are now two different
/// classes and they have to agree: a marker drawn at one inset and cleared at another would leave a
/// smear on the card it was meant to be on.
/// </remarks>
internal static class BoardMetrics
{
    public const double CornerRadius = 10;

    /// <summary>The diameter of a connector, matching the ring the pipes were always drawn with.</summary>
    public const double ConnectorSize = 20;

    /// <summary>How deep the recessed strip a connector sits on runs into the card.</summary>
    public const double StripHeight = 16;

    /// <summary>The gap between a card's writing and the picture under it.</summary>
    public const double WidgetMargin = 8;

    /// <summary>
    /// How tall the widget area inside a card is drawn.
    /// </summary>
    /// <remarks>
    /// The extra height a card is given for having something to show, less the gap above it - so the
    /// picture is exactly the room the layout granted and no more. Derived rather than stated,
    /// because the two were separate numbers meaning one thing: shrinking the card without shrinking
    /// this would push the picture through the bottom of the card it lives in.
    /// </remarks>
    public static readonly double WidgetHeight =
        LayoutOptions.Default.StepHeightWithWidget - LayoutOptions.Default.StepHeight - WidgetMargin;

    /// <summary>The inset of a card's content from its edge.</summary>
    public const double CardPadding = 16;

    /// <summary>The breakpoint marker's width, and how far in from the card's edge it sits.</summary>
    public const double MarkerWidth = 16;
    public const double MarkerInset = 8;

    /// <summary>How much clear space is left between the marker and whatever the heading ends with.</summary>
    public const double MarkerGap = 6;

    /// <summary>
    /// The column a card's heading gives up to the breakpoint marker.
    /// </summary>
    /// <remarks>
    /// Derived rather than chosen, because the marker is not in that grid and cannot push back: it
    /// hangs in the card's own host so that dimming an unrun card cannot dim it. That makes the
    /// heading the only side able to leave room, and a number picked by eye would drift the moment
    /// the marker moved. The card's padding already covers part of the marker's reach; this is the
    /// rest of it, plus the gap.
    /// </remarks>
    public const double MarkerColumn = MarkerWidth + MarkerInset - CardPadding + MarkerGap;

    /// <summary>
    /// How strongly something that has not happened yet is drawn.
    /// </summary>
    /// <remarks>
    /// Low enough to fall back behind the run, high enough to still be readable - the declared shape
    /// of a timeline is worth seeing, it just should not compete with what is actually happening.
    /// </remarks>
    public const double DormantOpacity = 0.4;

    /// <summary>How strongly a breakpoint marker that is merely available is drawn.</summary>
    public const double RestingMarkerOpacity = 0.25;
}
