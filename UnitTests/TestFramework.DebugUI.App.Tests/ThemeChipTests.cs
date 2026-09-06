using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TestFramework.DebugUI.Controls.Settings;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;
using Xunit;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// That a theme the machine cannot deliver is offered as unavailable rather than offered as working.
/// </summary>
/// <remarks>
/// <para>
/// A see-through theme without the compositor's blur is not a slightly duller version of itself. It is
/// a window with the desktop showing through it sharply, which reads as the tool being broken rather
/// than as a setting being off — and nothing reports the failure, because there is not one: Windows
/// accepts the request and quietly draws a flat fill.
/// </para>
/// <para>
/// Testable without touching the machine's settings because the answer is a parameter rather than
/// something the chip asks for. Whether energy saver is on is Windows' business and is read in one
/// place; what a chip does about it is decided here, and this is that decision.
/// </para>
/// </remarks>
public class ThemeChipTests
{
    private static ThemeDefinition SeeThrough
        => BuiltInThemes.All.First(theme => theme.ShowsWhatIsBehind);

    private static ThemeDefinition Painted
        => BuiltInThemes.All.First(theme => !theme.ShowsWhatIsBehind);

    /// <summary>
    /// That a theme needing the blur cannot be chosen while the blur is off.
    /// </summary>
    [Theory]
    [InlineData(BlurBlock.EnergySaver)]
    [InlineData(BlurBlock.TransparencyOff)]
    [InlineData(BlurBlock.Unknown)]
    public void AThemeThatNeedsTheBlurIsNotOfferedWithoutIt(BlurBlock block)
        => Wpf.Run(() =>
        {
            Button chip = ThemeChip.Build(SeeThrough, chosen: false, Owner(), block);

            Assert.False(chip.IsEnabled);
        });

    /// <summary>
    /// That the reason is on the chip, and reachable.
    /// </summary>
    /// <remarks>
    /// Both halves matter and only one of them is obvious. A disabled control in WPF does not show its
    /// tooltip at all unless it is told to, so a chip carrying a perfectly good explanation that nobody
    /// can ever hover is the likely way for this to be wrong.
    /// </remarks>
    [Fact]
    public void AnUnavailableThemeSaysWhyOnHover()
        => Wpf.Run(() =>
        {
            Button chip = ThemeChip.Build(SeeThrough, chosen: false, Owner(), BlurBlock.EnergySaver);

            Assert.True(ToolTipService.GetShowOnDisabled(chip), "The explanation cannot be hovered.");
            Assert.Contains("energy saver", Assert.IsType<string>(chip.ToolTip), StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>
    /// That each reason names the setting to change rather than only reporting the state.
    /// </summary>
    /// <remarks>
    /// The tooltip is the only place the reader is told, and "unavailable" on its own sends somebody
    /// hunting through Windows' settings for a cause the tool already knows.
    /// </remarks>
    [Theory]
    [InlineData(BlurBlock.EnergySaver, "energy saver")]
    [InlineData(BlurBlock.TransparencyOff, "transparency")]
    public void TheReasonNamesTheSetting(BlurBlock block, string expected)
        => Assert.Contains(expected, BlurSupport.Explain("Glass Dark", block), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// That a theme painting its own ground is unaffected, blur or no blur.
    /// </summary>
    /// <remarks>
    /// The half that would be missed by disabling on the block alone. Eight of the ten themes never ask
    /// the compositor for anything, and taking them away because a laptop is on battery would leave a
    /// reader with no theme at all.
    /// </remarks>
    [Fact]
    public void AThemeThatPaintsItsOwnGroundIsAlwaysOffered()
        => Wpf.Run(() =>
        {
            Button chip = ThemeChip.Build(Painted, chosen: false, Owner(), BlurBlock.EnergySaver);

            Assert.True(chip.IsEnabled);
        });

    /// <summary>
    /// That nothing is taken away when Windows is drawing effects.
    /// </summary>
    [Fact]
    public void EveryThemeIsOfferedWhenTheBlurWorks()
        => Wpf.Run(() =>
        {
            FrameworkElement owner = Owner();

            foreach (ThemeDefinition theme in BuiltInThemes.All)
                Assert.True(ThemeChip.Build(theme, chosen: false, owner, BlurBlock.None).IsEnabled, theme.Id);
        });

    /// <summary>Something whose resources reach the application's, which is where the palette is.</summary>
    private static FrameworkElement Owner() => new Grid();
}
