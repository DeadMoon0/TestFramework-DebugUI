using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TestFramework.DebugUI.State.Theming;

namespace TestFramework.DebugUI.Theme;

/// <summary>
/// Puts a theme's colours into the theme's brushes, and keeps the brushes the same objects while it
/// does.
/// </summary>
/// <remarks>
/// <para>
/// Every control in the window resolved its brushes once, while it was being built, and is holding
/// those objects. So a theme change must not replace them — it must move the colour inside the ones
/// already held. Then the three hundred-odd <c>StaticResource</c> sites and the two hundred-odd lookups
/// by key never re-resolve, and a theme change is a few dozen colour assignments and one frame.
/// </para>
/// <para>
/// <b>The obvious way to do that does not work, and it is worth knowing why.</b> A brush in a compiled
/// resource dictionary is frozen: the XAML compiler freezes what it can, and — worse — a
/// <see cref="ResourceDictionary"/> that belongs to an application seals every freezable put into it, so
/// swapping in an unfrozen copy just produces a second frozen brush. Both were measured rather than
/// assumed.
/// </para>
/// <para>
/// What a dictionary cannot seal is a freezable it is not allowed to freeze, and a freezable with a
/// binding on it cannot be frozen. So each colour becomes two brushes: a <em>source</em> that lives
/// nowhere but in its own binding, and the brush in the dictionary whose <c>Color</c> follows it. The
/// dictionary is welcome to try to seal that one; it cannot, and everything holding it follows the
/// source for ever.
/// </para>
/// <para>
/// The prepared brush goes into the application's own dictionary rather than into the merged one the
/// markup loaded. That matters: a dictionary loaded from a <c>Source</c> URI belongs to WPF, and a brush
/// written into it did not stay written — the next call found the markup's original again, replaced it a
/// second time, and every control still holding the first one was left behind. The top-level entry
/// shadows the merged original and stays put.
/// </para>
/// <para>
/// The cost is one binding per colour for the whole application — not one per themed property, which is
/// what <c>DynamicResource</c> would have cost across a board with thousands of elements. The exception
/// is a <c>Style</c> setter, which freezes its value when the style is sealed; the seventeen of those in
/// the theme do use <c>DynamicResource</c>, and only they.
/// </para>
/// </remarks>
internal static class ThemeApplier
{
    /// <summary>
    /// How long a theme change takes to arrive.
    /// </summary>
    /// <remarks>
    /// Short enough not to be a transition anybody waits through, long enough that the window is
    /// visibly the same window afterwards rather than a different one. A hard cut across forty-eight
    /// colours reads as a redraw, which is the one thing this is not.
    /// </remarks>
    public static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(160);

    /// <summary>
    /// The colour behind each palette brush, which is the thing a theme change actually moves.
    /// </summary>
    /// <remarks>
    /// Static because the palette is: these brushes are shared by every window in the process, so
    /// "which source drives <c>Accent</c>" has exactly one answer. It was first written to be read back
    /// out of the brush's own binding instead, to avoid state — and that quietly did not work, because
    /// <c>BindingOperations.GetBinding</c> does not return the binding once WPF has taken the brush into
    /// its resource machinery. Prepare then thought nothing had been prepared, replaced the brush on
    /// every theme change, and orphaned every control already holding the previous one.
    /// </remarks>
    private static readonly Dictionary<string, SolidColorBrush> Sources = new(StringComparer.Ordinal);

    /// <summary>What this class put in the dictionary, so it can tell its own work from the markup's.</summary>
    private static readonly Dictionary<string, SolidColorBrush> Prepared = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes the theme's brushes movable, before anything has a chance to hold one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Timing is the whole of it.</b> A control resolves its brushes while it is being built, so this
    /// has to have happened before the first control exists — otherwise that control keeps the frozen
    /// original and stops following the theme, and the result is a window that changes everywhere
    /// except in one panel. That is why <c>App</c> calls it before it constructs a window, rather than
    /// the window doing it for itself.
    /// </para>
    /// <para>
    /// Idempotent: a colour this class already put in the dictionary is recognised by identity and
    /// left alone, so calling it again costs one lookup per colour.
    /// </para>
    /// </remarks>
    /// <returns>The colours the markup has no brush for, which is a defect rather than a bad theme.</returns>
    public static IReadOnlyList<string> Prepare(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        List<string> missing = [];

        foreach (string key in ThemePalette.Keys)
        {
            // The indexer searches the merged dictionaries; the assignment below does not go into them.
            if (resources[key] is not SolidColorBrush brush)
            {
                missing.Add($"{key} is not a brush in the theme.");
                continue;
            }

            if (Prepared.TryGetValue(key, out SolidColorBrush? already) && ReferenceEquals(already, brush))
                continue;

            // Seeded from what the markup says, so a window is in the right colours from its first frame
            // even if nothing ever applies a theme.
            SolidColorBrush source = new(brush.Color);
            SolidColorBrush following = new();

            BindingOperations.SetBinding(
                following,
                SolidColorBrush.ColorProperty,
                new Binding(nameof(SolidColorBrush.Color)) { Source = source, Mode = BindingMode.OneWay });

            // Into the dictionary that was handed in - the application's own - rather than into the merged
            // one the markup loaded. A dictionary loaded from a Source URI is WPF's to manage, and writing
            // into it did not survive; the top-level entry shadows the merged one and stays put.
            resources[key] = following;

            Sources[key] = source;
            Prepared[key] = following;
        }

        return missing;
    }

    /// <summary>
    /// Applies a theme, and says what it could not apply.
    /// </summary>
    /// <remarks>
    /// An empty result is the good one. A key in the palette with no brush of that name in the markup is
    /// a programming mistake rather than a bad theme, and it is a mistake nothing else would report: the
    /// surface would simply keep the previous theme's colour and look almost right.
    /// </remarks>
    /// <param name="resources">The dictionary holding the theme's brushes.</param>
    /// <param name="theme">The theme to apply.</param>
    /// <param name="animate">
    /// Whether to cross-fade. False at start-up, where there is no previous colour to fade from and the
    /// window has not been shown yet.
    /// </param>
    public static IReadOnlyList<string> Apply(ResourceDictionary resources, ThemeDefinition theme, bool animate)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(theme);

        // Cheap and idempotent, so there is no order these two have to be called in for the colours to
        // come out right - only for the brushes to be the ones the controls are already holding.
        IReadOnlyList<string> missing = Prepare(resources);

        foreach ((string key, ThemeColour colour) in theme.Colours)
        {
            if (Sources.TryGetValue(key, out SolidColorBrush? source))
                Set(source, ToColor(colour), animate);
        }

        return missing;
    }

    /// <summary>A theme colour as the framework's.</summary>
    public static Color ToColor(ThemeColour colour)
        => Color.FromArgb(colour.Alpha, colour.Red, colour.Green, colour.Blue);

    /// <summary>
    /// Moves one colour to its new value.
    /// </summary>
    /// <remarks>
    /// The base value is set first and the animation only covers the journey, with
    /// <see cref="FillBehavior.Stop"/> so it lets go at the end. An animation left holding its final
    /// value would own the property from then on, and the next plain assignment — by a later theme
    /// change, or by anything else — would appear to do nothing at all.
    /// </remarks>
    private static void Set(SolidColorBrush source, Color colour, bool animate)
    {
        Color from = source.Color;

        source.BeginAnimation(SolidColorBrush.ColorProperty, null);
        source.Color = colour;

        if (!animate || from == colour)
            return;

        source.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(from, colour, new Duration(Fade)) { FillBehavior = FillBehavior.Stop });
    }
}
