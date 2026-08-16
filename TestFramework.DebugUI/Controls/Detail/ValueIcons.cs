using System;
using System.Collections.Generic;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// The glyph and colour to draw a value with.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the schema the producer publishes, never on a CLR type name. That is the whole reason the
/// schema key exists: a SQL row gets the same icon whether it came from the EF-backed or the
/// ADO-backed implementation, and a consumer can add a type without this having to know about it.
/// </para>
/// <para>
/// Two families are registered here. Artifacts declare what kind of thing they are, so they get a
/// glyph of that thing. A plain variable has no such claim to make — it is whatever a step assigned —
/// so it is drawn by its shape: a list looks like a list whether it holds orders or integers.
/// </para>
/// <para>
/// An unrecognised key falls back to the generic glyph rather than to nothing, so a new type looks
/// unfamiliar rather than broken.
/// </para>
/// </remarks>
public static class ValueIcons
{
    private const string GenericGlyph = "M3,2 H13 V14 H3 Z";

    private static readonly Dictionary<string, ValueIcon> Registry = new(StringComparer.Ordinal)
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
        [DebugValueSchemaKeys.File] = new("M4,2 H10 L13,5 V14 H4 Z", "#FF9A9A9A"),

        // One thing, on its own.
        [DebugValueSchemaKeys.Scalar] = new("M8,3 L13,8 L8,13 L3,8 Z", "#FF8FA7C9"),

        // Lines of text.
        [DebugValueSchemaKeys.Text] = new("M3,4 H13 M3,8 H13 M3,12 H9", "#FF8FA7C9"),

        // A run of cells, for an ordered sequence.
        [DebugValueSchemaKeys.Collection] = new("M2,5 H14 V11 H2 Z M6,5 V11 M10,5 V11", "#FF8FA7C9"),

        // Keys on the left, values on the right.
        [DebugValueSchemaKeys.Dictionary] = new("M2,4 H6 V12 H2 Z M8,4 H14 M8,8 H14 M8,12 H14", "#FF8FA7C9"),

        // Bits.
        [DebugValueSchemaKeys.Binary] = new("M3,3 H7 V7 H3 Z M9,3 H13 V7 H9 Z M3,9 H7 V13 H3 Z M9,9 H13 V13 H9 Z", "#FFC9A862"),

        // A composite: a body with members hanging off it.
        [DebugValueSchemaKeys.Object] = new("M6,2 H13 V14 H6 Z M3,5 H6 M3,8 H6 M3,11 H6", "#FF8FA7C9"),

        // Nothing. Drawn as the empty set rather than as a box that happens to be blank.
        [DebugValueSchemaKeys.Null] = new("M3,3 L13,13 M8,3 A5,5 0 1 1 8,13 A5,5 0 1 1 8,3", "#FF6A6A6A")
    };

    /// <summary>Finds the icon for a schema key.</summary>
    public static ValueIcon For(string? schemaKey)
        => schemaKey is not null && Registry.TryGetValue(schemaKey, out ValueIcon icon)
            ? icon
            : new ValueIcon(GenericGlyph, "#FF7A7A7A");
}

/// <summary>How one kind of value is drawn.</summary>
/// <param name="Glyph">Path data on a sixteen-unit square.</param>
/// <param name="Colour">The stroke colour, as a hex string.</param>
public readonly record struct ValueIcon(string Glyph, string Colour);
