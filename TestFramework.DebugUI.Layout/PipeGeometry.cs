using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TestFramework.DebugUI.Layout;

/// <summary>
/// One piece of a drawn pipe: a straight run, or a rounded corner.
/// </summary>
/// <remarks>
/// Deliberately not a WPF type, though it maps onto one line for line and one for arc. Keeping the
/// clamping arithmetic here is what lets it be tested directly, rather than being noticed only when
/// a corner overshoots on screen.
/// </remarks>
public readonly record struct PipeSegment
{
    /// <summary>Gets the point this segment ends at.</summary>
    public required LayoutPoint To { get; init; }

    /// <summary>Gets the corner radius, or zero when the segment is straight.</summary>
    public double Radius { get; init; }

    /// <summary>Gets a value indicating whether a corner turns clockwise on screen.</summary>
    public bool Clockwise { get; init; }

    /// <summary>Gets a value indicating whether this segment is a rounded corner.</summary>
    public bool IsCorner => Radius > 0;
}

/// <summary>A pipe ready to be drawn: where it starts, and what it does from there.</summary>
public sealed record PipeFigure
{
    /// <summary>An empty figure, for a pipe with nothing to draw.</summary>
    public static PipeFigure Empty { get; } = new() { Start = default, Segments = ImmutableList<PipeSegment>.Empty };

    /// <summary>Gets the starting point.</summary>
    public required LayoutPoint Start { get; init; }

    /// <summary>Gets the segments, in order.</summary>
    public required ImmutableList<PipeSegment> Segments { get; init; }
}

/// <summary>
/// Rounds the corners of a laid-out pipe.
/// </summary>
/// <remarks>
/// The layout emits square corners on the grid because that is what keeps pipes parallel and
/// predictable; this is what makes them look like pipework rather than a schematic. Kept apart from
/// the layout because a corner radius is an appearance, and a board should be able to change how
/// round its corners are without changing where anything is.
/// </remarks>
public static class PipeGeometry
{
    /// <summary>
    /// Builds a drawable figure from an axis-aligned polyline.
    /// </summary>
    /// <param name="points">The polyline, from source to target.</param>
    /// <param name="radius">The corner radius asked for. Each corner may use less.</param>
    /// <remarks>
    /// A corner never takes more than half of either segment it joins, so two corners at the ends of
    /// one short run cannot eat past each other. That is what turns a tight corner into a tighter
    /// curve instead of an arc that overshoots the segment and doubles back — which is what the
    /// naive version does exactly where a board is most crowded.
    /// </remarks>
    public static PipeFigure Fillet(IReadOnlyList<LayoutPoint> points, double radius)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
            return PipeFigure.Empty;

        ImmutableList<PipeSegment>.Builder segments = ImmutableList.CreateBuilder<PipeSegment>();
        LayoutPoint cursor = points[0];

        for (int index = 1; index < points.Count - 1; index++)
        {
            LayoutPoint corner = points[index];
            LayoutPoint next = points[index + 1];

            double incoming = Distance(cursor, corner);
            double outgoing = Distance(corner, next);

            // Measured from the cursor, not from the previous polyline point: the run available to
            // this corner is what the corner before it left behind.
            double effective = Math.Min(radius, Math.Min(incoming, outgoing) / 2);

            if (effective <= 0 || !Turns(cursor, corner, next))
            {
                segments.Add(Line(corner));
                cursor = corner;
                continue;
            }

            LayoutPoint entry = Along(corner, cursor, effective);
            LayoutPoint exit = Along(corner, next, effective);

            if (entry != cursor)
                segments.Add(Line(entry));

            segments.Add(new PipeSegment
            {
                To = exit,
                Radius = effective,
                Clockwise = IsClockwise(cursor, corner, next)
            });

            cursor = exit;
        }

        LayoutPoint end = points[^1];
        if (end != cursor)
            segments.Add(Line(end));

        return new PipeFigure { Start = points[0], Segments = segments.ToImmutable() };
    }

    private static PipeSegment Line(LayoutPoint to) => new() { To = to };

    private static double Distance(LayoutPoint from, LayoutPoint to)
        => Math.Sqrt(((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));

    /// <summary>Moves from a corner towards a neighbour by a distance.</summary>
    private static LayoutPoint Along(LayoutPoint corner, LayoutPoint towards, double distance)
    {
        double length = Distance(corner, towards);
        if (length <= 0)
            return corner;

        double ratio = distance / length;

        return new LayoutPoint(
            corner.X + ((towards.X - corner.X) * ratio),
            corner.Y + ((towards.Y - corner.Y) * ratio));
    }

    private static bool Turns(LayoutPoint before, LayoutPoint corner, LayoutPoint after)
        => Math.Abs(Cross(before, corner, after)) > 1e-9;

    /// <summary>
    /// Reports whether the turn is clockwise as drawn.
    /// </summary>
    /// <remarks>
    /// Screen coordinates run downwards, so a positive cross product is a clockwise turn — right
    /// then down looks clockwise even though the same numbers would be anticlockwise on a graph.
    /// </remarks>
    private static bool IsClockwise(LayoutPoint before, LayoutPoint corner, LayoutPoint after)
        => Cross(before, corner, after) > 0;

    private static double Cross(LayoutPoint before, LayoutPoint corner, LayoutPoint after)
    {
        double firstX = corner.X - before.X;
        double firstY = corner.Y - before.Y;
        double secondX = after.X - corner.X;
        double secondY = after.Y - corner.Y;

        return (firstX * secondY) - (firstY * secondX);
    }
}
