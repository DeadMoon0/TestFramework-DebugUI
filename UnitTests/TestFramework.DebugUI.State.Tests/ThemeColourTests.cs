using TestFramework.DebugUI.State.Theming;
using Xunit;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Reading a colour out of a file somebody typed.
/// </summary>
/// <remarks>
/// The built-in palettes never come through here — they are packed literals, so a mistyped one is a
/// compiler error. Everything below is about the other way in, where the input is text and being wrong
/// has to be an answer rather than an exception.
/// </remarks>
public class ThemeColourTests
{
    [Fact]
    public void EightDigitsAreReadAlphaFirst()
    {
        Assert.True(ThemeColour.TryParse("#8012AB34", out ThemeColour colour));

        Assert.Equal(0x80, colour.Alpha);
        Assert.Equal(0x12, colour.Red);
        Assert.Equal(0xAB, colour.Green);
        Assert.Equal(0x34, colour.Blue);
    }

    /// <summary>
    /// That six digits mean opaque.
    /// </summary>
    /// <remarks>
    /// What every colour picker and every stylesheet means by six digits, so it is what somebody
    /// pasting one into a theme file will mean too.
    /// </remarks>
    [Fact]
    public void SixDigitsAreOpaque()
    {
        Assert.True(ThemeColour.TryParse("#12AB34", out ThemeColour colour));

        Assert.Equal(new ThemeColour(0xFF12AB34u), colour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12AB34")]
    [InlineData("#12AB3")]
    [InlineData("#12AB34567")]
    [InlineData("#XXAB34")]
    [InlineData("rebeccapurple")]
    public void AnythingElseIsRefusedRatherThanGuessedAt(string? text)
        => Assert.False(ThemeColour.TryParse(text, out _));

    [Fact]
    public void SurroundingSpaceIsForgiven()
    {
        Assert.True(ThemeColour.TryParse("  #FF102030  ", out ThemeColour colour));

        Assert.Equal(new ThemeColour(0xFF102030u), colour);
    }

    /// <summary>That what a colour prints is what it reads back as.</summary>
    [Fact]
    public void ItPrintsWhatItCanRead()
    {
        ThemeColour original = new(0x4C, 0x4A, 0x7E, 0xD8);

        Assert.Equal("#4C4A7ED8", original.ToString());
        Assert.True(ThemeColour.TryParse(original.ToString(), out ThemeColour again));
        Assert.Equal(original, again);
    }

    [Fact]
    public void ThePackedFormAndTheChannelsAgree()
    {
        Assert.Equal(new ThemeColour(0x4C, 0x4A, 0x7E, 0xD8), new ThemeColour(0x4C4A7ED8u));
        Assert.Equal(0x4C4A7ED8u, new ThemeColour(0x4C, 0x4A, 0x7E, 0xD8).Argb);
    }
}
