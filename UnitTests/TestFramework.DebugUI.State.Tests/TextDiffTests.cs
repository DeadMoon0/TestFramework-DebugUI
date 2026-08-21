using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the line diff shown when a value differs from the last passing run.
/// </summary>
public class TextDiffTests
{
    [Fact]
    public void IdenticalTextIsSaidToBeIdenticalRatherThanShownAsAnEmptyDiff()
    {
        // An empty panel reads as "the diff failed". Saying so outright is the difference between an
        // answer and a blank.
        ImmutableList<DiffLine> diff = TextDiff.Unified("one\ntwo", "one\ntwo");

        DiffLine only = Assert.Single(diff);
        Assert.Equal(DiffLineKind.Note, only.Kind);
        Assert.Contains("identical", only.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AChangedLineIsShownAsARemovalAndAnAddition()
    {
        ImmutableList<DiffLine> diff = TextDiff.Unified("ping -n 3", "ping -n 5");

        Assert.Equal([DiffLineKind.Removed, DiffLineKind.Added], Kinds(diff));
        Assert.Equal("ping -n 3", diff[0].Text);
        Assert.Equal("ping -n 5", diff[1].Text);
    }

    [Fact]
    public void AnInsertedLineDoesNotMakeEveryLineBelowItLookChanged()
    {
        // The case a naive line-by-line walk gets wrong, and the reason this uses LCS. One inserted
        // line at the top would otherwise report the whole document as rewritten.
        ImmutableList<DiffLine> diff = TextDiff.Unified("a\nb\nc", "new\na\nb\nc");

        Assert.Single(diff.Where(line => line.Kind == DiffLineKind.Added));
        Assert.Empty(diff.Where(line => line.Kind == DiffLineKind.Removed));
        Assert.Equal("new", diff.First(line => line.Kind == DiffLineKind.Added).Text);
    }

    [Fact]
    public void UnchangedRunsAreCollapsedWithTheirCountRatherThanDropped()
    {
        // A value whose last line changed must not make the reader scroll past hundreds of identical
        // ones — but the diff must not look shorter than the value either.
        string baseline = string.Join("\n", Enumerable.Range(1, 200).Select(index => $"line {index}"));
        string current = baseline + "\nline 201";

        ImmutableList<DiffLine> diff = TextDiff.Unified(baseline, current);

        DiffLine note = Assert.Single(diff.Where(line => line.Kind == DiffLineKind.Note));
        Assert.Contains("unchanged", note.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("197", note.Text);

        // Kept small: the change plus its context, not the whole file.
        Assert.True(diff.Count < 10, $"Expected a short diff, got {diff.Count} lines.");
    }

    [Fact]
    public void ContextIsKeptEitherSideOfAChange()
    {
        string baseline = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"line {index}"));
        string current = baseline.Replace("line 20", "line twenty", StringComparison.Ordinal);

        ImmutableList<DiffLine> diff = TextDiff.Unified(baseline, current);

        Assert.Contains(diff, line => line.Kind == DiffLineKind.Context && line.Text == "line 17");
        Assert.Contains(diff, line => line.Kind == DiffLineKind.Context && line.Text == "line 23");
        Assert.DoesNotContain(diff, line => line.Kind == DiffLineKind.Context && line.Text == "line 5");
    }

    [Fact]
    public void TwoWildlyDifferentValuesAreDeclaredTooDifferentInsteadOfBeingForcedTogether()
    {
        // The bound that stops a quadratic diff freezing the window. Pairing unrelated lines from two
        // mostly-different documents also reads worse than saying they do not line up.
        string baseline = string.Join("\n", Enumerable.Range(1, 4_000).Select(index => $"left {index}"));
        string current = string.Join("\n", Enumerable.Range(1, 4_000).Select(index => $"right {index}"));

        DiffLine only = Assert.Single(TextDiff.Unified(baseline, current));

        Assert.Equal(DiffLineKind.Note, only.Kind);
        Assert.Contains("Too much differs", only.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALargeDocumentWithOneChangedFieldIsStillDiffedProperly()
    {
        // The ordinary case, and the reason prefix and suffix are matched off first: 8,000 lines is
        // far past the comparison limit, but only one of them differs.
        string[] lines = [.. Enumerable.Range(1, 8_000).Select(index => $"  \"field{index}\": {index},")];
        string baseline = string.Join("\n", lines);

        lines[4_000] = "  \"field4001\": 999999,";
        string current = string.Join("\n", lines);

        ImmutableList<DiffLine> diff = TextDiff.Unified(baseline, current);

        Assert.Single(diff.Where(line => line.Kind == DiffLineKind.Added));
        Assert.Single(diff.Where(line => line.Kind == DiffLineKind.Removed));
        Assert.Contains("999999", diff.First(line => line.Kind == DiffLineKind.Added).Text);
    }

    [Fact]
    public void AValueThatAppearedHasEveryLineAdded()
    {
        ImmutableList<DiffLine> diff = TextDiff.Unified(null, "first\nsecond");

        Assert.Equal([DiffLineKind.Added, DiffLineKind.Added], Kinds(diff));
    }

    [Fact]
    public void MarkersMatchWhatAUnifiedDiffLooksLike()
    {
        ImmutableList<DiffLine> diff = TextDiff.Unified("old", "new");

        Assert.Equal("-", diff.First(line => line.Kind == DiffLineKind.Removed).Marker);
        Assert.Equal("+", diff.First(line => line.Kind == DiffLineKind.Added).Marker);
    }

    [Fact]
    public void LineEndingsDoNotCountAsADifference()
    {
        // A baseline recorded on one platform and a current run on another must not report every line
        // as changed.
        Assert.Equal(
            DiffLineKind.Note,
            Assert.Single(TextDiff.Unified("one\r\ntwo", "one\ntwo")).Kind);
    }

    private static DiffLineKind[] Kinds(ImmutableList<DiffLine> diff)
        => [.. diff.Where(line => line.Kind != DiffLineKind.Note).Select(line => line.Kind)];
}
