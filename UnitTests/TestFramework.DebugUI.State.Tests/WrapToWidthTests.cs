using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers wrapping the diff pane's own remarks.
/// </summary>
/// <remarks>
/// The pane's document is deliberately far wider than the panel so a line of code is never broken
/// mid-statement. Prose has to be wrapped before it goes in, or it runs off to the right where nobody
/// scrolls to read it — which is how this started: the explanation of why a diff could not be shown
/// was there all along and invisible.
/// </remarks>
public class WrapToWidthTests
{
    [Fact]
    public void ShortTextIsLeftAsOneLine()
    {
        Assert.Equal(["already fits"], ValueInspection.WrapToWidth("already fits", 40));
    }

    [Fact]
    public void LongTextIsBrokenOnWordBoundaries()
    {
        ImmutableList<string> lines = ValueInspection.WrapToWidth(
            "The part that was sent is the same in both runs, but the values differ.", 30);

        Assert.True(lines.Count > 1);
        Assert.All(lines, line => Assert.True(line.Length <= 30, $"'{line}' is {line.Length} characters."));

        // Nothing lost and nothing invented: the words come back in order.
        Assert.Equal(
            "The part that was sent is the same in both runs, but the values differ.",
            string.Join(" ", lines));
    }

    [Fact]
    public void AWordLongerThanTheBudgetIsKeptWholeRatherThanCut()
    {
        // Usually a path or a hash. Half of one is worse than a line that overflows, because half of
        // one looks like a whole one.
        ImmutableList<string> lines = ValueInspection.WrapToWidth(@"see C:\runs\a-very-long-run-folder-name\values\report.txt", 20);

        Assert.Contains(lines, line => line.Contains(@"a-very-long-run-folder-name\values\report.txt"));
    }

    [Fact]
    public void NothingToWrapProducesNoLines()
    {
        Assert.Empty(ValueInspection.WrapToWidth(null, 40));
        Assert.Empty(ValueInspection.WrapToWidth("   ", 40));
    }

    [Fact]
    public void ANonsensicalBudgetReturnsTheTextRatherThanLoopingOrLosingIt()
    {
        Assert.Equal(["some text"], ValueInspection.WrapToWidth("some text", 0));
    }
}
