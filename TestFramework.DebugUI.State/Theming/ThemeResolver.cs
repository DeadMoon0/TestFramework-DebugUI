using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>What a theme file turned into, and everything that was wrong with it.</summary>
/// <remarks>
/// Both, always. A file with one mistyped colour still produces a theme — the rest of it is perfectly
/// good — and a file that produces nothing still has to be able to say why, or the user is left with a
/// theme that silently did not appear in the picker.
/// </remarks>
public sealed record ThemeResolution
{
    /// <summary>The theme, or null when the file could not name a base to build on.</summary>
    public ThemeDefinition? Theme { get; init; }

    /// <summary>What could not be used, in the words the user needs to fix the file.</summary>
    public required ImmutableArray<string> Problems { get; init; }
}

/// <summary>
/// Turns a theme file into a theme.
/// </summary>
/// <remarks>
/// <para>
/// Honest: the same file and the same set of built-ins always give the same result. It reads nothing,
/// writes nothing and knows no paths — <see cref="ThemeStore"/> does all of that and hands the outcome
/// here, which is what makes every rule below assertable without a disk or a window.
/// </para>
/// <para>
/// Nothing here throws at the caller. A theme is a preference; a broken one is worth a line in the
/// message feed and never worth a tool that will not start.
/// </para>
/// </remarks>
public static class ThemeResolver
{
    /// <summary>
    /// Below this, text over its own card is hard enough to read that it is worth saying so.
    /// </summary>
    /// <remarks>
    /// WCAG calls 4.5 the bar for body text and 3 the bar for large text. This sits at the lower one:
    /// the check exists to catch a theme that is unusable, not to referee somebody's taste.
    /// </remarks>
    private const double MinimumTextContrast = 3.0;

    /// <summary>
    /// Resolves one file against the themes it may inherit from.
    /// </summary>
    /// <param name="id">The theme's id, which is its file name without the extension.</param>
    /// <param name="file">The file's contents, or null when it could not be read at all.</param>
    /// <param name="bases">What <c>inherits</c> may name.</param>
    public static ThemeResolution Resolve(
        string id,
        ThemeFile? file,
        IReadOnlyDictionary<string, ThemeDefinition> bases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(bases);

        List<string> problems = [];

        if (file is null)
        {
            problems.Add($"'{id}' could not be read.");
            return new ThemeResolution { Problems = [.. problems] };
        }

        if (!TryFindBase(file.Inherits, bases, out ThemeDefinition? baseTheme))
        {
            problems.Add(file.Inherits is { Length: > 0 } named
                ? $"'{id}' inherits '{named}', which is not a theme this build has."
                : $"'{id}' does not say which theme it inherits from.");

            return new ThemeResolution { Problems = [.. problems] };
        }

        ImmutableDictionary<string, ThemeColour> colours = Merge(id, file, baseTheme, problems);

        ThemeDefinition theme = new()
        {
            Id = id,
            Name = file.Name is { Length: > 0 } name ? name : id,
            Family = file.Family is { Length: > 0 } family ? family : baseTheme.Family,
            Mode = baseTheme.Mode,
            Backdrop = ReadBackdrop(id, file.Backdrop, baseTheme.Backdrop, problems),
            Colours = colours,
            IsCustom = true
        };

        problems.AddRange(Legibility(id, theme));

        return new ThemeResolution { Theme = theme, Problems = [.. problems] };
    }

    private static bool TryFindBase(
        string? inherits,
        IReadOnlyDictionary<string, ThemeDefinition> bases,
        [NotNullWhen(true)] out ThemeDefinition? found)
    {
        found = null;

        return inherits is { Length: > 0 }
            && bases.TryGetValue(inherits, out found);
    }

    /// <summary>
    /// The base theme's colours with the file's replacements applied.
    /// </summary>
    /// <remarks>
    /// Replacements only. A file cannot remove a colour, which is why a theme built this way is as
    /// complete as the palette it started from and nothing downstream has to check.
    /// </remarks>
    private static ImmutableDictionary<string, ThemeColour> Merge(
        string id,
        ThemeFile file,
        ThemeDefinition baseTheme,
        List<string> problems)
    {
        ImmutableDictionary<string, ThemeColour>.Builder colours = baseTheme.Colours.ToBuilder();

        foreach ((string key, string value) in Written(file))
        {
            if (!baseTheme.Colours.ContainsKey(key))
            {
                problems.Add($"'{id}' sets '{key}', which is not a theme colour.");
                continue;
            }

            if (!ThemeColour.TryParse(value, out ThemeColour colour))
            {
                problems.Add($"'{id}' sets {key} to '{value}', which is not a colour like #FF1E1E1E.");
                continue;
            }

            colours[key] = colour;
        }

        return colours.ToImmutable();
    }

    /// <summary>Both spellings of the colour block, with the British one winning a collision.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Written(ThemeFile file)
        => (file.Colors ?? []).Concat(file.Colours ?? [])
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(group => group.Last());

    private static ThemeBackdrop ReadBackdrop(
        string id,
        ThemeFileBackdrop? written,
        ThemeBackdrop inherited,
        List<string> problems)
    {
        if (written is null)
            return inherited;

        BackdropRecipe recipe = inherited.Recipe;

        if (written.Recipe is { Length: > 0 } named)
        {
            if (Enum.TryParse(named, ignoreCase: true, out BackdropRecipe parsed) && Enum.IsDefined(parsed))
                recipe = parsed;
            else
                problems.Add($"'{id}' asks for the '{named}' backdrop, which is not one of: {Recipes()}.");
        }

        return new ThemeBackdrop
        {
            Recipe = recipe,
            Blur = written.Blur ?? inherited.Blur
        };
    }

    private static string Recipes()
        => string.Join(", ", Enum.GetNames<BackdropRecipe>());

    /// <summary>
    /// Whether the theme's own text can be read on the theme's own card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported, never enforced. Somebody's theme is theirs, and a tool that refuses to load a palette
    /// because it disagrees about contrast is a tool that has decided it knows better. But a theme
    /// whose text is invisible looks like the tool is broken rather than like the theme is, so it is
    /// worth one line saying which it was.
    /// </para>
    /// <para>
    /// Only the pairing that would make the tool unusable is checked. Every colour against every
    /// surface would flag half of a deliberately quiet palette, and then nobody would read any of it.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Legibility(string id, ThemeDefinition theme)
    {
        (string ink, string ground)[] pairs =
        [
            (nameof(ThemePalette.TextPrimary), nameof(ThemePalette.SurfaceCard)),
            (nameof(ThemePalette.TextPrimary), nameof(ThemePalette.SurfaceOverlay))
        ];

        foreach ((string ink, string ground) in pairs)
        {
            double ratio = Contrast(theme.Colour(ink), theme.Colour(ground));

            if (ratio < MinimumTextContrast)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{id}' has {ink} at {ratio:0.0}:1 against {ground}, which is below {MinimumTextContrast:0.0}:1 and will be hard to read.");
            }
        }
    }

    /// <summary>The WCAG contrast ratio between two colours, both treated as opaque.</summary>
    /// <remarks>
    /// Alpha is ignored deliberately: what a translucent surface actually composites over is the
    /// backdrop, the acrylic blur and whatever is behind the window, none of which this can know. The
    /// opaque comparison is the optimistic one, so anything it does flag is genuinely bad.
    /// </remarks>
    internal static double Contrast(ThemeColour ink, ThemeColour ground)
    {
        double a = Luminance(ink) + 0.05;
        double b = Luminance(ground) + 0.05;

        return a > b ? a / b : b / a;
    }

    private static double Luminance(ThemeColour colour)
        => (0.2126 * Channel(colour.Red)) + (0.7152 * Channel(colour.Green)) + (0.0722 * Channel(colour.Blue));

    private static double Channel(byte value)
    {
        double v = value / 255.0;

        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
