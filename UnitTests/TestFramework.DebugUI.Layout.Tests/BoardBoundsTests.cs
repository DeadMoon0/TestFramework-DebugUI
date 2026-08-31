using System.Linq;
using TestFramework.DebugUI.Layout;
using Xunit;
using Xunit.Abstractions;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Covers the board's reported size actually describing what is drawn on it.
/// </summary>
/// <remarks>
/// Everything that fits a board to a window divides by <see cref="LayoutResult.Width"/> and centres
/// against it. If the box is wider than the content, or the content does not start where the box
/// does, a fitted board sits off to one side with dead space beside it — and no amount of pressing
/// Fit will move it, because the arithmetic is doing exactly what it was told.
/// </remarks>
public class BoardBoundsTests(ITestOutputHelper output)
{
    [Fact]
    public void TheBoardIsAsWideAsWhatIsDrawnOnIt()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(
            GraphBuilder.Stage(
                "Main Stage",
                GraphBuilder.Step(0, 0, "browse the catalogue"),
                GraphBuilder.Step(1, 1, "fill the basket"),
                GraphBuilder.Step(2, 2, "check out"))));

        double left = board.Nodes.Min(node => node.X);
        double right = board.Nodes.Max(node => node.X + node.Width);

        output.WriteLine($"box 0..{board.Width}  content {left}..{right}  gaps {left} / {board.Width - right}");

        Assert.True(
            left <= board.Width - right + 1,
            $"the empty strip left of the content ({left}) is wider than the one right of it ({board.Width - right}), so anything centring this box puts the run off to one side");
    }

    [Fact]
    public void TheBoardIsAsTallAsWhatIsDrawnOnIt()
    {
        LayoutResult board = RunBoardLayout.Compute(GraphBuilder.Run(
            GraphBuilder.Stage(
                "Main Stage",
                GraphBuilder.Step(0, 0, "browse the catalogue"),
                GraphBuilder.Step(1, 1, "fill the basket"))));

        double top = board.Nodes.Min(node => node.Y);
        double bottom = board.Nodes.Max(node => node.Y + node.Height);

        output.WriteLine($"box 0..{board.Height}  content {top}..{bottom}  gaps {top} / {board.Height - bottom}");

        Assert.True(
            top <= board.Height - bottom + 1,
            $"the empty strip above the content ({top}) is taller than the one below it ({board.Height - bottom})");
    }
}
