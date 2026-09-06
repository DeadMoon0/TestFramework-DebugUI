using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;
using Xunit;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// That every backdrop draws, draws the same thing twice, and stays inside its frame.
/// </summary>
/// <remarks>
/// A backdrop is the one part of a theme that is generated rather than declared, which makes it the one
/// part that can be wrong in ways a palette cannot: a recipe that throws on some arithmetic, or one that
/// quietly draws nothing, or one whose shapes wander off the area the window shows.
/// </remarks>
public class BackdropPainterTests
{
    private static readonly BackdropInk Ink = new(
        Color.FromArgb(0xFF, 0x12, 0x13, 0x16),
        Color.FromArgb(0xFF, 0x2A, 0x31, 0x40),
        Color.FromArgb(0xFF, 0x17, 0x18, 0x1C),
        Color.FromArgb(0x4C, 0x4A, 0x7E, 0xD8));

    [Theory]
    [MemberData(nameof(Recipes))]
    public void EveryRecipeDrawsSomething(BackdropRecipe recipe)
    {
        Drawing drawing = BackdropPainter.Paint(recipe, Ink);

        Assert.True(drawing.IsFrozen);

        // Clear is the one that is allowed to be empty: it is how a see-through theme says so.
        if (recipe == BackdropRecipe.Clear)
            return;

        Assert.False(drawing.Bounds.IsEmpty);
    }

    /// <summary>
    /// That a recipe is a function of its inputs.
    /// </summary>
    /// <remarks>
    /// The shapes are placed by a hash rather than by a random number generator, precisely so that two
    /// windows on one theme draw the same field and a screenshot stays comparable. A generator seeded
    /// from the clock would pass every other test here and fail this one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Recipes))]
    public void TheSameRecipeDrawsTheSameThingTwice(BackdropRecipe recipe)
    {
        Drawing first = BackdropPainter.Paint(recipe, Ink);
        Drawing second = BackdropPainter.Paint(recipe, Ink);

        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Equal(Count(first), Count(second));
    }

    /// <summary>
    /// That a see-through theme's backdrop really is nothing.
    /// </summary>
    /// <remarks>
    /// Not a detail: the control collapses itself on this, and collapsing is what saves a blur pass and
    /// a window-sized cached bitmap spent on a rectangle nobody can see.
    /// </remarks>
    [Fact]
    public void ClearOverATransparentGroundDrawsNothingAtAll()
    {
        BackdropInk seeThrough = Ink with { Ground = Color.FromArgb(0, 0, 0, 0) };

        Assert.Equal(0, Count(BackdropPainter.Paint(BackdropRecipe.Clear, seeThrough)));
    }

    /// <summary>
    /// That a recipe under a see-through theme still draws, with its shapes translucent.
    /// </summary>
    /// <remarks>
    /// The ramp's alpha is interpolated along with its colour, which is what lets somebody put Hexfield
    /// under Glass and get a honeycomb floating over their desktop instead of one hiding it.
    /// </remarks>
    [Fact]
    public void AGeometryOverATransparentGroundStillDraws()
    {
        BackdropInk floating = new(
            Color.FromArgb(0x00, 0, 0, 0),
            Color.FromArgb(0x4D, 0x2A, 0x31, 0x40),
            Color.FromArgb(0x33, 0x14, 0x18, 0x20),
            Color.FromArgb(0x4C, 0x6F, 0xA3, 0xFF));

        Assert.True(Count(BackdropPainter.Paint(BackdropRecipe.Hexfield, floating)) > 0);
    }

    /// <summary>
    /// That a recipe actually covers the area the window shows.
    /// </summary>
    /// <remarks>
    /// Overflow is normal and often the point — Orbits is centred off the frame, and a lamp is a soft
    /// ellipse far wider than the area it lights. What is not normal is a recipe whose arithmetic put
    /// its shapes somewhere else entirely: that still draws, still has bounds, and comes out as a
    /// backdrop that is mostly empty once the Viewbox has scaled it to fit.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Recipes))]
    public void EveryRecipeCoversTheFrame(BackdropRecipe recipe)
    {
        Drawing drawing = BackdropPainter.Paint(recipe, Ink);

        if (recipe == BackdropRecipe.Clear)
            return;

        Rect frame = new(0, 0, BackdropPainter.Width, BackdropPainter.Height);
        Rect covered = Rect.Intersect(drawing.Bounds, frame);

        Assert.False(covered.IsEmpty, $"{recipe} draws nothing inside the frame.");

        double share = covered.Width * covered.Height / (frame.Width * frame.Height);

        Assert.True(share > 0.9, $"{recipe} covers only {share:P0} of the frame.");
    }

    /// <summary>That every theme that ships can paint the backdrop it asks for.</summary>
    [Fact]
    public void EveryBuiltInThemePaints()
    {
        foreach (ThemeDefinition theme in BuiltInThemes.All)
        {
            BackdropInk ink = new(
                ThemeApplier.ToColor(theme.Colour(ThemeKeys.BackdropBase)),
                ThemeApplier.ToColor(theme.Colour(ThemeKeys.BackdropNear)),
                ThemeApplier.ToColor(theme.Colour(ThemeKeys.BackdropFar)),
                ThemeApplier.ToColor(theme.Colour(ThemeKeys.BackdropGlow)));

            Drawing drawing = BackdropPainter.Paint(theme.Backdrop.Recipe, ink);

            Assert.True(drawing.IsFrozen, theme.Id);
        }
    }

    public static TheoryData<BackdropRecipe> Recipes()
    {
        TheoryData<BackdropRecipe> data = [];

        foreach (BackdropRecipe recipe in Enum.GetValues<BackdropRecipe>())
            data.Add(recipe);

        return data;
    }

    private static int Count(Drawing drawing)
        => drawing is DrawingGroup group ? group.Children.Count : 1;
}
