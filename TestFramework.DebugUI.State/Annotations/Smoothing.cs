using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TestFramework.DebugUI.State.Annotations;

/// <summary>
/// Turns what a hand actually did into a line worth looking at.
/// </summary>
/// <remarks>
/// <para>
/// A pointer reports a position every few milliseconds, so a stroke drawn slowly arrives as hundreds of points a
/// pixel apart with the tremor of the hand in every one of them. Drawn as straight segments that reads as a
/// jagged mess, and stored it is kilobytes of noise per stroke.
/// </para>
/// <para>
/// Two steps, in this order. First throw away the points that say nothing — Ramer–Douglas–Peucker keeps only
/// those that carry the shape, which removes both the noise and the bulk. Then curve what is left: Catmull-Rom
/// through the surviving points, converted to the cubic Béziers a renderer can draw, so the line passes exactly
/// through the points a person aimed at while arriving at each of them smoothly.
/// </para>
/// <para>
/// Both steps are here rather than in the drawing code because they are arithmetic, and arithmetic that decides
/// what a drawing looks like is worth being able to test without a window.
/// </para>
/// </remarks>
public static class Smoothing
{
    /// <summary>
    /// How far a point may sit from the line between its neighbours before it is worth keeping.
    /// </summary>
    /// <remarks>
    /// In board units. Small enough that a deliberate wobble survives, large enough that the hand's own tremor
    /// does not.
    /// </remarks>
    public const double DefaultTolerance = 1.6;

    /// <summary>
    /// Drops the points that do not change the shape.
    /// </summary>
    /// <remarks>
    /// Ramer–Douglas–Peucker: keep the two ends, find the point furthest from the line between them, and if it is
    /// further than the tolerance keep it and repeat either side. Iterative rather than recursive — a stroke can
    /// be thousands of points long and a drawing must not be able to overflow the stack.
    /// </remarks>
    public static ImmutableList<AnnotationPoint> Simplify(
        IReadOnlyList<AnnotationPoint> points,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count <= 2)
            return [.. points];

        bool[] keep = new bool[points.Count];

        keep[0] = true;
        keep[^1] = true;

        Stack<(int From, int To)> spans = new();

        spans.Push((0, points.Count - 1));

        while (spans.Count > 0)
        {
            (int from, int to) = spans.Pop();

            if (to <= from + 1)
                continue;

            double worst = -1;
            int at = -1;

            for (int index = from + 1; index < to; index++)
            {
                double distance = DistanceToSegment(points[index], points[from], points[to]);

                if (distance > worst)
                {
                    worst = distance;
                    at = index;
                }
            }

            if (worst <= tolerance || at < 0)
                continue;

            keep[at] = true;

            spans.Push((from, at));
            spans.Push((at, to));
        }

        List<AnnotationPoint> kept = [];

        for (int index = 0; index < points.Count; index++)
        {
            if (keep[index])
                kept.Add(points[index]);
        }

        return [.. kept];
    }

    /// <summary>
    /// The cubic Bézier segments that draw a smooth line through every one of the given points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Catmull-Rom, expressed as Béziers because that is what renderers draw. The curve is interpolating rather
    /// than approximating: it goes <em>through</em> the points, which matters here because the points are where
    /// somebody's hand was and a curve that merely approaches them redraws their intent.
    /// </para>
    /// <para>
    /// The ends are duplicated so the first and last segments have the neighbour they need, which is what stops a
    /// stroke starting or finishing with a straight stub.
    /// </para>
    /// </remarks>
    public static ImmutableList<CurveSegment> Curve(IReadOnlyList<AnnotationPoint> points, double tension = 0.5)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
            return [];

        List<CurveSegment> segments = [];

        for (int index = 0; index < points.Count - 1; index++)
        {
            AnnotationPoint before = points[Math.Max(index - 1, 0)];
            AnnotationPoint start = points[index];
            AnnotationPoint end = points[index + 1];
            AnnotationPoint after = points[Math.Min(index + 2, points.Count - 1)];

            AnnotationPoint first = new(
                start.X + ((end.X - before.X) * tension / 3),
                start.Y + ((end.Y - before.Y) * tension / 3));

            AnnotationPoint second = new(
                end.X - ((after.X - start.X) * tension / 3),
                end.Y - ((after.Y - start.Y) * tension / 3));

            segments.Add(new CurveSegment(first, second, end));
        }

        return [.. segments];
    }

    /// <summary>
    /// How far a point is from the segment between two others.
    /// </summary>
    /// <remarks>
    /// From the segment, not from the infinite line through it. A stroke that doubles back has points whose
    /// nearest place on the line is beyond an end, and measuring to the line would keep them for a shape they do
    /// not contribute to.
    /// </remarks>
    private static double DistanceToSegment(AnnotationPoint point, AnnotationPoint from, AnnotationPoint to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= double.Epsilon)
            return Distance(point, from);

        double along = (((point.X - from.X) * dx) + ((point.Y - from.Y) * dy)) / lengthSquared;

        along = Math.Clamp(along, 0, 1);

        AnnotationPoint nearest = new(from.X + (along * dx), from.Y + (along * dy));

        return Distance(point, nearest);
    }

    private static double Distance(AnnotationPoint a, AnnotationPoint b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}

/// <summary>One cubic Bézier hop, from wherever the line already is.</summary>
/// <param name="FirstControl">The control point leaving the previous point.</param>
/// <param name="SecondControl">The control point arriving at <paramref name="End"/>.</param>
/// <param name="End">The point the segment reaches, which is one the hand actually visited.</param>
public readonly record struct CurveSegment(AnnotationPoint FirstControl, AnnotationPoint SecondControl, AnnotationPoint End);
