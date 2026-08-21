using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.DebugUI.State.Board;

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

        // Clear of the boxes, on whichever side it took. Which side is a separate question, and
        // demanding the right one here is what made the layout send every pipe the long way round.
        LayoutNode[] boxes = [.. board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage)];
        double leftmostBox = boxes.Min(node => node.X);
        double rightmostBox = boxes.Max(node => node.Right);

        bool clearOfBoxes = consumption.Points.Max(point => point.X) > rightmostBox
                            || consumption.Points.Min(point => point.X) < leftmostBox;

        Assert.True(clearOfBoxes, "The routing lane must sit clear of the boxes.");
        Assert.True(board.Width > consumption.Points.Max(point => point.X), "The board must be wide enough to contain its lanes.");
        Assert.True(consumption.Points.Min(point => point.X) > 0, "Nothing may be drawn off the left of the board.");
    }

    [Fact]
    public void APipeLeavesByTheSideItHasLessGroundToCover()
    {
        // A pipe that skips rows has to leave the block, and it used to always leave rightwards —
        // so a value produced at the left edge crossed the entire board twice to reach a lane, and
        // paid for a lane's width on the far side to do it.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["fromTheLeft"]),
            GraphBuilder.Step(1, layer: 0),
            GraphBuilder.Step(2, layer: 0),
            GraphBuilder.Step(3, layer: 0, outputs: ["fromTheRight"]),
            GraphBuilder.Step(4, layer: 1),
            GraphBuilder.Step(5, layer: 2, inputs: ["fromTheLeft"]),
            GraphBuilder.Step(6, layer: 2, inputs: ["fromTheRight"]))));

        LayoutNode[] boxes = [.. board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage)];
        double centre = (boxes.Min(node => node.X) + boxes.Max(node => node.Right)) / 2;

        LayoutEdge fromLeft = board.Edges.Single(edge => edge.Key == "fromTheLeft");
        LayoutEdge fromRight = board.Edges.Single(edge => edge.Key == "fromTheRight");

        // Which side, not how far out: a pipe that can descend inside the block should, so the test
        // is that each keeps to its own half rather than that both step over everything.
        Assert.True(fromLeft.Points.Min(point => point.X) < centre, "A pipe at the left edge should descend on the left.");
        Assert.True(fromRight.Points.Max(point => point.X) > centre, "A pipe at the right edge should descend on the right.");
    }

    [Fact]
    public void APipePassingOnlyNarrowRowsDescendsInsideTheBlock()
    {
        // The rows it passes are one step wide and the widest row is three, so there is a corridor
        // down each side of the stage that nothing occupies. Leaving the block to get past a row that
        // was never in the way is what made pipes long and boards wide.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["carried"]),
            GraphBuilder.Step(1, layer: 0),
            GraphBuilder.Step(2, layer: 0),
            GraphBuilder.Step(3, layer: 1),
            GraphBuilder.Step(4, layer: 2, inputs: ["carried"]))));

        LayoutNode[] boxes = [.. board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage)];
        double left = boxes.Min(node => node.X);
        double right = boxes.Max(node => node.Right);

        LayoutEdge carried = board.Edges.Single(edge => edge.Key == "carried");

        Assert.True(carried.Points.Min(point => point.X) >= left, "The pipe should not leave the block to the left.");
        Assert.True(carried.Points.Max(point => point.X) <= right, "The pipe should not leave the block to the right.");

        // The corridor is only usable while it clears the row it passes, which is the box in layer 1.
        LayoutNode passed = boxes.Single(node => node.Y > boxes.Min(box => box.Y) && node.Y < boxes.Max(box => box.Y));

        Assert.All(
            carried.Points,
            point => Assert.True(
                point.X <= passed.X || point.X >= passed.Right || point.Y <= passed.Y || point.Y >= passed.Bottom,
                "The pipe must not enter the box it passes."));
    }

    [Fact]
    public void RoomForALeftLaneIsMadeRatherThanBorrowedFromOffTheBoard()
    {
        // The left of the board is a hard edge: a lane placed beyond it is not drawn at all. The
        // block moves right to make the room instead.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["first", "second"]),
            GraphBuilder.Step(1, layer: 1),
            GraphBuilder.Step(2, layer: 2, inputs: ["first"]),
            GraphBuilder.Step(3, layer: 3, inputs: ["second"]))));

        Assert.All(board.Edges, edge => Assert.All(edge.Points, point => Assert.True(point.X > 0, "No pipe may be drawn off the left of the board.")));
        Assert.All(board.Nodes, node => Assert.True(node.X >= 0, "No box may be drawn off the left of the board."));
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
    public void PipesThatCouldNotCollideShareOneLane()
    {
        // One pipe finishes above where the next begins, so putting both in one lane cannot draw
        // them on top of each other. Refusing to share cost real width for nothing: every long pipe
        // took a lane, so a board whose pipes ran one after another down it grew as wide as if they
        // had all been travelling at once.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["first"]),
            GraphBuilder.Step(1, layer: 1),
            GraphBuilder.Step(2, layer: 2, inputs: ["first"], outputs: ["second"]),
            GraphBuilder.Step(3, layer: 3),
            GraphBuilder.Step(4, layer: 4, inputs: ["second"]))));

        int[] lanes = [.. board.Edges.Where(edge => edge.Lane > 0).Select(edge => edge.Lane)];

        Assert.Equal(2, lanes.Length);
        Assert.Single(lanes.Distinct());

        // The concern that made lanes exclusive, asserted rather than avoided: sharing is only safe
        // while the two never occupy the lane at the same height.
        Assert.All(Overlaps(board), overlap => Assert.Fail($"Two pipes share a line: {overlap}."));
    }

    [Fact]
    public void APipeSkippingOneRowStaysNearerTheBlockThanOneSkippingSeveral()
    {
        // Lanes are handed out shortest journey first. Ordered by where a pipe starts instead, the
        // pipe with least to travel could be sent to the outermost lane and make the longest detour
        // on the board to skip a single row.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["far"]),
            GraphBuilder.Step(1, layer: 1, outputs: ["near"]),
            GraphBuilder.Step(2, layer: 2),
            GraphBuilder.Step(3, layer: 3, inputs: ["near"]),
            GraphBuilder.Step(4, layer: 4),
            GraphBuilder.Step(5, layer: 5, inputs: ["far"]))));

        LayoutEdge near = board.Edges.Single(edge => edge.Key == "near");
        LayoutEdge far = board.Edges.Single(edge => edge.Key == "far");

        Assert.True(near.Lane < far.Lane, "The shorter journey must get the lane nearer the block.");
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

    /// <summary>
    /// Every pair of segments from different pipes that lie on one line and share more than a point.
    /// </summary>
    /// <remarks>
    /// Two pipes leaving the same connector are one value that forks: they are meant to share the run
    /// out of the port, so they are not counted.
    /// </remarks>
    private static IEnumerable<string> Overlaps(LayoutResult board)
    {
        (string Port, LayoutPoint From, LayoutPoint To)[] segments =
        [
            .. board.Edges.SelectMany(edge => Enumerable
                .Range(1, edge.Points.Count - 1)
                .Select(index => (edge.FromPortId, edge.Points[index - 1], edge.Points[index])))
        ];

        for (int outer = 0; outer < segments.Length; outer++)
        {
            for (int inner = outer + 1; inner < segments.Length; inner++)
            {
                if (segments[outer].Port == segments[inner].Port)
                    continue;

                if (Collinear(segments[outer].From, segments[outer].To, segments[inner].From, segments[inner].To))
                    yield return $"({segments[outer].From.X},{segments[outer].From.Y})-({segments[outer].To.X},{segments[outer].To.Y})";
            }
        }
    }

    private static bool Collinear(LayoutPoint firstFrom, LayoutPoint firstTo, LayoutPoint secondFrom, LayoutPoint secondTo)
    {
        bool firstVertical = Math.Abs(firstFrom.X - firstTo.X) < 1e-9;

        if (firstVertical != (Math.Abs(secondFrom.X - secondTo.X) < 1e-9))
            return false;

        return firstVertical
            ? Math.Abs(firstFrom.X - secondFrom.X) < 1e-9 && Shares(firstFrom.Y, firstTo.Y, secondFrom.Y, secondTo.Y)
            : Math.Abs(firstFrom.Y - secondFrom.Y) < 1e-9 && Shares(firstFrom.X, firstTo.X, secondFrom.X, secondTo.X);
    }

    private static bool Shares(double firstA, double firstB, double secondA, double secondB)
        => Math.Min(Math.Max(firstA, firstB), Math.Max(secondA, secondB))
           - Math.Max(Math.Min(firstA, firstB), Math.Min(secondA, secondB)) > 1e-9;

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
