using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers laying a run out as a board.
/// </summary>
public sealed class RunBoardLayoutTests
{
    private static readonly LayoutOptions Options = LayoutOptions.Default;

    [Fact]
    public void ARunWithNoStepsLaysOutToNothing()
    {
        Assert.Same(LayoutResult.Empty, RunBoardLayout.Compute(RunGraph.Empty));
    }

    [Fact]
    public void StepsThatRanTogetherAreDrawnSideBySide()
    {
        // The reason the layer is carried through the protocol at all: a reader should see what ran
        // at the same time without being told.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0),
            GraphBuilder.Step(1, layer: 0))));

        LayoutNode[] steps = [.. Steps(board)];

        Assert.Equal(2, steps.Length);
        Assert.Equal(steps[0].Y, steps[1].Y);
        Assert.NotEqual(steps[0].X, steps[1].X);
        Assert.True(DoNotOverlap(steps[0], steps[1]), "Steps in one layer must not overlap.");
    }

    [Fact]
    public void StepsThatWaitedForEachOtherAreDrawnOneBelowTheOther()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Chain("Main", 3)));

        double[] tops = [.. Steps(board).OrderBy(step => step.StepId).Select(step => step.Y)];

        Assert.Equal(3, tops.Length);
        Assert.True(tops[0] < tops[1] && tops[1] < tops[2], "Each layer must sit below the one before it.");
    }

    [Fact]
    public void ARowIsCentredOnTheSameAxisAsEveryOtherRow()
    {
        // Otherwise a parallel layer shunts the flow sideways and the board reads as a ladder.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0),
            GraphBuilder.Step(1, layer: 1),
            GraphBuilder.Step(2, layer: 1))));

        LayoutNode single = Steps(board).Single(step => step.StepId == 0);
        LayoutNode[] pair = [.. Steps(board).Where(step => step.StepId != 0)];

        double pairCentre = (pair.Min(step => step.X) + pair.Max(step => step.Right)) / 2;

        Assert.Equal(single.CentreX, pairCentre, precision: 6);
    }

    [Fact]
    public void EveryPipeSegmentIsAxisAlignedAndOnTheGrid()
    {
        // The property that keeps parallel pipes parallel. Cheap to assert and the thing most likely
        // to be broken by a later change to the routing.
        LayoutResult board = RunBoardLayout.Compute(Tangled());

        Assert.NotEmpty(board.Edges);

        foreach (LayoutEdge edge in board.Edges)
        {
            Assert.True(edge.Points.Count >= 2, $"Pipe '{edge.Id}' has no segments.");

            foreach (LayoutPoint point in edge.Points)
            {
                Assert.True(IsOnGrid(point.X), $"Pipe '{edge.Id}' has x={point.X} off the grid.");
                Assert.True(IsOnGrid(point.Y), $"Pipe '{edge.Id}' has y={point.Y} off the grid.");
            }

            for (int index = 1; index < edge.Points.Count; index++)
            {
                LayoutPoint from = edge.Points[index - 1];
                LayoutPoint to = edge.Points[index];

                bool axisAligned = Math.Abs(from.X - to.X) < 1e-9 || Math.Abs(from.Y - to.Y) < 1e-9;
                Assert.True(axisAligned, $"Pipe '{edge.Id}' has a diagonal segment from ({from.X},{from.Y}) to ({to.X},{to.Y}).");
            }
        }
    }

    [Fact]
    public void AValueGoingToTheVeryNextRowRunsStraightDown()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["answer"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["answer"]))));

        LayoutEdge consumption = board.Edges.Single(edge => edge.Kind == LayoutEdgeKind.Consumption);

        Assert.Equal(0, consumption.Lane);
    }

    [Fact]
    public void AValueGoingFurtherDownIsRoutedIntoALaneClearOfEveryBox()
    {
        // The behaviour the right-hand channel exists for: a pipe that skips rows leaves the column
        // instead of crossing the steps in between.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["answer"]),
            GraphBuilder.Step(1, layer: 1),
            GraphBuilder.Step(2, layer: 2),
            GraphBuilder.Step(3, layer: 3, inputs: ["answer"]))));

        LayoutEdge consumption = board.Edges.Single(edge => edge.Kind == LayoutEdgeKind.Consumption);

        Assert.True(consumption.Lane > 0, "A pipe that skips rows should be routed.");

        double rightmostBox = board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage).Max(node => node.Right);
        double laneX = consumption.Points.Max(point => point.X);

        Assert.True(laneX > rightmostBox, "The routing lane must sit clear of the boxes.");
        Assert.True(board.Width > laneX, "The board must be wide enough to contain its lanes.");
    }

    [Fact]
    public void PipesThatWouldCrossGetLanesOfTheirOwn()
    {
        // Two long pipes overlapping in height cannot share a lane without being drawn on top of
        // each other.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["first"]),
            GraphBuilder.Step(1, layer: 1, outputs: ["second"]),
            GraphBuilder.Step(2, layer: 2),
            GraphBuilder.Step(3, layer: 3, inputs: ["first"]),
            GraphBuilder.Step(4, layer: 4, inputs: ["second"]))));

        LayoutEdge[] routes = [.. board.Edges.Where(edge => edge.Lane > 0)];

        Assert.Equal(2, routes.Length);
        Assert.NotEqual(routes[0].Lane, routes[1].Lane);
    }

    [Fact]
    public void EveryRoutedPipeGetsALaneOfItsOwn()
    {
        // Lanes are not shared, even by pipes whose heights do not overlap. A lane is a vertical
        // line, and two pipes on one would be free to run along each other down it — the exact
        // failure the routing rules exist to prevent. Sharing would save width; it would cost the
        // property that makes the board readable, so it is not taken.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["first"]),
            GraphBuilder.Step(1, layer: 1),
            GraphBuilder.Step(2, layer: 2, inputs: ["first"], outputs: ["second"]),
            GraphBuilder.Step(3, layer: 3),
            GraphBuilder.Step(4, layer: 4, inputs: ["second"]))));

        int[] lanes = [.. board.Edges.Where(edge => edge.Lane > 0).Select(edge => edge.Lane)];

        Assert.Equal(2, lanes.Length);
        Assert.Equal(lanes.Length, lanes.Distinct().Count());
    }

    [Fact]
    public void NothingIsDrawnOnAPipeItself()
    {
        // A value used to hang under the step that produced it, labelled. On a run with more than a
        // handful of variables and artifacts that is a wall of little boxes competing with the steps.
        // The connectors carry which value moved; the pipe just moves it.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["a", "b", "c"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["a", "b", "c"]))));

        // Only the steps themselves and the band around them. What moved between them is said by
        // the connectors and the pipe, not by a box sitting on the pipe.
        Assert.All(board.Nodes, node => Assert.True(
            node.Kind is LayoutNodeKind.Step or LayoutNodeKind.Stage,
            $"'{node.Id}' is drawn on the board as a {node.Kind}."));
    }

    [Fact]
    public void AStepProducingAValueIsConnectedToWhateverConsumesIt()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["answer"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["answer"]))));

        LayoutEdge consumption = board.Edges.Single();

        Assert.Equal("answer", consumption.Key);
        Assert.Equal("step:Main/0", consumption.FromNodeId);
        Assert.Equal("step:Main/1", consumption.ToNodeId);
    }

    [Fact]
    public void AProducedValueNobodyConsumesDrawsNoPipe()
    {
        // Without a value box there is nothing for it to flow into. The output connector still says
        // the step produced it, which is the part worth keeping.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["answer"]))));

        Assert.Empty(board.Edges);
    }

    [Fact]
    public void AnInputNoStepProducesDrawsNoPipe()
    {
        // A variable registered on the timeline rather than produced by a step. There is nothing on
        // the board to draw from, and inventing an origin would be a lie about where it came from.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, inputs: ["registered-up-front"]))));

        Assert.Empty(board.Edges);
    }

    [Fact]
    public void StepsSharingALayerAreNotConnectedToEachOther()
    {
        // They ran at the same time, so one cannot have consumed the other's output. Drawing that
        // edge would assert an ordering the run never had.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["answer"]),
            GraphBuilder.Step(1, layer: 0, inputs: ["answer"]))));

        Assert.DoesNotContain(board.Edges, edge => edge.Kind == LayoutEdgeKind.Consumption);
    }

    [Fact]
    public void AValueCrossingStagesIsStillConnected()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(
            GraphBuilder.Stage("Setup", GraphBuilder.Step(0, layer: 0, outputs: ["answer"])),
            GraphBuilder.Stage("Main", GraphBuilder.Step(0, layer: 0, inputs: ["answer"]))));

        LayoutEdge consumption = board.Edges.Single(edge => edge.Kind == LayoutEdgeKind.Consumption);

        Assert.Equal("step:Setup/0", consumption.FromNodeId);
        Assert.Equal("step:Main/0", consumption.ToNodeId);
    }

    [Fact]
    public void EachStageBandEnclosesItsOwnStepsAndNoOthers()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(
            GraphBuilder.Stage("Setup", GraphBuilder.Step(0, layer: 0)),
            GraphBuilder.Stage("Main", GraphBuilder.Step(0, layer: 0))));

        LayoutNode[] bands = [.. board.Nodes.Where(node => node.Kind == LayoutNodeKind.Stage)];
        Assert.Equal(2, bands.Length);

        foreach (LayoutNode band in bands)
        {
            foreach (LayoutNode step in Steps(board).Where(step => step.StageName == band.StageName))
            {
                Assert.True(step.X >= band.X && step.Right <= band.Right, "A band must enclose its steps horizontally.");
                Assert.True(step.Y >= band.Y && step.Bottom <= band.Bottom, "A band must enclose its steps vertically.");
            }
        }

        Assert.True(DoNotOverlap(bands[0], bands[1]), "Stage bands must not overlap.");
    }

    [Fact]
    public void BandsAreListedBeforeTheBoxesTheyEnclose()
    {
        // So a view can draw the result in order and have the band land behind its contents.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Chain("Main", 2)));

        int lastBand = board.Nodes.FindLastIndex(node => node.Kind == LayoutNodeKind.Stage);
        int firstStep = board.Nodes.FindIndex(node => node.Kind == LayoutNodeKind.Step);

        Assert.True(lastBand < firstStep, "Stage bands should come first.");
    }

    [Fact]
    public void TheSameRunAlwaysLaysOutTheSameWay()
    {
        // A pure function of the run and the measurements. Anything else would make the board move
        // under the reader as events arrive that changed nothing they can see.
        RunGraph graph = Tangled();

        Assert.Equal(RunBoardLayout.Compute(graph), RunBoardLayout.Compute(graph));
    }

    [Fact]
    public void TwoBoardsWithDifferentContentsAreNotEqual()
    {
        // Guards the other half of the comparison: equality that always says yes would be just as
        // useless as one that always says no.
        LayoutResult two = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Chain("Main", 2)));
        LayoutResult three = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Chain("Main", 3)));

        Assert.NotEqual(two, three);
    }

    [Fact]
    public void EveryBoxIsInsideTheReportedBoard()
    {
        LayoutResult board = RunBoardLayout.Compute(Tangled());

        Assert.All(board.Nodes, node =>
        {
            Assert.True(node.X >= 0 && node.Y >= 0, "A box must not sit off the top or left of the board.");
            Assert.True(node.Right <= board.Width, "A box must not overflow the board's width.");
            Assert.True(node.Bottom <= board.Height, "A box must not overflow the board's height.");
        });
    }

    [Fact]
    public void MeasurementsAreHonoured()
    {
        // The options are the whole styling surface, so a board built with different ones must
        // actually differ.
        LayoutOptions wide = LayoutOptions.Default with { StepWidth = 700 };

        LayoutResult standard = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Chain("Main", 2)));
        LayoutResult widened = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Chain("Main", 2)), wide);

        Assert.Equal(LayoutOptions.Default.StepWidth, Steps(standard).First().Width);
        Assert.Equal(700, Steps(widened).First().Width);
    }

    private static RunGraph Tangled() => GraphBuilder.Run(
        GraphBuilder.Stage(
            "Setup",
            GraphBuilder.Step(0, layer: 0, outputs: ["config", "token"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["config"], outputs: ["client"])),
        GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, inputs: ["client"], outputs: ["row"]),
            GraphBuilder.Step(1, layer: 1),
            GraphBuilder.Step(2, layer: 1),
            GraphBuilder.Step(3, layer: 2, inputs: ["row", "token"])),
        GraphBuilder.Stage(
            "Cleanup",
            GraphBuilder.Step(0, layer: 0, inputs: ["client"])));

    private static IEnumerable<LayoutNode> Steps(LayoutResult board)
        => board.Nodes.Where(node => node.Kind == LayoutNodeKind.Step);

    private static bool IsOnGrid(double value)
        => Math.Abs(value - (Math.Round(value / Options.Grid) * Options.Grid)) < 1e-9;

    private static bool DoNotOverlap(LayoutNode left, LayoutNode right)
        => left.Right <= right.X || right.Right <= left.X || left.Bottom <= right.Y || right.Bottom <= left.Y;

    private static IEnumerable<(LayoutNode Left, LayoutNode Right)> Pairs(IReadOnlyList<LayoutNode> nodes)
    {
        for (int outer = 0; outer < nodes.Count; outer++)
        {
            for (int inner = outer + 1; inner < nodes.Count; inner++)
                yield return (nodes[outer], nodes[inner]);
        }
    }
}
