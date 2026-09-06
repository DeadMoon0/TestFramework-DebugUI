using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.DebugUI.State.Theming;
using Xunit;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// The rules about the shipped themes that a type cannot state.
/// </summary>
/// <remarks>
/// Completeness is not among them: <see cref="ThemePalette"/> requires every colour, so a palette that
/// forgot one does not compile and there is nothing here to check. What is left is the handful of things
/// about the <em>set</em> — that ids are unique, that a mode has a partner, that the default is real.
/// </remarks>
public class BuiltInThemesTests
{
    [Fact]
    public void EveryIdIsUnique()
        => Assert.Empty(BuiltInThemes.All
            .GroupBy(theme => theme.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key));

    [Fact]
    public void TheDefaultIsOneOfThem()
        => Assert.Equal(BuiltInThemes.DefaultId, BuiltInThemes.Get(BuiltInThemes.DefaultId).Id);

    /// <summary>
    /// That an id nobody answers to lands on the default rather than throwing.
    /// </summary>
    /// <remarks>
    /// This is what a settings file written by a newer build looks like from an older one, and what a
    /// custom theme that has since been deleted looks like from any of them. Neither is worth an error.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("slate-mauve")]
    public void AnUnknownIdFallsBackToTheDefault(string? id)
        => Assert.Equal(BuiltInThemes.DefaultId, BuiltInThemes.Get(id).Id);

    /// <summary>
    /// That every identity comes in both modes.
    /// </summary>
    /// <remarks>
    /// The promise the picker makes by grouping them. Somebody who has chosen Ember and then wants a
    /// light window should find Ember Light rather than have to leave the family to get one.
    /// </remarks>
    [Fact]
    public void EveryFamilyHasBothModes()
    {
        List<string> lopsided = BuiltInThemes.All
            .GroupBy(theme => theme.Family, StringComparer.Ordinal)
            .Where(group => group.Select(theme => theme.Mode).Distinct().Count() != 2)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(lopsided);
    }

    /// <summary>
    /// That a see-through theme is see-through in both of the ways it has to be, and still readable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Painting nothing is only half of it. A theme with the Clear recipe and an opaque window tint
    /// shows a flat slab instead of the desktop, and one with a transparent tint but a recipe that
    /// paints would cover the desktop it just asked to see.
    /// </para>
    /// <para>
    /// The lower bound is the half that was learned by looking. The first Glass Dark carried a
    /// thirty-five percent tint, which over a white desktop leaves white chrome text on a light grey
    /// sheet — see-through, and unusable. A tint has to be thin enough to be glass and thick enough to
    /// be a ground.
    /// </para>
    /// </remarks>
    [Fact]
    public void AThemeThatPaintsNothingLetsTheDesktopThrough()
    {
        byte opaqueTheme = BuiltInThemes.SlateDark.Colour(nameof(ThemePalette.WindowTint)).Alpha;

        foreach (ThemeDefinition theme in BuiltInThemes.All.Where(t => t.Backdrop.Recipe == BackdropRecipe.Clear))
        {
            byte tint = theme.Colour(nameof(ThemePalette.WindowTint)).Alpha;

            Assert.Equal(0, theme.Colour(nameof(ThemePalette.BackdropBase)).Alpha);
            Assert.True(tint < opaqueTheme, $"{theme.Id} is no more see-through than an opaque theme.");
            Assert.True(tint >= 0xA0, $"{theme.Id} is too thin a tint to read chrome text against.");
        }
    }

    /// <summary>
    /// That a theme meant to be read at maximum contrast is not also translucent.
    /// </summary>
    /// <remarks>
    /// The accessibility mode's whole claim is that nothing composites: no acrylic to be turned off by
    /// battery saver, no wash for a wallpaper to show through, and a surface that is the colour it says.
    /// </remarks>
    [Fact]
    public void TheContrastThemesAreEntirelyOpaque()
    {
        string[] opaque =
        [
            nameof(ThemePalette.WindowTint),
            nameof(ThemePalette.BackdropBase),
            nameof(ThemePalette.SurfacePanel),
            nameof(ThemePalette.SurfaceSunken),
            nameof(ThemePalette.SurfaceCard),
            nameof(ThemePalette.SurfaceOverlay)
        ];

        foreach (ThemeDefinition theme in BuiltInThemes.All.Where(t => t.Family == "Contrast"))
        {
            foreach (string key in opaque)
                Assert.Equal(0xFF, theme.Colour(key).Alpha);
        }
    }

    /// <summary>
    /// That every shipped theme is one somebody can read.
    /// </summary>
    /// <remarks>
    /// The same check a hand-written theme gets, turned on its authors. A palette tuned by eye in one
    /// mode is exactly the kind of thing that ends up with grey text on a grey card in the other.
    /// </remarks>
    [Fact]
    public void EveryBuiltInThemeIsLegible()
    {
        List<string> unreadable = BuiltInThemes.All
            .Where(theme => ThemeResolver.Contrast(
                theme.Colour(nameof(ThemePalette.TextPrimary)),
                theme.Colour(nameof(ThemePalette.SurfaceCard))) < 4.5)
            .Select(theme => theme.Id)
            .ToList();

        Assert.Empty(unreadable);
    }
}
