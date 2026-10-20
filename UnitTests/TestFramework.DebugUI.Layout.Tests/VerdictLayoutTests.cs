using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers the verdict: the box every asserted value flows into.
/// </summary>
/// <remarks>
/// It answers "why is this run valid" as part of the board rather than as a list elsewhere, so what
/// matters is that the pipes into it are real — drawn from the values that were actually checked,
/// and from nothing else.
/// </remarks>
public sealed class VerdictLayoutTests
{
    [Fact]
    public void ARunThatAssertedNothingHasNoVerdict()
    {
        // Drawing an empty verdict would answer a question nobody asked, and answer it with nothing.
        LayoutResult board = RunBoardLayout.Compute(Checked());

        Assert.DoesNotContain(board.Nodes, node => node.Kind == LayoutNodeKind.Verdict);
    }

    [Fact]
    public void EveryAssertedValueIsPipedIntoTheVerdict()
    {
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true), ("name", true)));

        LayoutNode verdict = Assert.Single(board.Nodes.Where(node => node.Kind == LayoutNodeKind.Verdict));

        string[] arriving =
        [
            .. board.Edges
                .Where(edge => edge.ToNodeId == verdict.Id)
                .Select(edge => edge.Key)
                .Order()
        ];

        Assert.Equal(["greeting", "name"], arriving);
    }

    [Fact]
    public void PipesIntoTheVerdictAreAssertionsRatherThanConsumptions()
    {
        // Nothing downstream uses the value; it is being checked. The view colours it by whether the
        // check held, which it cannot do if the pipe claims to be an ordinary consumption.
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", false)));

        LayoutEdge edge = Assert.Single(board.Edges.Where(candidate => candidate.ToNodeId == "verdict"));

        Assert.Equal(LayoutEdgeKind.Assertion, edge.Kind);
    }

    [Fact]
    public void APipeIntoTheVerdictIsAnAssertionHoweverFarItTravels()
    {
        // A pipe that has to leave for a lane and come back is built by a different branch from one
        // that drops straight in. Both end at the verdict, so both are assertions — and the long one
        // is the likelier of the two, since the verdict sits below everything.
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Long(), ("early", false)));

        LayoutEdge edge = Assert.Single(board.Edges.Where(candidate => candidate.ToNodeId == "verdict"));

        Assert.Equal(LayoutEdgeKind.Assertion, edge.Kind);
        Assert.True(edge.Points.Count > 2, "The pipe was expected to travel rather than drop straight in.");
    }

    [Fact]
    public void AnAssertionAgainstSomethingNoStepProducedIsNotWiredIn()
    {
        // There is nothing on the board to run a pipe from, and inventing a source would draw a flow
        // the run never had.
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true), ("nowhere", true)));

        string[] arriving = [.. board.Edges.Where(edge => edge.ToNodeId == "verdict").Select(edge => edge.Key)];

        Assert.Equal(["greeting"], arriving);
    }

    [Fact]
    public void ARunCheckedOnlyThroughSomethingNoStepProducedStillHasAVerdict()
    {
        // Most checks are of this kind - a step that completed, an expression. A run proved entirely by them is
        // still a run that proved something, and the verdict is where the board says so.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Asserting(Checked(), ("nowhere", false)));

        Assert.Single(board.Nodes.Where(node => node.Kind == LayoutNodeKind.Verdict));
        Assert.DoesNotContain(board.Edges, edge => edge.ToNodeId == "verdict");
    }

    [Fact]
    public void ACheckOnAStepIsNeverPipedFromAValueThatSharesItsName()
    {
        // The check names a step; a variable called the same is a different thing.
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Step, "greeting", "HaveCompleted", true)));

        Assert.DoesNotContain(board.Edges, edge => edge.ToNodeId == "verdict");
        Assert.Equal(["greeting"], VerdictChecks.Unwired(Checking(Checked(), Check(DebugAssertionTargetKind.Step, "greeting", "HaveCompleted", true))).Select(check => check.Target));
    }

    [Fact]
    public void TheVerdictGrowsALineForEachCheckItLists()
    {
        LayoutOptions options = new();

        double one = VerdictHeight(Checking(Checked(), Step("a")));
        double three = VerdictHeight(Checking(Checked(), Step("a"), Step("b"), Step("c")));

        Assert.Equal(options.StepHeight + options.VerdictLineHeight, one);
        Assert.Equal(options.StepHeight + (3 * options.VerdictLineHeight), three);
    }

    [Fact]
    public void BeyondAFewChecksTheVerdictStopsGrowingAndCountsTheRest()
    {
        LayoutOptions options = new();
        AssertionNode[] many = [.. Enumerable.Range(0, VerdictChecks.Listed + 5).Select(index => Step($"s{index}"))];

        Assert.Equal(options.StepHeight + ((VerdictChecks.Listed + 1) * options.VerdictLineHeight), VerdictHeight(Checking(Checked(), many)));
    }

    [Fact]
    public void ACheckOnAValueAStepProducedIsPipedRatherThanListed()
    {
        RunGraph run = Checking(Checked(), Check(DebugAssertionTargetKind.Variable, "greeting", "Be", true));

        Assert.Empty(VerdictChecks.Unwired(run));
        Assert.Equal(new LayoutOptions().StepHeight, VerdictHeight(run));
    }

    [Fact]
    public void ACheckMadeRepeatedlyIsListedOnce_AndHeldOnlyIfItAlwaysHeld()
    {
        RunGraph run = Checking(
            Checked(),
            Check(DebugAssertionTargetKind.Step, "ping", "HaveCompleted", true),
            Check(DebugAssertionTargetKind.Step, "ping", "HaveCompleted", false));

        VerdictCheck check = Assert.Single(VerdictChecks.Unwired(run));
        Assert.False(check.Held);
    }

    private static double VerdictHeight(RunGraph run)
        => RunBoardLayout.Compute(run).Nodes.Single(node => node.Kind == LayoutNodeKind.Verdict).Height;

    private static RunGraph Checking(RunGraph run, params AssertionNode[] checks) => run with { Assertions = [.. checks] };

    private static AssertionNode Step(string name) => Check(DebugAssertionTargetKind.Step, name, "HaveCompleted", true);

    private static AssertionNode Check(DebugAssertionTargetKind kind, string target, string name, bool held) => new()
    {
        TargetKind = kind,
        Target = target,
        AssertionName = name,
        Succeeded = held
    };

    [Fact]
    public void TheVerdictSitsBelowEverythingTheRunDid()
    {
        // It is the conclusion, so it reads last. Anything above the steps would be read as an input.
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true)));

        LayoutNode verdict = board.Nodes.Single(node => node.Kind == LayoutNodeKind.Verdict);

        Assert.All(
            board.Nodes.Where(node => node.Kind == LayoutNodeKind.Step),
            node => Assert.True(node.Bottom <= verdict.Y, $"'{node.Id}' is not above the verdict."));
    }

    [Fact]
    public void TheVerdictIsNotBandedAsAStage()
    {
        // A band around it would read as a stage the run executed.
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true)));

        LayoutNode verdict = board.Nodes.Single(node => node.Kind == LayoutNodeKind.Verdict);

        Assert.All(
            board.Nodes.Where(node => node.Kind == LayoutNodeKind.Stage),
            band => Assert.True(band.Bottom < verdict.Y, $"Stage band '{band.Id}' encloses the verdict."));
    }

    [Fact]
    public void OneFailedAssertionIsEnoughToLeaveTheVerdictUnsatisfied()
    {
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true), ("name", false)));

        Assert.False(board.Nodes.Single(node => node.Kind == LayoutNodeKind.Verdict).IsSatisfied);
    }

    [Fact]
    public void AllHoldingLeavesTheVerdictSatisfied()
    {
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true), ("name", true)));

        Assert.True(board.Nodes.Single(node => node.Kind == LayoutNodeKind.Verdict).IsSatisfied);
    }

    [Fact]
    public void AValueAssertedTwiceIsPipedInOnce()
    {
        // Two pipes between the same two connectors are indistinguishable from one, and the second
        // would claim a track it does not need.
        LayoutResult board = RunBoardLayout.Compute(
            GraphBuilder.Asserting(Checked(), ("greeting", true), ("greeting", false)));

        Assert.Single(board.Edges.Where(edge => edge.ToNodeId == "verdict"));
    }

    /// <summary>A run whose asserted value is produced several rows above the verdict.</summary>
    private static RunGraph Long() => GraphBuilder.Run(
        GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["early"]),
            GraphBuilder.Step(1, layer: 1, outputs: ["middle"]),
            GraphBuilder.Step(2, layer: 2, inputs: ["middle"])),
        GraphBuilder.Stage(
            "Cleanup",
            GraphBuilder.Step(0, layer: 0)));

    /// <summary>A run that produces two values, so there is something to assert against.</summary>
    private static RunGraph Checked() => GraphBuilder.Run(
        GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["name"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["name"], outputs: ["greeting"])));
}
