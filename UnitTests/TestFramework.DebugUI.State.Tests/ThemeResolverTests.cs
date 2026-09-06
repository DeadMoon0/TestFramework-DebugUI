using System.Collections.Generic;
using System.Linq;
using TestFramework.DebugUI.State.Theming;
using Xunit;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// What a theme file somebody wrote turns into.
/// </summary>
/// <remarks>
/// Every one of these runs without a window, a disk or an application, because resolution is a function
/// of a file and the themes it may inherit from and nothing else. That is the whole reason it is not
/// part of the thing that applies it.
/// </remarks>
public class ThemeResolverTests
{
    private static IReadOnlyDictionary<string, ThemeDefinition> Bases => BuiltInThemes.ById;

    [Fact]
    public void AFileReplacesOnlyWhatItNames()
    {
        ThemeFile file = new()
        {
            Name = "Mine",
            Inherits = "slate-dark",
            Colours = new Dictionary<string, string> { ["Accent"] = "#FFE07A3F" }
        };

        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(ThemeResolver.Resolve("mine", file, Bases).Theme);

        Assert.Equal("Mine", theme.Name);
        Assert.True(theme.IsCustom);
        Assert.Equal(new ThemeColour(0xFFE07A3Fu), theme.Colour(nameof(ThemePalette.Accent)));

        // Everything else is still the base theme's, including its backdrop.
        Assert.Equal(
            BuiltInThemes.SlateDark.Colour(nameof(ThemePalette.SurfaceCard)),
            theme.Colour(nameof(ThemePalette.SurfaceCard)));

        Assert.Equal(BuiltInThemes.SlateDark.Backdrop.Recipe, theme.Backdrop.Recipe);
    }

    /// <summary>
    /// That a sparse file is complete once resolved.
    /// </summary>
    /// <remarks>
    /// The property everything downstream depends on. The applier indexes the palette by name without
    /// checking, and it is allowed to because a file can only ever replace an entry, never remove one.
    /// </remarks>
    [Fact]
    public void AResolvedThemeHasEveryColour()
    {
        ThemeFile file = new() { Inherits = "tide-light" };

        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(ThemeResolver.Resolve("sparse", file, Bases).Theme);

        Assert.Empty(ThemePalette.Keys.Where(key => !theme.Colours.ContainsKey(key)));
    }

    [Fact]
    public void AFileWithNoBaseIsRefusedAndSaysSo()
    {
        ThemeResolution resolved = ThemeResolver.Resolve("orphan", new ThemeFile(), Bases);

        Assert.Null(resolved.Theme);
        Assert.Contains(resolved.Problems, problem => problem.Contains("does not say which theme"));
    }

    [Fact]
    public void AFileInheritingSomethingUnknownIsRefusedAndNamesIt()
    {
        ThemeResolution resolved = ThemeResolver.Resolve(
            "lost",
            new ThemeFile { Inherits = "slate-mauve" },
            Bases);

        Assert.Null(resolved.Theme);
        Assert.Contains(resolved.Problems, problem => problem.Contains("slate-mauve"));
    }

    /// <summary>
    /// That one bad line does not cost the whole theme.
    /// </summary>
    /// <remarks>
    /// A theme is a preference somebody is editing by hand, so the useful behaviour is to take what
    /// parses, say what did not, and carry on — not to hand back nothing over one typo.
    /// </remarks>
    [Fact]
    public void AMistypedColourIsReportedAndTheRestSurvives()
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Colours = new Dictionary<string, string>
            {
                ["Accent"] = "not a colour",
                ["StateTimeout"] = "#FFE8C547"
            }
        };

        ThemeResolution resolved = ThemeResolver.Resolve("half", file, Bases);
        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(resolved.Theme);

        Assert.Contains(resolved.Problems, problem => problem.Contains("not a colour"));
        Assert.Equal(new ThemeColour(0xFFE8C547u), theme.Colour(nameof(ThemePalette.StateTimeout)));
        Assert.Equal(BuiltInThemes.SlateDark.Colour(nameof(ThemePalette.Accent)), theme.Colour(nameof(ThemePalette.Accent)));
    }

    [Fact]
    public void AColourThatIsNotAPaletteKeyIsReported()
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Colours = new Dictionary<string, string> { ["Accnet"] = "#FF112233" }
        };

        ThemeResolution resolved = ThemeResolver.Resolve("typo", file, Bases);

        Assert.NotNull(resolved.Theme);
        Assert.Contains(resolved.Problems, problem => problem.Contains("Accnet"));
    }

    /// <summary>
    /// That the American spelling works too.
    /// </summary>
    /// <remarks>
    /// The alternative is a file that looks right, parses, loads and changes nothing at all, with no way
    /// for the person who wrote it to find out which of the two spellings was wanted.
    /// </remarks>
    [Fact]
    public void EitherSpellingOfTheColourBlockIsRead()
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Colors = new Dictionary<string, string> { ["Accent"] = "#FF00FF00" }
        };

        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(ThemeResolver.Resolve("american", file, Bases).Theme);

        Assert.Equal(new ThemeColour(0xFF00FF00u), theme.Colour(nameof(ThemePalette.Accent)));
    }

    [Fact]
    public void ABackdropCanBeChosenByNameAndKeepsTheInheritedBlur()
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Backdrop = new ThemeFileBackdrop { Recipe = "orbits" }
        };

        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(ThemeResolver.Resolve("rings", file, Bases).Theme);

        Assert.Equal(BackdropRecipe.Orbits, theme.Backdrop.Recipe);
        Assert.Equal(BuiltInThemes.SlateDark.Backdrop.Blur, theme.Backdrop.Blur);
    }

    [Fact]
    public void AnUnknownBackdropIsReportedAndTheInheritedOneIsKept()
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Backdrop = new ThemeFileBackdrop { Recipe = "spirals" }
        };

        ThemeResolution resolved = ThemeResolver.Resolve("spiral", file, Bases);
        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(resolved.Theme);

        Assert.Contains(resolved.Problems, problem => problem.Contains("spirals") && problem.Contains("Hexfield"));
        Assert.Equal(BuiltInThemes.SlateDark.Backdrop.Recipe, theme.Backdrop.Recipe);
    }

    /// <summary>
    /// That an absurd blur cannot lock the window up.
    /// </summary>
    /// <remarks>
    /// The value arrives from a file, so it is the one number in a theme a person can make arbitrarily
    /// large by accident. Clamped rather than refused: the theme is otherwise fine.
    /// </remarks>
    [Theory]
    [InlineData(-4, 0)]
    [InlineData(100000, ThemeBackdrop.MaximumBlur)]
    [InlineData(double.NaN, 0)]
    public void AnImpossibleBlurIsClamped(double written, double expected)
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Backdrop = new ThemeFileBackdrop { Blur = written }
        };

        ThemeDefinition theme = Assert.IsType<ThemeDefinition>(ThemeResolver.Resolve("blurry", file, Bases).Theme);

        Assert.Equal(expected, theme.Backdrop.SafeBlur);
    }

    /// <summary>
    /// That a theme nobody could read says so, and loads anyway.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Refusing it would be the tool overruling somebody about their own window;
    /// saying nothing would leave them with a tool that looks broken and no clue that the theme did it.
    /// </remarks>
    [Fact]
    public void TextThatCannotBeReadOnItsOwnCardIsReported()
    {
        ThemeFile file = new()
        {
            Inherits = "slate-dark",
            Colours = new Dictionary<string, string> { ["TextPrimary"] = "#FF303030" }
        };

        ThemeResolution resolved = ThemeResolver.Resolve("invisible", file, Bases);

        Assert.NotNull(resolved.Theme);
        Assert.Contains(resolved.Problems, problem => problem.Contains("TextPrimary") && problem.Contains("hard to read"));
    }

    [Fact]
    public void AReadableThemeIsNotComplainedAbout()
    {
        foreach (ThemeDefinition built in BuiltInThemes.All)
        {
            ThemeResolution resolved = ThemeResolver.Resolve(
                "copy-of-" + built.Id,
                new ThemeFile { Inherits = built.Id },
                Bases);

            Assert.Empty(resolved.Problems);
        }
    }
}
