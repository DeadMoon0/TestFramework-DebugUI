using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;
using Xunit;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// That the palettes and the markup describe the same set of colours, and that applying one moves it.
/// </summary>
/// <remarks>
/// <para>
/// The one gap a type cannot close. <see cref="ThemePalette"/> makes the compiler insist that every
/// theme names every colour, but nothing compiles <c>Theme.xaml</c> against it — a brush renamed in the
/// markup, or a palette entry added without one, produces a window that starts, runs, and quietly keeps
/// the previous theme's colour on one surface.
/// </para>
/// <para>
/// Both directions are checked, because the two failures look nothing alike. A palette entry with no
/// brush is a colour nobody can see; a brush with no palette entry is a surface no theme can reach,
/// which is exactly the bug this whole feature was fixing.
/// </para>
/// </remarks>
public class ThemeApplierTests
{
    [Fact]
    public void EveryBuiltInThemeAppliesCompletely()
    {
        Wpf.Run(() =>
        {
            List<string> unapplied = BuiltInThemes.All
                .SelectMany(theme => ThemeApplier.Apply(Application.Current.Resources, theme, animate: false)
                    .Select(problem => $"{theme.Id}: {problem}"))
                .ToList();

            Assert.True(unapplied.Count == 0, string.Join("; ", unapplied.Distinct()));
        });
    }

    /// <summary>
    /// That the markup holds no colour a theme cannot reach.
    /// </summary>
    /// <remarks>
    /// The direction that matters most. Adding a brush to the theme dictionary is how somebody
    /// reasonably introduces a new colour, and nothing about doing so hints that a palette entry is
    /// needed too — the surface simply stays dark for ever, under every theme, and it takes a light
    /// theme to notice.
    /// </remarks>
    [Fact]
    public void EveryBrushInTheThemeIsAPaletteColour()
    {
        Wpf.Run(() =>
        {
            HashSet<string> palette = [.. ThemePalette.Keys];

            List<string> unreachable = Application.Current.Resources.MergedDictionaries
                .SelectMany(dictionary => dictionary.Keys.Cast<object>()
                    .Where(key => dictionary[key] is SolidColorBrush)
                    .Select(key => key.ToString() ?? string.Empty))
                .Where(key => !palette.Contains(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.Empty(unreachable);
        });
    }

    /// <summary>
    /// That applying a theme actually moves the brush every control is already holding.
    /// </summary>
    /// <remarks>
    /// The claim the whole design rests on: the brushes in the dictionary are unfrozen, so a control
    /// that resolved one at construction follows it afterwards. If any of them were ever frozen this
    /// would be the test that said so, rather than a window that changes theme everywhere except in
    /// one panel.
    /// </remarks>
    [Fact]
    public void ApplyingAThemeMovesTheBrushesInPlace()
    {
        Wpf.Run(() =>
        {
            SolidColorBrush accent = (SolidColorBrush)Application.Current.Resources[ThemeKeys.Accent];

            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.EmberDark, animate: false);
            Color warm = accent.Color;

            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.TideDark, animate: false);
            Color cool = accent.Color;

            // Same instance throughout: nothing was re-resolved and nothing was replaced, which is the
            // only reason a control built before the change shows the colours after it.
            Assert.Same(accent, Application.Current.Resources[ThemeKeys.Accent]);
            Assert.NotEqual(warm, cool);
            Assert.Equal(ThemeApplier.ToColor(BuiltInThemes.TideDark.Colour(ThemeKeys.Accent)), cool);
        });
    }

    /// <summary>
    /// That applying a theme never swaps the brush out from under what is holding it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bug this exists for shipped, passed every other test here, and was only visible in a running
    /// window: <c>Prepare</c> could not tell that it had already prepared a colour, so every theme change
    /// put a <em>new</em> brush in the dictionary. Anything built earlier kept the previous one and froze
    /// at whatever it was showing — the board followed, because it is redrawn, and four dock panels did
    /// not.
    /// </para>
    /// <para>
    /// Identity is the whole assertion. A test that only checked colours passed throughout, because the
    /// dictionary's newest brush always had the right one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ApplyingAThemeNeverReplacesTheBrush()
    {
        Wpf.Run(() =>
        {
            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.SlateDark, animate: false);

            object first = Application.Current.Resources[ThemeKeys.SurfaceOverlay];

            // A control that took the brush before any of the switches below, the way a panel does.
            Border early = new() { Background = (Brush)first };

            foreach (ThemeDefinition theme in BuiltInThemes.All)
            {
                ThemeApplier.Apply(Application.Current.Resources, theme, animate: false);

                Assert.Same(first, Application.Current.Resources[ThemeKeys.SurfaceOverlay]);
                Assert.Equal(
                    ThemeApplier.ToColor(theme.Colour(ThemeKeys.SurfaceOverlay)),
                    ((SolidColorBrush)early.Background).Color);
            }

            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.SlateDark, animate: false);
        });
    }

    /// <summary>
    /// That a colour taken through a <see cref="Style"/> follows the theme too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A style setter is the one path where <c>StaticResource</c> is not enough. Sealing a style — which
    /// WPF does the first time it is applied — <b>freezes the setter's value</b>, so every control wearing
    /// that style gets a frozen copy of whatever the brush happened to be at that moment and never moves
    /// again. Measured: the direct assignment beside it stayed shared and followed perfectly.
    /// </para>
    /// <para>
    /// So the setters in the theme use <c>DynamicResource</c>, and only they. There are seventeen of them
    /// against several hundred direct uses, which is why this costs nothing worth counting.
    /// </para>
    /// </remarks>
    [Fact]
    public void AColourTakenThroughAStyleFollowsTheTheme()
    {
        Wpf.Run(() =>
        {
            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.SlateDark, animate: false);

            Border styled = new() { Style = (Style)Application.Current.Resources["PanelOverlay"] };
            Border direct = new() { Background = (Brush)Application.Current.Resources[ThemeKeys.SurfaceOverlay] };

            Wpf.Layout(new StackPanel { Children = { styled, direct } }, 200, 200);

            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.ContrastLight, animate: false);

            Color expected = ThemeApplier.ToColor(BuiltInThemes.ContrastLight.Colour(ThemeKeys.SurfaceOverlay));

            Assert.Equal(expected, ((SolidColorBrush)direct.Background).Color);
            Assert.Equal(expected, ((SolidColorBrush)styled.Background).Color);

            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.SlateDark, animate: false);
        });
    }

    /// <summary>
    /// That the tool ends up back in the theme it ships in, whatever order these tests ran in.
    /// </summary>
    /// <remarks>
    /// The dictionary is process-wide and these tests move it, so the last one to run would otherwise
    /// decide what every later rendering test sees.
    /// </remarks>
    [Fact]
    public void TheDefaultThemeCanAlwaysBeRestored()
    {
        Wpf.Run(() =>
        {
            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.ContrastLight, animate: false);
            ThemeApplier.Apply(Application.Current.Resources, BuiltInThemes.Get(BuiltInThemes.DefaultId), animate: false);

            SolidColorBrush sunken = (SolidColorBrush)Application.Current.Resources[ThemeKeys.SurfaceSunken];

            Assert.Equal(
                ThemeApplier.ToColor(BuiltInThemes.SlateDark.Colour(ThemeKeys.SurfaceSunken)),
                sunken.Color);
        });
    }
}
