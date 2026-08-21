using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers the promises the routing makes about where pipes may go.
/// </summary>
/// <remarks>
/// These are the properties that decide whether a board is readable: a pipe that runs through a box
/// looks like it belongs to that box, and two pipes sharing a line look like one pipe. Both are easy
/// to produce by accident and almost impossible to notice by eye on a board with any traffic on it,
/// which is exactly why they are asserted rather than reviewed.
/// </remarks>
public sealed class PipeRoutingTests
{
    [Fact]
    public void NoPipeEverCrossesABox()
    {
        // The strongest of the three. A pipe over a step reads as attached to it.
        LayoutResult board = RunBoardLayout.Compute(Busy());

        LayoutNode[] boxes = [.. board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage)];

        foreach (LayoutEdge edge in board.Edges)
        {
            foreach (Segment segment in Segments(edge))
            {
                foreach (LayoutNode box in boxes)
                {
                    // A pipe touching the box it starts or ends at is the connection itself.
                    if (box.Id == edge.FromNodeId || box.Id == edge.ToNodeId)
                        continue;

                    Assert.False(
                        PassesThrough(segment, box),
                        $"Pipe '{edge.Id}' runs through '{box.Id}'.");
                }
            }
        }
    }

    [Fact]
    public void NoTwoPipesEverRunAlongTheSameLine()
    {
        // Two overlapping pipes are indistinguishable from one, so a value would appear to vanish.
        LayoutResult board = RunBoardLayout.Compute(Busy());

        Segment[] segments = [.. board.Edges.SelectMany(Segments)];

        for (int outer = 0; outer < segments.Length; outer++)
        {
            for (int inner = outer + 1; inner < segments.Length; inner++)
            {
                // Two pipes leaving the same connector are one pipe that forks: they share the run
                // out of the port and separate afterwards, which is how a fan-out should look.
                if (segments[outer].PortId == segments[inner].PortId)
                    continue;

                Assert.False(
                    Overlap(segments[outer], segments[inner]),
                    $"Two pipes share a line: {segments[outer]} and {segments[inner]}.");
            }
        }
    }

    [Fact]
    public void ABusyChannelGetsDeeperRatherThanCrowded()
    {
        // Channels are sized from the traffic through them. A fixed gap is what forces pipes onto
        // each other in exactly the places a board is hardest to read.
        RunGraph light = GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["a"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["a"])));

        RunGraph heavy = GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["a", "b", "c", "d"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["a", "b", "c", "d"])));

        double lightGap = GapBetweenRows(RunBoardLayout.Compute(light));
        double heavyGap = GapBetweenRows(RunBoardLayout.Compute(heavy));

        Assert.True(heavyGap >= lightGap, "A channel carrying more pipes must not be shallower than one carrying fewer.");
    }

    [Fact]
    public void AValueDropsStraightOutOfTheConnectorThatMadeIt()
    {
        // Inputs and outputs share one pitch from the same inset, so a value passed straight down
        // to the next step leaves and arrives at the same x. A straight drop cannot cross anything.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["a", "b", "c"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["a", "b", "c"]))));

        Assert.NotEmpty(board.Edges);

        foreach (LayoutEdge pipe in board.Edges)
        {
            Assert.Equal(2, pipe.Points.Count);
            Assert.Equal(pipe.Points[0].X, pipe.Points[1].X, precision: 6);
        }
    }

    [Fact]
    public void EveryPipeLeavesAndArrivesAtAConnector()
    {
        LayoutResult board = RunBoardLayout.Compute(Busy());

        Assert.NotEmpty(board.Ports);

        foreach (LayoutEdge edge in board.Edges)
        {
            LayoutPort from = board.Ports.Single(port => port.Id == edge.FromPortId);
            LayoutPort to = board.Ports.Single(port => port.Id == edge.ToPortId);

            Assert.False(from.IsInput, "A pipe must leave an output connector.");
            Assert.True(to.IsInput, "A pipe must arrive at an input connector.");

            Assert.Equal(from.X, edge.Points[0].X, precision: 6);
            Assert.Equal(from.Y, edge.Points[0].Y, precision: 6);
            Assert.Equal(to.X, edge.Points[^1].X, precision: 6);
            Assert.Equal(to.Y, edge.Points[^1].Y, precision: 6);
        }
    }

    [Fact]
    public void ConnectorsOnOneBoxAreNeverOnTopOfEachOther()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["a", "b", "c", "d"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["a", "b", "c", "d"]))));

        foreach (IGrouping<string, LayoutPort> box in board.Ports.GroupBy(port => port.NodeId, StringComparer.Ordinal))
        {
            foreach (IGrouping<bool, LayoutPort> side in box.GroupBy(port => port.IsInput))
            {
                int distinct = side.Select(port => (port.X, port.Y)).Distinct().Count();
                Assert.Equal(side.Count(), distinct);
            }
        }
    }

    [Fact]
    public void AnInputNothingProducesStillGetsAConnector()
    {
        // The step asked for something no earlier step supplies. That is worth seeing, and it is
        // what a reader is hunting when a step does not get the value it expected.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, inputs: ["registered-up-front"]))));

        LayoutPort port = Assert.Single(board.Ports);

        Assert.True(port.IsInput);
        Assert.False(port.IsConnected);
        Assert.Empty(board.Edges);
    }

    [Fact]
    public void AVariableAndAnArtifactAreToldApart()
    {
        // The two behave differently, so the board colours them differently — which it can only do
        // if the contract's kind survives all the way from Core.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["count", "row"], artifacts: ["row"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["count", "row"], artifacts: ["row"]))));

        Assert.Equal(DebugValueKind.Artifact, board.Edges.First(edge => edge.Key == "row").ValueKind);
        Assert.Equal(DebugValueKind.Variable, board.Edges.First(edge => edge.Key == "count").ValueKind);

        // The connectors carry it too: with no box on the pipe, they are where a reader sees which
        // of the two a connection is.
        Assert.Equal(DebugValueKind.Artifact, board.Ports.First(port => port.Key == "row").Kind);
        Assert.Equal(DebugValueKind.Variable, board.Ports.First(port => port.Key == "count").Kind);
    }

    /// <summary>A run with enough traffic to catch a routing mistake.</summary>
    /// <summary>
    /// A run with enough traffic to break routing, including the pipes into the verdict.
    /// </summary>
    /// <remarks>
    /// The assertions are here rather than in a case of their own so the invariants above cover the
    /// verdict's pipes too. They are the pipes most able to cross something: they come from anywhere
    /// on the board and all arrive at one box.
    /// </remarks>
    private static RunGraph Busy() => GraphBuilder.Asserting(
        Executed(),
        ("row", true),
        ("count", true),
        ("session", false));

    private static RunGraph Executed() => GraphBuilder.Run(
        GraphBuilder.Stage(
            "Setup",
            GraphBuilder.Step(0, layer: 0, outputs: ["config", "token", "client"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["config"], outputs: ["session"])),
        GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, inputs: ["session"], outputs: ["row", "count"], artifacts: ["row"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["row"], artifacts: ["row"]),
            GraphBuilder.Step(2, layer: 1, inputs: ["count"]),
            GraphBuilder.Step(3, layer: 2, inputs: ["token", "client", "count"])),
        GraphBuilder.Stage(
            "Cleanup",
            GraphBuilder.Step(0, layer: 0, inputs: ["client", "session"])));

    private static double GapBetweenRows(LayoutResult board)
    {
        LayoutNode[] steps = [.. board.Nodes.Where(node => node.Kind == LayoutNodeKind.Step).OrderBy(node => node.Y)];
        return steps[1].Y - steps[0].Bottom;
    }

    private static IEnumerable<Segment> Segments(LayoutEdge edge)
    {
        for (int index = 1; index < edge.Points.Count; index++)
            yield return new Segment(edge.Id, edge.FromPortId, edge.Points[index - 1], edge.Points[index]);
    }

    /// <summary>Whether a segment enters a box's interior rather than merely touching its edge.</summary>
    private static bool PassesThrough(Segment segment, LayoutNode box)
    {
        double left = Math.Min(segment.From.X, segment.To.X);
        double right = Math.Max(segment.From.X, segment.To.X);
        double top = Math.Min(segment.From.Y, segment.To.Y);
        double bottom = Math.Max(segment.From.Y, segment.To.Y);

        return left < box.Right && right > box.X && top < box.Bottom && bottom > box.Y;
    }

    /// <summary>Whether two segments lie on the same line and share more than a point of it.</summary>
    private static bool Overlap(Segment left, Segment right)
    {
        bool leftVertical = Math.Abs(left.From.X - left.To.X) < 1e-9;
        bool rightVertical = Math.Abs(right.From.X - right.To.X) < 1e-9;

        if (leftVertical != rightVertical)
            return false;

        if (leftVertical)
        {
            return Math.Abs(left.From.X - right.From.X) < 1e-9
                   && Spans(left.From.Y, left.To.Y, right.From.Y, right.To.Y);
        }

        return Math.Abs(left.From.Y - right.From.Y) < 1e-9
               && Spans(left.From.X, left.To.X, right.From.X, right.To.X);
    }

    private static bool Spans(double firstA, double firstB, double secondA, double secondB)
    {
        double firstLow = Math.Min(firstA, firstB);
        double firstHigh = Math.Max(firstA, firstB);
        double secondLow = Math.Min(secondA, secondB);
        double secondHigh = Math.Max(secondA, secondB);

        return Math.Min(firstHigh, secondHigh) - Math.Max(firstLow, secondLow) > 1e-9;
    }

    private readonly record struct Segment(string EdgeId, string PortId, LayoutPoint From, LayoutPoint To)
    {
        public override string ToString() => $"{EdgeId} ({From.X},{From.Y})-({To.X},{To.Y})";
    }
}
