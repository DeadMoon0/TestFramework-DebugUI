using System;
using System.Globalization;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>
/// One colour of a theme, spelled the way both the palettes and the theme files spell it: eight hex
/// digits, alpha first.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than the framework's <c>Color</c> because a palette is data, and this assembly
/// does not reference WPF. Converting to a brush colour is one line at the single place that paints.
/// </para>
/// <para>
/// Two ways in, deliberately. The built-in palettes use the <see cref="ThemeColour(uint)"/> constructor,
/// so a mistyped literal is a compiler error and there is no parse to fail at start-up. A theme file
/// coming off disk is text, so it goes through <see cref="TryParse"/> and a bad value is reported
/// rather than thrown.
/// </para>
/// </remarks>
public readonly record struct ThemeColour
{
    /// <summary>The colour as one packed value, alpha in the high byte.</summary>
    /// <param name="argb">For example <c>0xFF171717</c>.</param>
    public ThemeColour(uint argb)
    {
        Alpha = (byte)(argb >> 24);
        Red = (byte)(argb >> 16);
        Green = (byte)(argb >> 8);
        Blue = (byte)argb;
    }

    /// <summary>The colour from its four channels.</summary>
    public ThemeColour(byte alpha, byte red, byte green, byte blue)
    {
        Alpha = alpha;
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>How much of what is behind shows through. 0 is invisible, 255 is opaque.</summary>
    public byte Alpha { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    /// <summary>The colour as one packed value, alpha in the high byte.</summary>
    public uint Argb => ((uint)Alpha << 24) | ((uint)Red << 16) | ((uint)Green << 8) | Blue;

    /// <summary>
    /// Reads a colour written as <c>#AARRGGBB</c> or <c>#RRGGBB</c>.
    /// </summary>
    /// <remarks>
    /// Six digits mean opaque, which is what every colour picker and every stylesheet in the world
    /// means by six digits. Anything else — a missing hash, a wrong length, a non-hex digit — is a
    /// false rather than an exception, because the only caller is a file somebody typed by hand.
    /// </remarks>
    public static bool TryParse(string? text, out ThemeColour colour)
    {
        colour = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string body = text.Trim();

        if (body.Length is not (7 or 9) || body[0] != '#')
            return false;

        if (!uint.TryParse(body.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            return false;

        colour = body.Length == 7 ? new ThemeColour(value | 0xFF000000u) : new ThemeColour(value);
        return true;
    }

    /// <summary>The colour as <c>#AARRGGBB</c>, which is what <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}");
}
