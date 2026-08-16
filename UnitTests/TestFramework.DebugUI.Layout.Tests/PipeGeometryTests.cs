using System;
using System.Collections.Generic;
using System.Linq;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers rounding the corners of a pipe.
/// </summary>
/// <remarks>
/// The arithmetic is small and entirely about the awkward cases: a corner asked for more room than
/// the segment has, two corners at the ends of one short run, a line with no corner at all. Left in
/// the view these would be found by looking at a crowded board and noticing something is wrong.
/// </remarks>
public sealed class PipeGeometryTests
{
    [Fact]
    public void AStraightRunIsOneLine()
    {
        PipeFigure figure = PipeGeometry.Fillet([new LayoutPoint(0, 0), new LayoutPoint(0, 100)], radius: 10);

        Assert.Equal(new LayoutPoint(0, 0), figure.Start);
        PipeSegment segment = Assert.Single(figure.Segments);
        Assert.False(segment.IsCorner);
        Assert.Equal(new LayoutPoint(0, 100), segment.To);
    }

    [Fact]
    public void ACornerBecomesALineAnArcAndALine()
    {
        PipeFigure figure = PipeGeometry.Fillet(
            [new LayoutPoint(0, 0), new LayoutPoint(0, 100), new LayoutPoint(100, 100)],
            radius: 10);

        Assert.Equal(3, figure.Segments.Count);
        Assert.False(figure.Segments[0].IsCorner);
        Assert.True(figure.Segments[1].IsCorner);
        Assert.False(figure.Segments[2].IsCorner);

        // The arc starts where the straight run stops short of the corner and ends the same distance
        // along the next one.
        Assert.Equal(new LayoutPoint(0, 90), figure.Segments[0].To);
        Assert.Equal(new LayoutPoint(10, 100), figure.Segments[1].To);
        Assert.Equal(new LayoutPoint(100, 100), figure.Segments[2].To);
    }

    [Fact]
    public void AShortSegmentTightensTheCornerRatherThanOvershootingIt()
    {
        // The case that matters on a crowded board. A 10-unit run cannot give 20 units to a corner,
        // and taking it anyway would send the arc past the corner and back on itself.
        PipeFigure figure = PipeGeometry.Fillet(
            [new LayoutPoint(0, 0), new LayoutPoint(0, 10), new LayoutPoint(100, 10)],
            radius: 20);

        PipeSegment corner = figure.Segments.Single(segment => segment.IsCorner);

        Assert.Equal(5, corner.Radius);
        Assert.All(AllPoints(figure), point => Assert.True(point.Y >= 0 && point.Y <= 10, "The corner overshot the segment it was drawn on."));
    }

    [Fact]
    public void TwoCornersOnOneShortRunDoNotEatIntoEachOther()
    {
        // Each corner may take half the run, so back-to-back corners meet in the middle at worst.
        PipeFigure figure = PipeGeometry.Fillet(
            [
                new LayoutPoint(0, 0),
                new LayoutPoint(0, 40),
                new LayoutPoint(60, 40),
                new LayoutPoint(60, 80)
            ],
            radius: 100);

        PipeSegment[] corners = [.. figure.Segments.Where(segment => segment.IsCorner)];

        Assert.Equal(2, corners.Length);
        Assert.True(corners.Sum(corner => corner.Radius) <= 60, "The two corners together took more than the run between them.");

        // Monotonic along the middle run means the second corner never starts before the first ends.
        double[] xs = [.. AllPoints(figure).Select(point => point.X)];
        Assert.Equal(xs.OrderBy(x => x), xs);
    }

    [Fact]
    public void TheTurnDirectionMatchesHowItIsDrawn()
    {
        // Screen coordinates run downwards, so "right then down" is a clockwise turn even though the
        // same numbers read as anticlockwise on a graph. Getting this backwards bulges every corner
        // the wrong way.
        PipeFigure clockwise = PipeGeometry.Fillet(
            [new LayoutPoint(0, 0), new LayoutPoint(100, 0), new LayoutPoint(100, 100)],
            radius: 10);

        PipeFigure anticlockwise = PipeGeometry.Fillet(
            [new LayoutPoint(0, 0), new LayoutPoint(100, 0), new LayoutPoint(100, -100)],
            radius: 10);

        Assert.True(clockwise.Segments.Single(segment => segment.IsCorner).Clockwise);
        Assert.False(anticlockwise.Segments.Single(segment => segment.IsCorner).Clockwise);
    }

    [Fact]
    public void APointThatDoesNotTurnIsNotACorner()
    {
        PipeFigure figure = PipeGeometry.Fillet(
            [new LayoutPoint(0, 0), new LayoutPoint(0, 50), new LayoutPoint(0, 100)],
            radius: 10);

        Assert.DoesNotContain(figure.Segments, segment => segment.IsCorner);
        Assert.Equal(new LayoutPoint(0, 100), figure.Segments[^1].To);
    }

    [Fact]
    public void ARadiusOfZeroLeavesTheCornersSquare()
    {
        PipeFigure figure = PipeGeometry.Fillet(
            [new LayoutPoint(0, 0), new LayoutPoint(0, 100), new LayoutPoint(100, 100)],
            radius: 0);

        Assert.DoesNotContain(figure.Segments, segment => segment.IsCorner);
        Assert.Equal(2, figure.Segments.Count);
    }

    [Fact]
    public void APipeWithNothingToDrawIsEmpty()
    {
        Assert.Empty(PipeGeometry.Fillet([], radius: 10).Segments);
        Assert.Empty(PipeGeometry.Fillet([new LayoutPoint(5, 5)], radius: 10).Segments);
    }

    [Fact]
    public void EveryLaidOutPipeCanBeRounded()
    {
        // The two halves have to agree: whatever the engine emits, this has to be able to draw.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(
            GraphBuilder.Stage(
                "Main",
                GraphBuilder.Step(0, layer: 0, outputs: ["a"]),
                GraphBuilder.Step(1, layer: 1),
                GraphBuilder.Step(2, layer: 2, inputs: ["a"]))));

        Assert.NotEmpty(board.Edges);

        foreach (LayoutEdge edge in board.Edges)
        {
            PipeFigure figure = PipeGeometry.Fillet(edge.Points, radius: 8);

            Assert.NotEmpty(figure.Segments);
            Assert.Equal(edge.Points[0], figure.Start);
            Assert.Equal(edge.Points[^1], figure.Segments[^1].To);
        }
    }

    private static IEnumerable<LayoutPoint> AllPoints(PipeFigure figure)
        => new[] { figure.Start }.Concat(figure.Segments.Select(segment => segment.To));
}
