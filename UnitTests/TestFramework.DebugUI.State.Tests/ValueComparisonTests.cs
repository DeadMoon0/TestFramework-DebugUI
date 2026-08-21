using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers comparing a run's values against the last run that passed.
/// </summary>
/// <remarks>
/// The reader is asking "what is different from when this worked", and they will act on the answer
/// without re-deriving it. That makes a false "unchanged" the expensive failure here: it sends someone
/// looking for the cause of a difference somewhere other than where the difference is.
/// </remarks>
public class ValueComparisonTests
{
    [Fact]
    public void AValueWrittenToAFileIsComparedByHashRatherThanByWhereItWasWritten()
    {
        // The whole reason this class exists. Each run writes its large values into its own folder, so
        // the paths always differ; comparing descriptions wholesale would report every large value as
        // changed on every run and the feature would be noise.
        ValueDescription baseline = FileBacked(hash: "abc123", path: @"C:\runs\run-1\values\report.txt");
        ValueDescription current = FileBacked(hash: "abc123", path: @"C:\runs\run-2\values\report.txt");

        Assert.Equal(ValueChangeKind.Unchanged, Change(baseline, current).Change);
    }

    [Fact]
    public void ADifferentHashIsAChangeEvenWhenEverythingElseMatches()
    {
        ValueDescription baseline = FileBacked(hash: "abc123", path: @"C:\runs\run-1\values\report.txt");
        ValueDescription current = FileBacked(hash: "def456", path: @"C:\runs\run-1\values\report.txt");

        Assert.Equal(ValueChangeKind.Changed, Change(baseline, current).Change);
    }

    [Fact]
    public void APlainValueIsComparedByItsPreview()
    {
        Assert.Equal(ValueChangeKind.Unchanged, Change(Plain("ping -n 3"), Plain("ping -n 3")).Change);
        Assert.Equal(ValueChangeKind.Changed, Change(Plain("ping -n 3"), Plain("ping -n 5")).Change);
    }

    [Fact]
    public void TwoPreviewsCutShortAtTheSamePointAreNotClaimedToBeEqual()
    {
        // Both previews say the values START the same way. Without a hash that is all they say, and
        // "unchanged" would be a claim about the part nobody looked at.
        ValueDescription baseline = Plain("the first ten thousand orders", truncated: true, size: 40_000);
        ValueDescription current = Plain("the first ten thousand orders", truncated: true, size: 40_000);

        ValueChange change = Change(baseline, current);

        Assert.Equal(ValueChangeKind.Indeterminate, change.Change);
        Assert.NotNull(change.Reason);
    }

    [Fact]
    public void ADifferentSizeIsAChangeEvenWhenTheVisiblePartIsIdentical()
    {
        // The common shape of a real regression: a collection that grew, whose first rows are the
        // same. Comparing only the preview text would call this unchanged.
        ValueDescription baseline = Plain("order 1\norder 2", truncated: true, size: 4_000);
        ValueDescription current = Plain("order 1\norder 2", truncated: true, size: 4_400);

        Assert.Equal(ValueChangeKind.Changed, Change(baseline, current).Change);
    }

    [Fact]
    public void AValueThatCarriesNothingComparableIsReportedAsSuchRatherThanAsUnchanged()
    {
        ValueDescription bare = new() { Shape = DebugValueShape.Object, Summary = "an object" };

        ValueChange change = Change(bare, bare with { Summary = "a different sentence about it" });

        Assert.Equal(ValueChangeKind.Indeterminate, change.Change);
        Assert.NotNull(change.Reason);
    }

    [Fact]
    public void TheSummaryAloneNeverDecidesTheAnswer()
    {
        // Summaries are prose for a human. Two runs can word the same value differently, and a
        // wording change is not a value change.
        ValueDescription baseline = Plain("same content") with { Summary = "19 characters" };
        ValueDescription current = Plain("same content") with { Summary = "a string of 19 chars" };

        Assert.Equal(ValueChangeKind.Unchanged, Change(baseline, current).Change);
    }

    [Fact]
    public void AKeyOnlyThisRunProducedIsAddedAndOneOnlyTheBaselineHadIsRemoved()
    {
        ImmutableList<ValueChange> changes = ValueComparison.Compare(
            new Dictionary<string, ValueDescription> { ["gone"] = Plain("x") },
            new Dictionary<string, ValueDescription> { ["fresh"] = Plain("y") },
            DebugValueKindTag.Variable);

        Assert.Equal(ValueChangeKind.Added, changes.Single(c => c.Key == "fresh").Change);
        Assert.Equal(ValueChangeKind.Removed, changes.Single(c => c.Key == "gone").Change);
    }

    [Fact]
    public void ADeclaredButUnassignedValueIsNotReportedAsRemoved()
    {
        // A key can be declared and never assigned. Counting that as present in the baseline would
        // invent a "removed" against a run that did assign it.
        ImmutableList<ValueChange> changes = ValueComparison.Compare(
            new Dictionary<string, ValueDescription> { ["maybe"] = ValueDescription.Empty },
            new Dictionary<string, ValueDescription> { ["maybe"] = Plain("now it has a value") },
            DebugValueKindTag.Variable);

        Assert.Equal(ValueChangeKind.Added, Assert.Single(changes).Change);
    }

    [Fact]
    public void TheChangesComeFirstSoAWallOfUnchangedValuesCannotBuryThem()
    {
        ImmutableList<ValueChange> changes = ValueComparison.Compare(
            new Dictionary<string, ValueDescription>
            {
                ["aaa"] = Plain("same"),
                ["bbb"] = Plain("same"),
                ["zzz"] = Plain("before"),
                ["yyy"] = Plain("dropped")
            },
            new Dictionary<string, ValueDescription>
            {
                ["aaa"] = Plain("same"),
                ["bbb"] = Plain("same"),
                ["zzz"] = Plain("after"),
                ["www"] = Plain("brand new")
            },
            DebugValueKindTag.Variable);

        Assert.Equal(
            [ValueChangeKind.Changed, ValueChangeKind.Added, ValueChangeKind.Removed, ValueChangeKind.Unchanged, ValueChangeKind.Unchanged],
            changes.Select(change => change.Change));

        Assert.Equal("zzz", changes[0].Key);
    }

    [Fact]
    public void OnlyAChangeIsWorthTheReadersAttention()
    {
        Assert.False(Change(Plain("x"), Plain("x")).IsInteresting);
        Assert.True(Change(Plain("x"), Plain("y")).IsInteresting);
    }

    private static ValueChange Change(ValueDescription baseline, ValueDescription current)
        => ValueComparison.For("value", DebugValueKindTag.Variable, baseline, current);

    private static ValueDescription Plain(string text, bool truncated = false, long? size = null) => new()
    {
        Summary = text,
        Shape = DebugValueShape.Text,
        Preview = new ValuePreview
        {
            Form = DebugPreviewForm.Text,
            Text = text,
            IsTruncated = truncated,
            SizeInBytes = size
        }
    };

    private static ValueDescription FileBacked(string hash, string path) => new()
    {
        Summary = "a report",
        Shape = DebugValueShape.Text,
        Body = new ValueBody
        {
            Path = path,
            RelativePath = "values/report.txt",
            SizeInBytes = 40_000,
            ContentHash = hash
        }
    };
}
