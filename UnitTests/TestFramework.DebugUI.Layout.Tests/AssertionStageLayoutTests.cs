using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers the assertions stage: one card per check, below the run, piped from the values it checked.
/// </summary>
/// <remarks>
/// It answers "why is this run valid" as part of the board rather than as a list elsewhere, so what matters is
/// that every check is there to be read and that the pipes into them are real - drawn from the values that were
/// actually checked, and from nothing else.
/// </remarks>
public sealed class AssertionStageLayoutTests
{
    [Fact]
    public void ARunThatCheckedNothingHasNoAssertionsStage()
    {
        // An empty stage would answer a question nobody asked, and answer it with nothing.
        LayoutResult board = RunBoardLayout.Compute(Checked());

        Assert.DoesNotContain(board.Nodes, node => node.Kind == LayoutNodeKind.Assertion);
        Assert.DoesNotContain(board.Nodes, node => node.StageName == RunChecks.StageName);
    }

    [Fact]
    public void EveryCheckIsACardOfItsOwn()
    {
        LayoutResult board = RunBoardLayout.Compute(Checking(
            Checked(),
            Check(DebugAssertionTargetKind.Variable, "greeting", "Be", true),
            Check(DebugAssertionTargetKind.Step, "ping", "HaveCompleted", true),
            Check(DebugAssertionTargetKind.Value, "count > 2", "BeTrue", true)));

        Assert.Equal(["Be", "HaveCompleted", "BeTrue"], Cards(board).Select(card => card.Title));
    }

    [Fact]
    public void ACheckOnAValueIsPipedFromTheStepThatProducedIt()
    {
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Variable, "greeting", "Be", true)));

        LayoutNode card = Assert.Single(Cards(board));
        LayoutEdge pipe = Assert.Single(board.Edges, edge => edge.ToNodeId == card.Id);

        Assert.Equal("greeting", pipe.Key);
        Assert.Equal("step:Main/1", pipe.FromNodeId);
        Assert.Equal(LayoutEdgeKind.Assertion, pipe.Kind);
    }

    [Fact]
    public void APipeIntoACheckIsAnAssertionHoweverFarItTravels()
    {
        // A pipe that has to leave for a lane and come back is built by a different branch from one that drops
        // straight in. Both end at a check, so both are assertions - and the long one is the likelier of the two,
        // since the checks sit below everything.
        LayoutResult board = RunBoardLayout.Compute(Checking(Long(), Check(DebugAssertionTargetKind.Variable, "early", "Be", false)));

        LayoutEdge pipe = Assert.Single(board.Edges, edge => edge.ToNodeId == Cards(board).Single().Id);

        Assert.Equal(LayoutEdgeKind.Assertion, pipe.Kind);
        Assert.True(pipe.Points.Count > 2, "The pipe was expected to travel rather than drop straight in.");
    }

    [Fact]
    public void ACheckOnSomethingNoStepProducedIsACardWithNothingPluggedIn()
    {
        // There is nothing on the board to run a pipe from, and inventing a source would draw a flow the run
        // never had.
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Variable, "nowhere", "Exist", false)));

        LayoutNode card = Assert.Single(Cards(board));
        Assert.DoesNotContain(board.Edges, edge => edge.ToNodeId == card.Id);
    }

    [Fact]
    public void ACheckOnAStepIsNeverPipedFromAValueThatSharesItsName()
    {
        // The check names a step; a variable called the same is a different thing.
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Step, "greeting", "HaveCompleted", true)));

        Assert.DoesNotContain(board.Edges, edge => edge.ToNodeId == Cards(board).Single().Id);
    }

    [Fact]
    public void ACheckMadeRepeatedlyIsOneCard_AndHeldOnlyIfItAlwaysHeld()
    {
        LayoutResult board = RunBoardLayout.Compute(Checking(
            Checked(),
            Check(DebugAssertionTargetKind.Step, "ping", "HaveCompleted", true),
            Check(DebugAssertionTargetKind.Step, "ping", "HaveCompleted", false)));

        Assert.False(Assert.Single(Cards(board)).IsSatisfied);
    }

    [Fact]
    public void ACardSaysWhatWasChecked()
    {
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Step, "ping", "HaveCompleted", true)));

        Assert.Equal("'ping'", Cards(board).Single().Detail);
    }

    [Fact]
    public void TheChecksSitBelowEverythingTheRunDid()
    {
        // They are the conclusion, so they read last. Anything above the steps would be read as an input.
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Variable, "greeting", "Be", true)));

        double firstCheck = Cards(board).Min(card => card.Y);

        Assert.All(
            board.Nodes.Where(node => node.Kind == LayoutNodeKind.Step),
            node => Assert.True(node.Bottom <= firstCheck, $"'{node.Id}' is not above the checks."));
    }

    [Fact]
    public void TheChecksAreBandedAsAStageOfTheirOwn()
    {
        LayoutResult board = RunBoardLayout.Compute(Checking(Checked(), Check(DebugAssertionTargetKind.Variable, "greeting", "Be", true)));

        LayoutNode band = Assert.Single(board.Nodes, node => node.Kind == LayoutNodeKind.Stage && node.StageName == RunChecks.StageName);

        Assert.All(Cards(board), card => Assert.True(card.Y >= band.Y && card.Bottom <= band.Bottom, $"'{card.Id}' is outside its band."));
        Assert.All(
            board.Nodes.Where(node => node.Kind == LayoutNodeKind.Stage && node.StageName != RunChecks.StageName),
            other => Assert.True(other.Bottom < band.Y, $"Stage band '{other.Id}' overlaps the checks."));
    }

    [Fact]
    public void OneFailedCheckLeavesTheStageUnsatisfied_AndCountsWhatHeld()
    {
        LayoutResult board = RunBoardLayout.Compute(Checking(
            Checked(),
            Check(DebugAssertionTargetKind.Variable, "greeting", "Be", true),
            Check(DebugAssertionTargetKind.Variable, "name", "Be", false)));

        LayoutNode band = board.Nodes.Single(node => node.Kind == LayoutNodeKind.Stage && node.StageName == RunChecks.StageName);

        Assert.False(band.IsSatisfied);
        Assert.Equal("1 of 2 held", band.Detail);
        Assert.Equal([true, false], Cards(board).Select(card => card.IsSatisfied));
    }

    [Fact]
    public void MoreChecksThanFitSideBySideStartARowOfTheirOwn()
    {
        AssertionNode[] many =
        [
            .. Enumerable.Range(0, RunChecks.PerRow + 1)
                .Select(index => Check(DebugAssertionTargetKind.Step, $"s{index}", "HaveCompleted", true))
        ];

        LayoutNode[] cards = Cards(RunBoardLayout.Compute(Checking(Checked(), many)));
        double firstRow = cards.Min(card => card.Y);

        Assert.Equal(2, cards.Select(card => card.Y).Distinct().Count());
        Assert.Equal(RunChecks.PerRow, cards.Count(card => card.Y == firstRow));
    }

    /// <summary>The check cards, in the order the run made the checks.</summary>
    private static LayoutNode[] Cards(LayoutResult board)
        => [.. board.Nodes.Where(node => node.Kind == LayoutNodeKind.Assertion).OrderBy(node => node.StepId)];

    private static RunGraph Checking(RunGraph run, params AssertionNode[] checks) => run with { Assertions = [.. checks] };

    private static AssertionNode Check(DebugAssertionTargetKind kind, string target, string name, bool held) => new()
    {
        TargetKind = kind,
        Target = target,
        AssertionName = name,
        Succeeded = held
    };

    /// <summary>A run whose checked value is produced several rows above the checks.</summary>
    private static RunGraph Long() => GraphBuilder.Run(
        GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["early"]),
            GraphBuilder.Step(1, layer: 1, outputs: ["middle"]),
            GraphBuilder.Step(2, layer: 2, inputs: ["middle"])),
        GraphBuilder.Stage(
            "Cleanup",
            GraphBuilder.Step(0, layer: 0)));

    /// <summary>A run that produces two values, so there is something to check.</summary>
    private static RunGraph Checked() => GraphBuilder.Run(
        GraphBuilder.Stage(
            "Main",
            GraphBuilder.Step(0, layer: 0, outputs: ["name"]),
            GraphBuilder.Step(1, layer: 1, inputs: ["name"], outputs: ["greeting"])));
}
