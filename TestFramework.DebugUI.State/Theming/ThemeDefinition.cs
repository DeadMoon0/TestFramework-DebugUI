using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>Whether a theme is read on a dark ground or a light one.</summary>
/// <remarks>
/// Carried so the picker can group the two halves of an identity together. Nothing in the painting
/// depends on it — a light theme is light because its colours are, not because of this.
/// </remarks>
public enum ThemeMode
{
    Dark,
    Light
}

/// <summary>
/// The shape the window paints behind everything.
/// </summary>
/// <remarks>
/// <para>
/// A name and a number rather than a picture, so a theme somebody writes by hand can choose its
/// geometry without shipping any drawing code. The colours it is drawn in are the four
/// <c>Backdrop*</c> entries of the palette.
/// </para>
/// <para>
/// The blur belongs here rather than being fixed in the markup because geometry and waves want
/// different amounts of it: a lattice smeared far enough to make ridges read as depth is grey soup,
/// and ridges at a lattice's radius read as a drawing of some hills.
/// </para>
/// </remarks>
public sealed record ThemeBackdrop
{
    /// <summary>Which shape to draw.</summary>
    public required BackdropRecipe Recipe { get; init; }

    /// <summary>
    /// How far out of focus, in device-independent pixels at the backdrop's own scale.
    /// </summary>
    /// <remarks>
    /// Clamped rather than trusted, because it arrives from a file: a negative radius is not a blur and
    /// a very large one is a way to make the tool stop responding.
    /// </remarks>
    public required double Blur { get; init; }

    /// <summary>The largest blur a theme file may ask for.</summary>
    public const double MaximumBlur = 80;

    /// <summary>The blur as it will actually be drawn.</summary>
    public double SafeBlur => double.IsFinite(Blur) ? Math.Clamp(Blur, 0, MaximumBlur) : 0;
}

/// <summary>
/// One theme, complete: everything the window needs to paint itself.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Colours"/> is the resolved lookup rather than a <see cref="ThemePalette"/>, because a
/// theme read from a file is a built-in with some entries replaced and there is no way to say that in
/// a record whose members are all required. Replacing an entry cannot remove one, so a definition made
/// this way is as complete as the palette it started from.
/// </para>
/// <para>
/// Plain data, and nothing here is WPF. A definition can be built, merged and asserted without opening
/// a window, which is most of why the resolution below is worth having as its own step.
/// </para>
/// </remarks>
public sealed record ThemeDefinition
{
    /// <summary>How the settings file names this theme.</summary>
    /// <remarks>
    /// Lower-case and hyphenated — <c>slate-dark</c>. A custom theme's id is its file name without the
    /// extension, which is the only name a user can give one without a second field to get wrong.
    /// </remarks>
    public required string Id { get; init; }

    /// <summary>What the picker calls it.</summary>
    public required string Name { get; init; }

    /// <summary>The family the theme belongs to, so the picker can put its two modes together.</summary>
    public required string Family { get; init; }

    public required ThemeMode Mode { get; init; }

    public required ThemeBackdrop Backdrop { get; init; }

    /// <summary>Every colour, by the key the theme resources use.</summary>
    public required ImmutableDictionary<string, ThemeColour> Colours { get; init; }

    /// <summary>Whether this theme came off disk rather than being compiled in.</summary>
    public bool IsCustom { get; init; }

    /// <summary>
    /// One colour, by name.
    /// </summary>
    /// <remarks>
    /// Safe to index because a definition is complete by construction — but a key that is not a palette
    /// key at all is a programming mistake rather than a bad file, so it says so.
    /// </remarks>
    public ThemeColour Colour(string key)
        => Colours.TryGetValue(key, out ThemeColour colour)
            ? colour
            : throw new KeyNotFoundException($"'{key}' is not a theme colour.");

    /// <summary>
    /// Whether anything behind the window can be seen through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both layers have to let light through, and either one of them alone can stop it: an opaque tint
    /// covers the desktop however clear the backdrop is, and a backdrop that paints wall to wall covers
    /// it however thin the tint is. So this is an <c>and</c>, not an <c>or</c>.
    /// </para>
    /// <para>
    /// Asked rather than declared, because a theme that shows nothing gains nothing from being frosted —
    /// the compositor would blur a desktop that is entirely painted over. Deriving it means a theme
    /// cannot be wrong about itself, which a <c>Frosted</c> flag next to an opaque tint could be.
    /// </para>
    /// </remarks>
    public bool ShowsWhatIsBehind
        => Colour(nameof(ThemePalette.WindowTint)).Alpha < byte.MaxValue
        && Colour(nameof(ThemePalette.BackdropBase)).Alpha < byte.MaxValue;

    /// <summary>Builds a definition from an authored palette.</summary>
    public static ThemeDefinition From(
        string id,
        string name,
        string family,
        ThemeMode mode,
        ThemeBackdrop backdrop,
        ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(backdrop);
        ArgumentNullException.ThrowIfNull(palette);

        return new ThemeDefinition
        {
            Id = id,
            Name = name,
            Family = family,
            Mode = mode,
            Backdrop = backdrop,
            Colours = palette.ToLookup()
        };
    }
}
