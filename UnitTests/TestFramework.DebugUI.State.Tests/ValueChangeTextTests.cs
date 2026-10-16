using System.Collections.Immutable;
using System.Globalization;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// What the value inspector says about a changed value. The text it compares is only what was sent of each
/// value, so it can match while the values do not - and then the comparison, not the text, has the last word.
/// </summary>
public class ValueChangeTextTests
{
    [Fact]
    public void TheSameVisibleTextForAChangedValue_SaysTheValuesDiffer_RatherThanThatTheyAreIdentical()
    {
        // The second corrected rule: the badge said "changed" and the panel used to answer "identical".
        ValueChange change = ValueComparison.For("report", DebugValueKindTag.Variable, FileBacked("abc123"), FileBacked("def456"));

        ImmutableList<DiffLine> lines = ValueChangeText.Of(change, CultureInfo.InvariantCulture);

        DiffLine line = Assert.Single(lines);
        Assert.Equal(DiffLineKind.Note, line.Kind);
        Assert.Contains("the values differ", line.Text, System.StringComparison.Ordinal);
        Assert.Contains("content hashes do not match", line.Text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnchangedValue_IsStillShownAsTheSame()
    {
        ValueChange change = ValueComparison.For("report", DebugValueKindTag.Variable, FileBacked("abc123"), FileBacked("abc123"));

        ImmutableList<DiffLine> lines = ValueChangeText.Of(change, CultureInfo.InvariantCulture);

        Assert.DoesNotContain(lines, line => line.Text.Contains("the values differ", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ValuesOfDifferentSizes_SayHowLargeEachWas()
    {
        ValueChange change = ValueComparison.For("report", DebugValueKindTag.Variable, FileBacked("abc123", size: 1_000), FileBacked("def456", size: 2_000));

        Assert.Contains("before", ValueChangeText.Difference(change, CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    private static ValueDescription FileBacked(string hash, long size = 40_000) => new()
    {
        Summary = "a report",
        Shape = DebugValueShape.Text,
        Body = new ValueBody
        {
            Path = @"C:\runs\run\values\report.txt",
            RelativePath = "values/report.txt",
            SizeInBytes = size,
            ContentHash = hash
        }
    };
}
