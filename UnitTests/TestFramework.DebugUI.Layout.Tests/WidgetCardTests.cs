using System.Linq;
using TestFramework.DebugUI.Layout;
using Xunit;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers a step's card growing only when the step has something to show in it.
/// </summary>
/// <remarks>
/// The whole board used to grow together, which meant one screenshot in a run turned every other
/// step into a mostly-empty card — and a taller board fits smaller, so the writing on every card got
/// harder to read to make room for space nothing was using.
/// </remarks>
public class WidgetCardTests
{
    [Fact]
    public void AStepWithAWidgetGetsTheRoomToShowIt()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Showing(
            GraphBuilder.Run(GraphBuilder.Stage("Main Stage", GraphBuilder.Step(0, 0, "photographs"))),
            ("Main Stage", 0)));

        LayoutNode card = board.Nodes.Single(node => node.Kind == LayoutNodeKind.Step);

        Assert.Equal(LayoutOptions.Default.StepHeightWithWidget, card.Height);
    }

    [Fact]
    public void AStepWithNothingToShowKeepsTheShortCard()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(
            GraphBuilder.Stage("Main Stage", GraphBuilder.Step(0, 0, "quiet"))));

        LayoutNode card = board.Nodes.Single(node => node.Kind == LayoutNodeKind.Step);

        Assert.Equal(LayoutOptions.Default.StepHeight, card.Height);
    }

    [Fact]
    public void OneStepShowingSomethingDoesNotInflateTheRest()
    {
        // The point of measuring per step. A run with one screenshot in it should cost one taller
        // card, not a board of them.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Showing(
            GraphBuilder.Run(GraphBuilder.Stage(
                "Main Stage",
                GraphBuilder.Step(0, 0, "photographs"),
                GraphBuilder.Step(1, 1, "quiet"),
                GraphBuilder.Step(2, 2, "also quiet"))),
            ("Main Stage", 0)));

        double[] heights = [.. board.Nodes.Where(node => node.Kind == LayoutNodeKind.Step).Select(node => node.Height)];

        Assert.Equal(1, heights.Count(height => height == LayoutOptions.Default.StepHeightWithWidget));
        Assert.Equal(2, heights.Count(height => height == LayoutOptions.Default.StepHeight));
    }

    [Fact]
    public void ARowIsAsDeepAsItsTallestCard()
    {
        // Cards in a layer run side by side and share a top edge, so the row has to reserve the
        // tallest of them or the next row would be drawn over one.
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Showing(
            GraphBuilder.Run(GraphBuilder.Stage(
                "Main Stage",
                GraphBuilder.Step(0, 0, "photographs"),
                GraphBuilder.Step(1, 0, "quiet"),
                GraphBuilder.Step(2, 1, "next"))),
            ("Main Stage", 0)));

        LayoutNode tall = board.Nodes.Single(node => node.StepId == 0);
        LayoutNode next = board.Nodes.Single(node => node.StepId == 2);

        Assert.True(
            next.Y >= tall.Y + tall.Height,
            $"the next row starts at {next.Y}, inside a card that runs to {tall.Y + tall.Height}");
    }
}
