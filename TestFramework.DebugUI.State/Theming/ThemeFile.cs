using System.Collections.Generic;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>
/// A theme as somebody writes it: a name, a built-in to start from, and only what they changed.
/// </summary>
/// <remarks>
/// <para>
/// Sparse on purpose. A file that had to list every colour would be wrong the day one more is added —
/// every hand-written theme in the world would start missing a key, and the tool would have to decide
/// what to do about it. (No count is quoted here for the same reason: it would be one more thing to
/// keep true.) Inheriting a built-in removes that question: what is
/// not mentioned is whatever the base says now, including colours that did not exist when the file was
/// written.
/// </para>
/// <para>
/// Mutable and nullable throughout, because this is the shape the deserialiser fills in and every
/// member of it is something a person could have left out or mistyped. Nothing reads it directly —
/// <see cref="ThemeResolver"/> turns it into a <see cref="ThemeDefinition"/>, or into a list of
/// reasons why it could not.
/// </para>
/// </remarks>
public sealed class ThemeFile
{
    /// <summary>What the picker should call it. Falls back to the file name.</summary>
    public string? Name { get; set; }

    /// <summary>The id of the built-in this theme starts from.</summary>
    public string? Inherits { get; set; }

    /// <summary>Which family the picker files it under. Falls back to the base theme's.</summary>
    public string? Family { get; set; }

    /// <summary>The colours to replace, by key. Anything absent stays as the base theme has it.</summary>
    public Dictionary<string, string>? Colours { get; set; }

    /// <summary>
    /// The same thing, spelled the other way.
    /// </summary>
    /// <remarks>
    /// Accepted because the alternative is a file that looks right, parses, loads, and changes nothing
    /// — and there is no way for the person who wrote it to tell which of the two spellings this tool
    /// wanted without being told.
    /// </remarks>
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>What to paint behind everything. Absent means the base theme's backdrop.</summary>
    public ThemeFileBackdrop? Backdrop { get; set; }
}

/// <summary>The backdrop half of a theme file.</summary>
public sealed class ThemeFileBackdrop
{
    /// <summary>A <see cref="BackdropRecipe"/> by name, matched without regard to case.</summary>
    public string? Recipe { get; set; }

    /// <summary>How far out of focus. Absent means the base theme's radius.</summary>
    public double? Blur { get; set; }
}
