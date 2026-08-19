using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using Newtonsoft.Json.Linq;

namespace TestFramework.DebugUI.State;

/// <summary>
/// What a value is, in the UI's own terms.
/// </summary>
/// <remarks>
/// <para>
/// Core's description is carried across the wire; this is the projected copy the state tree holds.
/// The reason for a copy rather than the original is equality. A C# record compares array members by
/// reference, and Core's description holds arrays — so two descriptions stating identical facts
/// compare unequal, every update looks like a change, and every binding watching a value re-emits on
/// every write in the run.
/// </para>
/// <para>
/// That is not a theoretical concern here: the value dictionaries are updated with
/// <c>SetItem</c>, which returns the dictionary unchanged when the new value equals the old one. Get
/// the equality wrong and that short-circuit silently stops working.
/// </para>
/// </remarks>
public sealed record ValueDescription
{
    /// <summary>A description of nothing, for a value that has not been observed.</summary>
    public static ValueDescription Empty { get; } = new();

    /// <summary>Gets the one line to show where there is room for only one.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets what shape of value this is, which is what a renderer is picked by.</summary>
    public DebugValueShape Shape { get; init; } = DebugValueShape.Unknown;

    /// <summary>Gets the named facts about the value, in the order they are worth reading.</summary>
    public ImmutableList<ValueFact> Facts { get; init; } = [];

    /// <summary>Gets the short labels worth showing beside the value, such as a lifecycle state.</summary>
    public ImmutableList<string> Badges { get; init; } = [];

    /// <summary>Gets a bounded look at the value's own content, when there is one.</summary>
    public ValuePreview? Preview { get; init; }

    /// <summary>Gets where the whole value was written, when it was too big to send.</summary>
    public ValueBody? Body { get; init; }

    /// <summary>Whether there is more to this value than what arrived.</summary>
    public bool HasMore => Body is not null || Preview?.IsTruncated == true;

    /// <summary>Projects Core's description into the UI's own.</summary>
    public static ValueDescription From(DebugValueDescription? description)
    {
        // Deliberately not a reference check against Core's Empty singleton. A description that
        // arrived over the wire is never that instance — and when a deserializer was populating the
        // singleton in place rather than replacing it, a reference check was true for every value in
        // the run and silently threw away every fact any of them carried.
        if (description is null || IsEmpty(description))
            return Empty;

        return new ValueDescription
        {
            Summary = description.Summary,
            Shape = description.Shape,
            Facts = [.. description.Fields.Select(field => new ValueFact
            {
                Name = field.Name,
                Value = field.Value,
                IsTruncated = field.IsTruncated
            })],
            Badges = [.. description.Badges],
            Preview = description.Preview is null
                ? null
                : new ValuePreview
                {
                    Form = description.Preview.Form,
                    Text = description.Preview.Text,
                    IsTruncated = description.Preview.IsTruncated,
                    SizeInBytes = description.Preview.SizeInBytes
                },
            Body = description.Body is null
                ? null
                : new ValueBody
                {
                    Path = description.Body.Path,
                    RelativePath = description.Body.RelativePath,
                    SizeInBytes = description.Body.SizeInBytes,
                    ContentHash = description.Body.ContentHash
                }
        };
    }

    /// <summary>Whether a description says nothing, which is what a replayed older value looks like.</summary>
    private static bool IsEmpty(DebugValueDescription description)
        => description.Summary.Length == 0
           && description.Fields.Length == 0
           && description.Badges.Length == 0
           && description.Preview is null
           && description.Body is null;

    /// <summary>Compares the facts and badges by content, which the generated members would not.</summary>
    public bool Equals(ValueDescription? other)
        => other is not null
           && string.Equals(Summary, other.Summary, StringComparison.Ordinal)
           && Shape == other.Shape
           && Equals(Preview, other.Preview)
           && Equals(Body, other.Body)
           && Facts.SequenceEqual(other.Facts)
           && Badges.SequenceEqual(other.Badges, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(Summary, StringComparer.Ordinal);
        hash.Add(Shape);
        hash.Add(Preview);
        hash.Add(Body);
        hash.Add(Facts.Count);
        hash.Add(Badges.Count);

        return hash.ToHashCode();
    }
}

/// <summary>One named fact about a value.</summary>
public sealed record ValueFact
{
    /// <summary>Gets the fact's name, such as <c>reference</c> or <c>length</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the fact, typed as it stands.</summary>
    public required JToken Value { get; init; }

    /// <summary>Gets the fact as text, for a row that only has to print it.</summary>
    public string Text => DebugJson.Text(Value);

    /// <summary>Gets whether the text was cut to fit, so a consumer can say so rather than imply it.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>
    /// Compares the value by content.
    /// </summary>
    /// <remarks>
    /// A <see cref="JToken"/> compares by reference, and two tokens deserialized from the same bytes are never
    /// the same instance. Without this, every value update would look like a change and re-emit to every
    /// binding watching it — the same trap the version list above is guarded against.
    /// </remarks>
    public bool Equals(ValueFact? other)
        => other is not null
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && IsTruncated == other.IsTruncated
           && JToken.DeepEquals(Value, other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
}

/// <summary>A bounded look at a value's content.</summary>
public sealed record ValuePreview
{
    /// <summary>Gets how the content should be read.</summary>
    public DebugPreviewForm Form { get; init; }

    /// <summary>Gets the content, up to the producer's preview budget.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets whether there is more content than arrived here.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Gets the full size of the content, when it is known.</summary>
    public long? SizeInBytes { get; init; }
}

/// <summary>Where the whole of a value was written.</summary>
/// <remarks>
/// The file is in the run's own output rather than anywhere debugger-private, so the UI opens it
/// directly instead of asking for it back over the transport.
/// </remarks>
public sealed record ValueBody
{
    /// <summary>Gets the full path to the file holding the value.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets the path relative to the run's output folder, such as <c>values/orderId.json</c>.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Gets the size of the file in bytes.</summary>
    public long SizeInBytes { get; init; }

    /// <summary>Gets the hash of the content.</summary>
    public string ContentHash { get; init; } = string.Empty;
}
