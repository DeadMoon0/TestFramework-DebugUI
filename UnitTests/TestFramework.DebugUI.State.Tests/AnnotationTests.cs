using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using TestFramework.DebugUI.State.Annotations;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the marks a reader draws on a run: how they are shaped, kept and shared.
/// </summary>
public sealed class AnnotationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "annotations-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the folder these write under.</summary>
    public AnnotationTests() => Directory.CreateDirectory(root);

    private string Journal
    {
        get
        {
            string path = Path.Combine(root, "20260818-120000-abc.ndjson");

            if (!File.Exists(path))
                File.WriteAllText(path, "{}");

            return path;
        }
    }

    [Fact]
    public void MarksSitBesideTheJournalRatherThanInIt()
    {
        // The whole basis of the feature. A journal edited to carry somebody's opinion is a journal you can no
        // longer trust as a record of what happened.
        string annotations = AnnotationStore.PathFor(Journal);

        Assert.Equal(Path.GetDirectoryName(Journal), Path.GetDirectoryName(annotations));
        Assert.EndsWith(".annotations.json", annotations, StringComparison.Ordinal);
        Assert.NotEqual(Journal, annotations);
    }

    [Fact]
    public void ARunWithNothingDrawnOnItLoadsEmpty()
    {
        AnnotationStore store = new();

        RunAnnotations loaded = store.Load(Journal, "abc");

        Assert.True(loaded.IsEmpty);
        Assert.Equal("abc", loaded.SessionId);
    }

    [Fact]
    public void MarksSurviveBeingWrittenAndReadBack()
    {
        AnnotationStore store = new();

        RunAnnotations drawn = new RunAnnotations { SessionId = "abc", LayoutVersion = 7 }
            .With(Stroke("one", "Cyan"))
            .With(Note("two", "look here"));

        Assert.True(store.Save(Journal, drawn));

        RunAnnotations loaded = store.Load(Journal, "abc");

        Assert.Equal(2, loaded.Marks.Count);
        Assert.Equal(7, loaded.LayoutVersion);
        Assert.Equal("Cyan", loaded.Marks[0].Ink);
        Assert.Equal("look here", loaded.Marks[1].Text);
        Assert.Equal(3, loaded.Marks[0].Points.Count);
    }

    [Fact]
    public void UndoingTheLastMarkRemovesTheFileWhenNothingIsLeft()
    {
        // So that "has anyone drawn on this run" stays answerable by looking, rather than by opening an empty
        // file and finding out.
        AnnotationStore store = new();

        store.Save(Journal, new RunAnnotations { SessionId = "abc" }.With(Stroke("one", "Cyan")));

        Assert.True(File.Exists(AnnotationStore.PathFor(Journal)));

        store.Save(Journal, new RunAnnotations { SessionId = "abc" });

        Assert.False(File.Exists(AnnotationStore.PathFor(Journal)));
    }

    [Fact]
    public void MarksFromANewerToolAreShownAsNothingRatherThanGuessedAt()
    {
        string path = AnnotationStore.PathFor(Journal);

        File.WriteAllText(path, """{ "FormatVersion": 99, "SessionId": "abc", "Marks": [] }""");

        string? complaint = null;
        AnnotationStore store = new(message => complaint = message);

        Assert.True(store.Load(Journal, "abc").IsEmpty);
        Assert.NotNull(complaint);
    }

    [Fact]
    public void AMalformedFileDoesNotStopTheRunOpening()
    {
        File.WriteAllText(AnnotationStore.PathFor(Journal), "this is not json");

        string? complaint = null;
        AnnotationStore store = new(message => complaint = message);

        Assert.True(store.Load(Journal, "abc").IsEmpty);
        Assert.NotNull(complaint);
    }

    [Fact]
    public void UndoTakesTheMostRecentMark()
    {
        RunAnnotations drawn = new RunAnnotations { SessionId = "abc" }
            .With(Stroke("one", "Cyan"))
            .With(Stroke("two", "Magenta"));

        Assert.Equal("one", Assert.Single(drawn.WithoutLast().Marks).Id);
    }

    [Fact]
    public void AMarkCanBeRemovedByIdentityRatherThanByPosition()
    {
        RunAnnotations drawn = new RunAnnotations { SessionId = "abc" }
            .With(Stroke("one", "Cyan"))
            .With(Stroke("two", "Magenta"))
            .With(Stroke("three", "White"));

        Assert.Equal(["one", "three"], drawn.Without("two").Marks.Select(mark => mark.Id));
    }

    [Fact]
    public void AnonymisingNumbersTheAuthorsInsteadOfCollapsingThem()
    {
        // A review is a conversation. Turning three people into "anonymous" hides nothing extra and makes the
        // thread unreadable.
        RunAnnotations drawn = new RunAnnotations { SessionId = "abc" }
            .With(Stroke("one", "Cyan") with { Author = "alex" })
            .With(Stroke("two", "Magenta") with { Author = "sam" })
            .With(Stroke("three", "White") with { Author = "alex" });

        RunAnnotations hidden = drawn.Anonymised();

        Assert.Equal(["Author 1", "Author 2", "Author 1"], hidden.Marks.Select(mark => mark.Author));
        Assert.DoesNotContain("alex", hidden.Marks.Select(mark => mark.Author));
    }

    [Fact]
    public void AnonymisingLeavesTheDrawingItself()
    {
        RunAnnotations drawn = new RunAnnotations { SessionId = "abc" }
            .With(Note("one", "the payload is wrong") with { Author = "alex" });

        Annotation mark = Assert.Single(drawn.Anonymised().Marks);

        Assert.Equal("the payload is wrong", mark.Text);
        Assert.Equal(AnnotationKind.Text, mark.Kind);
    }

    [Fact]
    public void SimplifyingKeepsTheEndsAndDropsWhatSaysNothing()
    {
        // Nine points along a straight line carry the same shape as two.
        ImmutableList<AnnotationPoint> straight =
        [
            .. Enumerable.Range(0, 9).Select(step => new AnnotationPoint(step * 10, 0))
        ];

        ImmutableList<AnnotationPoint> kept = Smoothing.Simplify(straight);

        Assert.Equal(2, kept.Count);
        Assert.Equal(new AnnotationPoint(0, 0), kept[0]);
        Assert.Equal(new AnnotationPoint(80, 0), kept[^1]);
    }

    [Fact]
    public void SimplifyingKeepsACorner()
    {
        // The point that carries the shape has to survive, or a drawn angle becomes a straight line.
        ImmutableList<AnnotationPoint> corner =
        [
            new(0, 0), new(10, 0), new(20, 0), new(20, 10), new(20, 20)
        ];

        Assert.Contains(new AnnotationPoint(20, 0), Smoothing.Simplify(corner));
    }

    [Fact]
    public void SimplifyingRemovesTheTremorInAStroke()
    {
        // What a slow hand actually produces: a straight intention with sub-pixel noise on it.
        AnnotationPoint[] shaky =
        [
            .. Enumerable.Range(0, 200).Select(step => new AnnotationPoint(step, step % 2 == 0 ? 0.4 : -0.4))
        ];

        ImmutableList<AnnotationPoint> kept = Smoothing.Simplify(shaky);

        Assert.True(kept.Count < 10, $"expected the noise to go, kept {kept.Count} of 200");
    }

    [Fact]
    public void SimplifyingAStrokeThatDoublesBackKeepsBothLegs()
    {
        // Measured to the segment rather than to the infinite line through it: the far point of a there-and-back
        // stroke is on that line, and measuring to the line would throw the whole shape away.
        ImmutableList<AnnotationPoint> there = [new(0, 0), new(50, 0), new(100, 0), new(50, 0), new(0, 0)];

        Assert.Contains(new AnnotationPoint(100, 0), Smoothing.Simplify(there));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SimplifyingSomethingTooShortToSimplifyLeavesItAlone(int count)
    {
        ImmutableList<AnnotationPoint> points =
        [
            .. Enumerable.Range(0, count).Select(step => new AnnotationPoint(step, step))
        ];

        Assert.Equal(count, Smoothing.Simplify(points).Count);
    }

    [Fact]
    public void ALongStrokeDoesNotOverflowTheStack()
    {
        // Iterative on purpose. A drawing must not be able to bring the window down by being long.
        AnnotationPoint[] long_ =
        [
            .. Enumerable.Range(0, 100_000).Select(step => new AnnotationPoint(step, Math.Sin(step / 7.0) * 40))
        ];

        Assert.NotEmpty(Smoothing.Simplify(long_));
    }

    [Fact]
    public void ACurvePassesThroughEveryPointItWasGiven()
    {
        // Interpolating, not approximating. The points are where a hand was, and a curve that merely approaches
        // them has redrawn somebody's intent.
        ImmutableList<AnnotationPoint> points = [new(0, 0), new(30, 40), new(60, 0), new(90, 40)];

        ImmutableList<CurveSegment> segments = Smoothing.Curve(points);

        Assert.Equal(3, segments.Count);
        Assert.Equal(points[1], segments[0].End);
        Assert.Equal(points[2], segments[1].End);
        Assert.Equal(points[3], segments[2].End);
    }

    [Fact]
    public void ACurveNeedsTwoPointsToExist()
    {
        Assert.Empty(Smoothing.Curve([new AnnotationPoint(1, 1)]));
        Assert.Empty(Smoothing.Curve([]));
    }

    [Fact]
    public void TheFirstSegmentLeavesTheStartRatherThanJumping()
    {
        // The ends are duplicated so the first and last segments have a neighbour; without it a stroke begins
        // with a straight stub before the curve takes over.
        ImmutableList<AnnotationPoint> points = [new(0, 0), new(10, 10), new(20, 0)];

        CurveSegment first = Smoothing.Curve(points)[0];

        Assert.True(first.FirstControl.X is > 0 and < 10, $"control point drifted to {first.FirstControl.X}");
    }

    private static Annotation Stroke(string id, string ink) => new()
    {
        Id = id,
        Kind = AnnotationKind.Freehand,
        Ink = ink,
        Points = [new AnnotationPoint(0, 0), new AnnotationPoint(10, 10), new AnnotationPoint(20, 0)],
        AtUtc = DateTimeOffset.UnixEpoch
    };

    private static Annotation Note(string id, string text) => new()
    {
        Id = id,
        Kind = AnnotationKind.Text,
        Ink = "White",
        Points = [new AnnotationPoint(5, 5)],
        Text = text,
        AtUtc = DateTimeOffset.UnixEpoch
    };

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not worth failing a test over.
        }
    }
}
