using System;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State.Annotations;

/// <summary>
/// What somebody drew on a run.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the journal, always. The journal is what the framework observed; this is what a person thinks
/// about it, and the moment those two live in one file nobody can tell the evidence from the opinion. That
/// distinction is most of what this tool is worth, so annotations are a sidecar and never an edit.
/// </para>
/// <para>
/// Marks are in board coordinates. The layout is a pure function of the run graph and a set of constants, so the
/// same journal lays out identically on any machine, at any window size, at any zoom — panning and zooming are
/// transforms over the top and move the marks with the board for free. What the coordinates cannot survive is a
/// later build whose layout engine places things differently, which is what <see cref="LayoutVersion"/> is for.
/// </para>
/// </remarks>
public sealed record RunAnnotations
{
    /// <summary>The layout of this file, so a later reader can refuse what it does not understand.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Gets the version of this file's own shape.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Gets the run these marks are about.</summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the version of the board layout the marks were drawn against.
    /// </summary>
    /// <remarks>
    /// Board coordinates are only meaningful next to the arrangement they were drawn on. Routing, card sizes and
    /// connector positions have all changed across builds of this tool; when they change again, marks drawn
    /// before are still shown, but the reader is told the board has moved under them rather than left to wonder
    /// why an arrow points at nothing.
    /// </remarks>
    public int LayoutVersion { get; init; }

    /// <summary>Gets the marks, oldest first, which is the order they are drawn in.</summary>
    public ImmutableList<Annotation> Marks { get; init; } = [];

    /// <summary>Whether there is anything here worth keeping.</summary>
    public bool IsEmpty => Marks.Count == 0;

    /// <summary>The same set with one more mark on top.</summary>
    public RunAnnotations With(Annotation mark)
    {
        ArgumentNullException.ThrowIfNull(mark);

        return this with { Marks = Marks.Add(mark) };
    }

    /// <summary>The same set with the most recent mark taken off.</summary>
    public RunAnnotations WithoutLast()
        => Marks.Count == 0 ? this : this with { Marks = Marks.RemoveAt(Marks.Count - 1) };

    /// <summary>The same set with one mark removed by identity.</summary>
    public RunAnnotations Without(string markId)
        => this with { Marks = [.. Marks.Where(mark => !string.Equals(mark.Id, markId, StringComparison.Ordinal))] };

    /// <summary>
    /// The same set with every author replaced by a number.
    /// </summary>
    /// <remarks>
    /// For an anonymous share. Numbered in the order they first appear rather than collapsed into one, because a
    /// review is a conversation and losing who replied to whom would make a thread of marks unreadable while
    /// hiding nothing extra.
    /// </remarks>
    public RunAnnotations Anonymised()
    {
        System.Collections.Generic.Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

        return this with
        {
            Marks =
            [
                .. Marks.Select(mark => mark with { Author = Alias(mark.Author, aliases) })
            ]
        };
    }

    private static string? Alias(string? author, System.Collections.Generic.Dictionary<string, string> aliases)
    {
        if (string.IsNullOrWhiteSpace(author))
            return null;

        if (!aliases.TryGetValue(author, out string? alias))
        {
            alias = $"Author {aliases.Count + 1}";
            aliases[author] = alias;
        }

        return alias;
    }
}

/// <summary>One thing somebody drew.</summary>
public sealed record Annotation
{
    /// <summary>Gets the mark's own identity, so it can be removed without counting from an end.</summary>
    public required string Id { get; init; }

    /// <summary>Gets what kind of mark it is.</summary>
    public required AnnotationKind Kind { get; init; }

    /// <summary>
    /// Gets the ink, as a palette name rather than a colour.
    /// </summary>
    /// <remarks>
    /// A name, so the theme decides what it looks like and a mark drawn today still reads on a surface restyled
    /// tomorrow. It also keeps human ink out of the board's own colour language, where red, amber and green
    /// already mean failed, unproven and passed.
    /// </remarks>
    public required string Ink { get; init; }

    /// <summary>Gets how thick the stroke is, in board units.</summary>
    public double Thickness { get; init; } = 3;

    /// <summary>
    /// Gets the points that define the mark.
    /// </summary>
    /// <remarks>
    /// A freehand stroke is many points; a rectangle, an ellipse and an arrow are two — where the drag started
    /// and where it ended; a note is one. Kept as one list rather than a shape per kind, because every one of
    /// them has to be moved, scaled and serialised the same way.
    /// </remarks>
    public ImmutableList<AnnotationPoint> Points { get; init; } = [];

    /// <summary>Gets the words, for a note.</summary>
    public string? Text { get; init; }

    /// <summary>Gets who drew it.</summary>
    public string? Author { get; init; }

    /// <summary>Gets when it was drawn.</summary>
    public DateTimeOffset AtUtc { get; init; }
}

/// <summary>The kinds of mark that can be made.</summary>
public enum AnnotationKind
{
    /// <summary>A drawn line, smoothed.</summary>
    Freehand,

    /// <summary>A box, from corner to corner.</summary>
    Rectangle,

    /// <summary>An ellipse in the box from corner to corner.</summary>
    Ellipse,

    /// <summary>A line with a head on the end, for pointing at something.</summary>
    Arrow,

    /// <summary>Words placed on the board.</summary>
    Text
}

/// <summary>A point on the board.</summary>
/// <remarks>
/// Its own type rather than a framework one, because this project has no user interface in it and the file this
/// ends up in has to be readable by anything.
/// </remarks>
public readonly record struct AnnotationPoint(double X, double Y);
