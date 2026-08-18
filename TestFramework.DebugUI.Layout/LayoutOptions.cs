using System;

namespace TestFramework.DebugUI.Layout;

/// <summary>
/// The measurements a board is laid out with.
/// </summary>
/// <remarks>
/// Every default is a multiple of <see cref="Grid"/>. That is not tidiness: the engine snaps every
/// coordinate to the grid, so sizes that are not multiples make nodes drift off the spacing they
/// were given and parallel pipes stop looking parallel.
/// </remarks>
public sealed record LayoutOptions
{
    /// <summary>The default measurements.</summary>
    public static LayoutOptions Default { get; } = new();

    /// <summary>Gets the spacing every coordinate is snapped to.</summary>
    /// <summary>
    /// The version of the arrangement these options and the routing produce.
    /// </summary>
    /// <remarks>
    /// Bumped whenever a change moves where things end up for the same run — card sizes, routing, connector
    /// placement. Annotations are stored in board coordinates and are only meaningful against the arrangement
    /// they were drawn on, so this is what lets a later build say "the board has moved under these marks"
    /// instead of quietly pointing an arrow at the wrong step.
    /// </remarks>
    public const int Version = 1;

    public double Grid { get; init; } = 10;

    /// <summary>
    /// Gets the width of a step box.
    /// </summary>
    /// <remarks>
    /// Deliberately large. A step is the thing a reader is actually looking at, and it has to hold a
    /// name, a status, a result and its outputs without any of them being abbreviated into
    /// uselessness — a board of small boxes says only that something happened, in order.
    /// </remarks>
    public double StepWidth { get; init; } = 460;

    /// <summary>
    /// Gets the height of a step box.
    /// </summary>
    /// <remarks>
    /// Sized to what a card actually holds. A taller card is not a more informative one: the empty
    /// space just makes the board taller, which makes the fitted view smaller, which is what makes
    /// the writing on the cards unreadable.
    /// </remarks>
    public double StepHeight { get; init; } = 130;

    /// <summary>
    /// Gets the horizontal gap between steps sharing a layer.
    /// </summary>
    /// <remarks>
    /// Chosen so that a step plus a gap is a round 500: every row's half-width then lands on the
    /// grid, and rows of one and of three steps centre on exactly the same axis. With an odd pitch
    /// the halves alternate on and off the grid, and successive rows sit five pixels apart — not
    /// enough to look deliberate, just enough to look wrong.
    /// </remarks>
    public double StepGap { get; init; } = 40;

    /// <summary>Gets the vertical gap between one layer's row and the next.</summary>
    public double LayerGap { get; init; } = 50;

    /// <summary>
    /// Gets the distance between one connector and the next along an edge.
    /// </summary>
    /// <remarks>
    /// The connectors are their own row, packed tight against the corner of the card, so a card with
    /// many of them still reads as a row of sockets rather than as a spread-out edge.
    /// </remarks>
    public double ConnectorPitch { get; init; } = 30;

    /// <summary>
    /// Gets the inset of the first connector from the left edge of a box.
    /// </summary>
    /// <remarks>
    /// Connectors run left to right from a fixed inset rather than spreading to fill the edge, so a
    /// step with two outputs and a step with five start their connectors in the same place. Spread
    /// connectors move every time the count changes, which makes two neighbouring cards look
    /// unrelated when they are not.
    /// </remarks>
    public double ConnectorInset { get; init; } = 10;

    /// <summary>
    /// Gets how far a pipe runs straight out of a connector before it may turn.
    /// </summary>
    /// <remarks>
    /// A pipe that turned the moment it left a connector would read as attached to the corner of the
    /// box rather than plugged into the port. The straight run is what makes the connection legible.
    /// </remarks>
    public double PipeLead { get; init; } = 30;

    /// <summary>
    /// Gets the distance between two horizontal runs sharing a channel.
    /// </summary>
    /// <remarks>
    /// Wide enough that two pipes on neighbouring tracks read as two pipes. This is what a channel's
    /// depth is measured in, so a busy junction grows rather than crowding.
    /// </remarks>
    public double TrackSpacing { get; init; } = 20;

    /// <summary>Gets the height of a stage's header band.</summary>
    public double StageHeaderHeight { get; init; } = 40;

    /// <summary>Gets the vertical gap between one stage and the next.</summary>
    public double StageGap { get; init; } = 60;

    /// <summary>Gets how far a stage band extends beyond the boxes it encloses.</summary>
    public double StagePadding { get; init; } = 20;

    /// <summary>Gets the gap between the widest row and the first routing lane.</summary>
    public double LaneGap { get; init; } = 80;

    /// <summary>Gets the horizontal distance between adjacent routing lanes.</summary>
    public double LaneWidth { get; init; } = 40;

    /// <summary>Gets the margin left of the content.</summary>
    public double LeftMargin { get; init; } = 40;

    /// <summary>Gets the margin above the content.</summary>
    public double TopMargin { get; init; } = 40;

    /// <summary>Gets the margin right of the last routing lane.</summary>
    public double RightMargin { get; init; } = 40;

    /// <summary>Gets the margin below the content.</summary>
    public double BottomMargin { get; init; } = 40;

    /// <summary>Snaps a coordinate to the grid.</summary>
    public double Snap(double value) => Math.Round(value / Grid, MidpointRounding.AwayFromZero) * Grid;
}
