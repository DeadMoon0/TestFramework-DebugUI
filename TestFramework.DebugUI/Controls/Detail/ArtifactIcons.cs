using System;
using System.Collections.Generic;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// The glyph and colour to draw an artifact with.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the schema the describer publishes, never on a CLR type name. That is the whole reason
/// the schema key exists: a SQL row gets the same icon whether it came from the EF-backed or the
/// ADO-backed implementation, and a consumer can add a type without this having to know about it.
/// </para>
/// <para>
/// An unrecognised key falls back to the generic glyph rather than to nothing, so a new artifact
/// type looks unfamiliar rather than broken.
/// </para>
/// </remarks>
public static class ArtifactIcons
{
    private const string GenericGlyph = "M3,2 H13 V14 H3 Z";

    private static readonly Dictionary<string, ArtifactIcon> Registry = new(StringComparer.Ordinal)
    {
        // A stack of records, the way a table is drawn everywhere.
        [DebugValueSchemaKeys.SqlRow] = new("M2,4 H14 M2,8 H14 M2,12 H14 M2,4 V12 M14,4 V12", "#FF62A8C9"),

        // A document with a folded corner.
        [DebugValueSchemaKeys.CosmosItem] = new("M4,2 H10 L13,5 V14 H4 Z M10,2 V5 H13", "#FF62C98F"),

        // A drum, as storage is conventionally drawn.
        [DebugValueSchemaKeys.Blob] = new("M3,4 C3,2 13,2 13,4 V12 C13,14 3,14 3,12 Z M3,4 C3,6 13,6 13,4", "#FFC9A862"),

        // A grid, for a keyed entity.
        [DebugValueSchemaKeys.TableEntity] = new("M2,2 H14 V14 H2 Z M2,8 H14 M8,2 V14", "#FFC46AC4"),

        // A file.
        [DebugValueSchemaKeys.File] = new("M4,2 H10 L13,5 V14 H4 Z", "#FF9A9A9A")
    };

    /// <summary>Finds the icon for a schema key.</summary>
    public static ArtifactIcon For(string? schemaKey)
        => schemaKey is not null && Registry.TryGetValue(schemaKey, out ArtifactIcon icon)
            ? icon
            : new ArtifactIcon(GenericGlyph, "#FF7A7A7A");
}

/// <summary>How one kind of artifact is drawn.</summary>
/// <param name="Glyph">Path data on a sixteen-unit square.</param>
/// <param name="Colour">The stroke colour, as a hex string.</param>
public readonly record struct ArtifactIcon(string Glyph, string Colour);
