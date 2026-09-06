using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TestFramework.DebugUI.Theme;

/// <summary>
/// Colours worked out from the palette rather than taken straight from it, that still follow it.
/// </summary>
/// <remarks>
/// <para>
/// Most of the window points at a theme brush and follows it for nothing, because the brush is the
/// thing the theme moves. A few places do not want the brush itself: they want the accent at a tenth of
/// its opacity behind a card, or the shadow colour of a shadow rather than a fill. Written out as
/// literals — which is what they were — those places keep the dark theme's colours forever.
/// </para>
/// <para>
/// A binding on the source brush's <c>Color</c> is what makes them keep up. It costs one listener per
/// derived colour rather than one per themed property, which is why this is affordable here and
/// <c>DynamicResource</c> across the whole window is not.
/// </para>
/// </remarks>
internal static class ThemeFollow
{
    private static readonly Dictionary<(string Key, byte Alpha), SolidColorBrush> Washes = [];

    /// <summary>
    /// A theme colour at a different opacity, as a brush that keeps up with the theme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared per colour and opacity, so the fifty rows of a step's log hold one brush between them
    /// rather than fifty brushes and fifty bindings.
    /// </para>
    /// <para>
    /// Deriving beats naming for these. A wash behind a verdict is <em>the verdict's own colour, quieter</em>;
    /// giving it a palette entry of its own would let the two drift apart, and every theme would have to
    /// remember to keep them together.
    /// </para>
    /// </remarks>
    /// <param name="key">The palette colour to follow.</param>
    /// <param name="alpha">The opacity to wear instead of that colour's own.</param>
    public static SolidColorBrush Wash(string key, byte alpha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (Washes.TryGetValue((key, alpha), out SolidColorBrush? cached))
            return cached;

        SolidColorBrush source = Source(key);
        SolidColorBrush wash = new(At(source.Color, alpha));

        BindingOperations.SetBinding(wash, SolidColorBrush.ColorProperty, new Binding(nameof(SolidColorBrush.Color))
        {
            Source = source,
            Mode = BindingMode.OneWay,
            Converter = AlphaOverride.Instance,
            ConverterParameter = alpha
        });

        Washes[(key, alpha)] = wash;

        return wash;
    }

    /// <summary>
    /// Points a shadow at a theme colour, and keeps it there.
    /// </summary>
    /// <remarks>
    /// A shadow is not always black. Under a light theme the card shadow is a low, cool grey and the
    /// halo behind an annotation is white — a black halo under dark ink on a bright board hides the
    /// very mark it is there to lift. The effect's own <c>Opacity</c> is left alone: the palette entry
    /// says what colour, the call site says how much.
    /// </remarks>
    public static void Shadow(DropShadowEffect effect, string key)
    {
        ArgumentNullException.ThrowIfNull(effect);

        BindingOperations.SetBinding(effect, DropShadowEffect.ColorProperty, new Binding(nameof(SolidColorBrush.Color))
        {
            Source = Source(key),
            Mode = BindingMode.OneWay
        });
    }

    private static SolidColorBrush Source(string key)
        => Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? throw new InvalidOperationException($"'{key}' is not a brush in the current theme.");

    private static Color At(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);

    /// <summary>The same colour at the opacity the binding was given.</summary>
    private sealed class AlphaOverride : IValueConverter
    {
        public static AlphaOverride Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Color colour && parameter is byte alpha ? At(colour, alpha) : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("A wash is read from the palette, never written back to it.");
    }
}
