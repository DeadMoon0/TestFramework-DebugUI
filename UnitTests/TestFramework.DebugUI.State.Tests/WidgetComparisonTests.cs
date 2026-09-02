using System;
using System.Collections.Immutable;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers setting a run's evidence beside the last run that passed.
/// </summary>
/// <remarks>
/// A screenshot of a page that has changed is the most legible difference a run can show and the
/// hardest to state in words, so this is the comparison a reader is most likely to act on directly.
/// It is the value comparison underneath — a widget is described the way a value is — and what is
/// tested here is the part that is not: which widget is set against which.
/// </remarks>
public class WidgetComparisonTests
{
    [Fact]
    public void TwoPicturesOfOnePageAreComparedByContentRatherThanByWhereTheyWereWritten()
    {
        // Every run writes its widgets into its own folder, so the paths always differ. Comparing
        // anything but the content would report every screenshot as changed on every run.
        RunGraph baseline = Run(("confirmation", "aaa111", @"C:\runs\one\widgets\confirmation.png"));
        RunGraph current = Run(("confirmation", "aaa111", @"C:\runs\two\widgets\confirmation.png"));

        Assert.Equal(ValueChangeKind.Unchanged, Compare(baseline, current).ChangeForWidget("confirmation")!.Change);
    }

    [Fact]
    public void APageThatLooksDifferentIsReportedAsChanged()
    {
        RunGraph baseline = Run(("confirmation", "aaa111", @"C:\runs\one\widgets\confirmation.png"));
        RunGraph current = Run(("confirmation", "bbb222", @"C:\runs\two\widgets\confirmation.png"));

        ValueChange change = Compare(baseline, current).ChangeForWidget("confirmation")!;

        Assert.Equal(ValueChangeKind.Changed, change.Change);

        // Both sides are carried, because the panel showing this puts the two pictures side by side
        // and needs a file for each.
        Assert.Equal("aaa111", change.Baseline!.Body!.ContentHash);
        Assert.Equal("bbb222", change.Current!.Body!.ContentHash);
    }

    [Fact]
    public void EvidenceOnlyOneRunProducedIsReportedAsAddedOrRemoved()
    {
        RunGraph baseline = Run(("confirmation", "aaa111", @"C:\runs\one\widgets\confirmation.png"));
        RunGraph current = Run(("failure", "bbb222", @"C:\runs\two\widgets\failure.png"));

        ValueDiff diff = Compare(baseline, current);

        Assert.Equal(ValueChangeKind.Added, diff.ChangeForWidget("failure")!.Change);
        Assert.Equal(ValueChangeKind.Removed, diff.ChangeForWidget("confirmation")!.Change);
    }

    [Fact]
    public void TheNewestPictureOfANameIsTheOneCompared()
    {
        // A run that photographs the same page twice holds two widgets of one name - the store
        // versions them - and the newest is the state that thing ended in. Comparing the first
        // against the baseline's last would set two different moments beside each other and call the
        // difference a change.
        RunGraph baseline = Run(("page", "same", @"C:\runs\one\widgets\page.png"));

        RunGraph current = Run(
            ("page", "early", @"C:\runs\two\widgets\page.png"),
            ("page", "same", @"C:\runs\two\widgets\page.v2.png"));

        Assert.Equal(ValueChangeKind.Unchanged, Compare(baseline, current).ChangeForWidget("page")!.Change);
    }

    [Fact]
    public void WidgetsDoNotCountAsValuesThatChanged()
    {
        // The header this feeds says how many values differ. A screenshot is evidence about a run
        // rather than a value it produced, and counting it there would answer a different question
        // than the one the header asks.
        RunGraph baseline = Run(("confirmation", "aaa111", @"C:\runs\one\widgets\confirmation.png"));
        RunGraph current = Run(("confirmation", "bbb222", @"C:\runs\two\widgets\confirmation.png"));

        ValueDiff diff = Compare(baseline, current);

        Assert.Equal(ValueChangeKind.Changed, diff.ChangeForWidget("confirmation")!.Change);
        Assert.Equal(0, diff.ChangedCount);
    }

    [Fact]
    public void ARunWithNoEvidenceComparesToNothingRatherThanFailing()
    {
        Assert.Empty(Compare(Run(), Run()).Widgets);
    }

    private static ValueDiff Compare(RunGraph baseline, RunGraph current)
        => RunBaselineSelector.Compare(current, RunBaselineSelector.BaselineFrom(Earlier, baseline));

    /// <summary>The run the baseline is taken from; only its identity matters here.</summary>
    private static readonly RunSummary Earlier = new()
    {
        SessionId = "earlier",
        Name = "timeline",
        StartedAtUtc = DateTimeOffset.UnixEpoch,
        FullyQualifiedName = "Suite.TheTest",
        IsFinished = true
    };

    /// <summary>A run holding the named pictures, in the order they were produced.</summary>
    private static RunGraph Run(params (string Name, string Hash, string Path)[] widgets)
    {
        ImmutableList<WidgetNode> nodes = ImmutableList<WidgetNode>.Empty;

        foreach ((string name, string hash, string path) in widgets)
        {
            nodes = nodes.Add(new WidgetNode
            {
                Kind = WidgetKinds.Screenshot,
                Name = name,
                OccurredAtUtc = DateTimeOffset.UnixEpoch,
                Description = Picture(hash, path)
            });
        }

        return RunGraph.Empty with { Widgets = nodes };
    }

    private static ValueDescription Picture(string hash, string path) => new()
    {
        Summary = "a page",
        Shape = DebugValueShape.Binary,
        Preview = new ValuePreview
        {
            Form = DebugPreviewForm.Image,
            Text = string.Empty,
            IsTruncated = true,
            SizeInBytes = 120_000
        },
        Body = new ValueBody
        {
            Path = path,
            RelativePath = "widgets/" + System.IO.Path.GetFileName(path),
            SizeInBytes = 120_000,
            ContentHash = hash
        }
    };
}
