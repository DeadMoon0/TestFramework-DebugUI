using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using TestFramework.DebugUI;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the shortcut table.
/// </summary>
/// <remarks>
/// A lookup table is not usually worth testing. This one is: three commands share F5, told apart only
/// by their modifiers, and one of them discards the run you are looking at. It also cannot be checked
/// by driving the window — WPF reads modifier state from the real keyboard, which a test harness cannot
/// set — so the matching is asserted here instead.
/// </remarks>
public class ShortcutsTests
{
    [Fact]
    public void NoTwoCommandsClaimTheSameGesture()
    {
        // The failure this whole file exists for. Two commands on one gesture means one of them never
        // runs, and which one is decided by declaration order.
        List<string> gestures = [.. Shortcuts.All.Select(Shortcuts.GestureOf)];

        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }

    [Fact]
    public void EveryCommandHasAGestureAndAName()
    {
        Assert.All(Shortcuts.All, command =>
        {
            Assert.NotEmpty(Shortcuts.GestureOf(command));
            Assert.False(string.IsNullOrWhiteSpace(command.Text), $"{command.Name} has no label to show.");
        });
    }

    [Theory]
    [InlineData(Key.F5, ModifierKeys.None, nameof(Shortcuts.Rerun))]
    [InlineData(Key.F5, ModifierKeys.Shift, nameof(Shortcuts.Stop))]
    [InlineData(Key.F5, ModifierKeys.Control, nameof(Shortcuts.Refresh))]
    [InlineData(Key.F, ModifierKeys.Control, nameof(Shortcuts.Fit))]
    [InlineData(Key.F, ModifierKeys.Control | ModifierKeys.Shift, nameof(Shortcuts.FirstFailure))]
    [InlineData(Key.W, ModifierKeys.Control | ModifierKeys.Shift, nameof(Shortcuts.ToggleWatch))]
    [InlineData(Key.OemComma, ModifierKeys.Control, nameof(Shortcuts.Settings))]
    [InlineData(Key.R, ModifierKeys.Control, nameof(Shortcuts.Runs))]
    [InlineData(Key.I, ModifierKeys.Control, nameof(Shortcuts.Summary))]
    [InlineData(Key.F8, ModifierKeys.None, nameof(Shortcuts.Continue))]
    [InlineData(Key.Escape, ModifierKeys.None, nameof(Shortcuts.CloseTopmost))]
    public void AKeyPressResolvesToItsCommand(Key key, ModifierKeys modifiers, string expected)
    {
        RoutedUICommand? matched = Shortcuts.Match(key, modifiers);

        Assert.NotNull(matched);
        Assert.Equal(expected, matched.Name);
    }

    [Theory]
    [InlineData(Key.F5, ModifierKeys.Alt)]
    [InlineData(Key.F5, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.F, ModifierKeys.None)]
    [InlineData(Key.W, ModifierKeys.Control)]
    [InlineData(Key.Escape, ModifierKeys.Control)]
    public void AnUnclaimedCombinationMatchesNothing(Key key, ModifierKeys modifiers)
    {
        // Modifiers must match exactly. A looser comparison would make Ctrl+Shift+F5 stop the run, or
        // plain F to re-run it, both of which lose work.
        Assert.Null(Shortcuts.Match(key, modifiers));
    }

    [Fact]
    public void AGestureReadsTheWayAReaderWouldWriteIt()
    {
        Assert.Equal("Ctrl+Shift+W", Shortcuts.GestureOf(Shortcuts.ToggleWatch));
        Assert.Equal("Ctrl+,", Shortcuts.GestureOf(Shortcuts.Settings));
        Assert.Equal("Esc", Shortcuts.GestureOf(Shortcuts.CloseTopmost));
        Assert.Equal("F5", Shortcuts.GestureOf(Shortcuts.Rerun));
        Assert.Equal("Shift+F5", Shortcuts.GestureOf(Shortcuts.Stop));
    }

    [Fact]
    public void ATooltipCarriesItsShortcut()
    {
        Assert.Equal("Fit the board  (Ctrl+F)", Shortcuts.Describe("Fit the board", Shortcuts.Fit));
    }
}
