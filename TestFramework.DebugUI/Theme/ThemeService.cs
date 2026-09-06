using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.State.Theming;

namespace TestFramework.DebugUI.Theme;

/// <summary>
/// Which theme the window is in, and the one place that changes it.
/// </summary>
/// <remarks>
/// <para>
/// Everything dishonest about theming is gathered here: reading the themes folder, holding "the current
/// one", and reaching into the application's resources. Below it, choosing and resolving a theme is
/// plain data and painting a backdrop is a function of its recipe — which is why almost none of this
/// feature needs a window to be tested.
/// </para>
/// <para>
/// One instance, owned by the window. A second one would be a second answer to "which theme is on",
/// and the brushes it fought over are shared by the whole process.
/// </para>
/// </remarks>
internal sealed class ThemeService
{
    private readonly ResourceDictionary resources;
    private readonly Action<string>? report;

    /// <summary>
    /// Reads what is available and settles on a theme, without applying it yet.
    /// </summary>
    /// <param name="resources">The dictionary holding the theme's brushes.</param>
    /// <param name="store">Where custom themes come from.</param>
    /// <param name="report">Told about a theme file that could not be used.</param>
    public ThemeService(ResourceDictionary resources, ThemeStore store, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(store);

        this.resources = resources;
        this.report = report;

        Store = store;
        Available = store.Load();
        Current = BuiltInThemes.Get(BuiltInThemes.DefaultId);
    }

    /// <summary>Raised once the new theme's colours are in the brushes.</summary>
    /// <remarks>
    /// What the backdrop listens to. The brushes look after themselves — every control in the window is
    /// already holding them — but a drawing built from those colours is a copy and has to be rebuilt.
    /// </remarks>
    public event Action<ThemeDefinition>? Changed;

    /// <summary>The themes on offer, built-in first and then whatever the folder had.</summary>
    public ImmutableArray<ThemeDefinition> Available { get; private set; }

    /// <summary>The theme the window is in.</summary>
    public ThemeDefinition Current { get; private set; }

    /// <summary>Where custom themes are read from, so the settings panel can offer to open it.</summary>
    public ThemeStore Store { get; }

    /// <summary>
    /// Puts the window into a theme, by id.
    /// </summary>
    /// <remarks>
    /// An id nothing answers to resolves to the default rather than to an error. That is what a settings
    /// file written by a newer build looks like from an older one, and what a custom theme that has since
    /// been deleted looks like from any of them.
    /// </remarks>
    /// <returns>The theme that is now on, which may not be the one that was asked for.</returns>
    public ThemeDefinition Use(string? id, bool animate)
    {
        ThemeDefinition wanted = Find(id);

        Current = wanted;

        foreach (string problem in ThemeApplier.Apply(resources, wanted, animate))
            report?.Invoke(problem);

        Changed?.Invoke(wanted);

        return wanted;
    }

    /// <summary>
    /// Re-reads the themes folder, keeping the current theme if it is still there.
    /// </summary>
    /// <remarks>
    /// Offered rather than watched. A file watcher on somebody's editor gives you a theme that
    /// re-applies on every keystroke, including the half-typed colours in between; a button means the
    /// tool reloads when the person writing the theme says they are ready.
    /// </remarks>
    public void Reload()
    {
        Available = Store.Load();

        Use(Current.Id, animate: true);
    }

    /// <summary>The tint Windows should draw the blur behind the window in.</summary>
    public Color WindowTint => ThemeApplier.ToColor(Current.Colour(ThemeKeys.WindowTint));

    /// <summary>The four colours the current theme's backdrop is drawn from.</summary>
    public BackdropInk BackdropInk => new(
        ThemeApplier.ToColor(Current.Colour(ThemeKeys.BackdropBase)),
        ThemeApplier.ToColor(Current.Colour(ThemeKeys.BackdropNear)),
        ThemeApplier.ToColor(Current.Colour(ThemeKeys.BackdropFar)),
        ThemeApplier.ToColor(Current.Colour(ThemeKeys.BackdropGlow)));

    private ThemeDefinition Find(string? id)
    {
        if (id is { Length: > 0 })
        {
            foreach (ThemeDefinition theme in Available)
            {
                if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase))
                    return theme;
            }
        }

        return BuiltInThemes.Get(BuiltInThemes.DefaultId);
    }
}
