using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TestFramework.DebugUI.Theme;

/// <summary>Why the compositor will not blur what is behind a window.</summary>
/// <remarks>
/// Public only because the settings panel is: it appears in the signature that hands the picker its
/// answer. Nothing outside this assembly has any use for it.
/// </remarks>
public enum BlurBlock
{
    /// <summary>It will. A see-through theme can be offered.</summary>
    None,

    /// <summary>Energy saver is on, which turns off every compositor effect.</summary>
    EnergySaver,

    /// <summary>Transparency effects are switched off in Windows' own settings.</summary>
    TransparencyOff,

    /// <summary>Windows says no and will not say why.</summary>
    Unknown
}

/// <summary>
/// Whether Windows will actually draw the blur a see-through theme is asking for.
/// </summary>
/// <remarks>
/// <para>
/// Worth asking because the answer is invisible otherwise. Nothing fails when a compositor effect is
/// switched off: <c>SetWindowCompositionAttribute</c> returns success, the window is composited, and
/// the blur is quietly replaced by a flat fill of the tint. A see-through theme in that state is not a
/// slightly worse version of itself — it is a window with the desktop showing through it sharply.
/// </para>
/// <para>
/// <b>Availability and the reason for it come from different places, on purpose.</b> Windows publishes
/// the decision it makes for its own acrylic as one flag, which already accounts for every cause,
/// present and future. It does not say which cause applied, so the reason is worked out separately and
/// only to have something to tell the reader — a wrong <em>reason</em> is a confusing tooltip, while a
/// wrong <em>answer</em> is a theme wrongly taken away.
/// </para>
/// <para>
/// Both halves fail open. An answer that cannot be got is taken as yes: a theme that looks a little
/// flatter than it should is a smaller problem than a theme nobody can choose because a version of
/// Windows did not implement a flag.
/// </para>
/// </remarks>
internal static class BlurSupport
{
    /// <summary>
    /// Asks Windows whether it will draw compositor effects at all, and why not if it will not.
    /// </summary>
    /// <remarks>
    /// Dishonest twice over — it reads a system setting and the power state, and both can change while
    /// the tool is running. See <see cref="WhenChanged"/> for being told rather than asking again.
    /// </remarks>
    public static BlurBlock Detect()
        => EffectsEnabled() ? BlurBlock.None : Reason();

    /// <summary>
    /// Calls back whenever Windows changes its mind, so a picker can be redrawn.
    /// </summary>
    /// <remarks>
    /// The change is not the tool's to make and can happen at any time — a laptop reaching a battery
    /// threshold turns energy saver on by itself, with no window involved.
    /// </remarks>
    /// <param name="changed">Told that the answer may now be different. Not told what it is.</param>
    /// <returns>The settings object the subscription lives on. Held, or the callback stops arriving.</returns>
    public static object? WhenChanged(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        try
        {
            Windows.UI.ViewManagement.UISettings settings = new();

            settings.AdvancedEffectsEnabledChanged += (_, _) => changed();

            return settings;
        }
        catch (Exception)
        {
            // No notifications, then. Every picker is rebuilt when it opens anyway.
            return null;
        }
    }

    /// <summary>
    /// What to tell somebody hovering a theme they cannot choose.
    /// </summary>
    /// <remarks>
    /// Pure, so the wording is a value rather than a thing the window does. Each one says what is true,
    /// then what to change — a message that only says "unavailable" makes the reader go looking.
    /// </remarks>
    public static string Explain(string themeName, BlurBlock block)
        => block switch
        {
            BlurBlock.EnergySaver
                => $"{themeName} needs the blur behind the window, and energy saver switches that off. "
                 + "Turn energy saver off in Windows' battery settings and it can be chosen again.",

            BlurBlock.TransparencyOff
                => $"{themeName} needs the blur behind the window, and transparency effects are off. "
                 + "Turn them on in Windows' Settings, under Personalisation and then Colours.",

            BlurBlock.Unknown
                => $"{themeName} needs the blur behind the window, and Windows is not drawing "
                 + "compositor effects at the moment. Energy saver and the transparency setting are the "
                 + "usual reasons.",

            _ => themeName
        };

    /// <summary>
    /// Windows' own answer to "should I draw acrylic", which is the same question this is.
    /// </summary>
    /// <remarks>
    /// One flag rather than a list of causes the tool would have to keep up with. It already covers the
    /// transparency setting and energy saver, and it will cover whatever is added next.
    /// </remarks>
    private static bool EffectsEnabled()
    {
        try
        {
            // Two questions, because no single flag answers both. The first is the transparency setting,
            // which somebody turns off on purpose; the second is energy saving, which the machine can
            // turn on by itself and which no transparency flag reflects.
            return new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled && !EnergySaverOn();
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static BlurBlock Reason()
    {
        // Asked first because it overrides the setting below: energy saving suppresses the effects while
        // the transparency setting still reads as on, which is what makes this so hard to see.
        if (EnergySaverOn())
            return BlurBlock.EnergySaver;

        if (TransparencyOff())
            return BlurBlock.TransparencyOff;

        return BlurBlock.Unknown;
    }

    /// <summary>The power mode Windows is actually running in, whatever the slider was left on.</summary>
    private static readonly Guid BestPowerEfficiency = new("961CC777-2547-4F9D-8174-7D86181B8A7A");

    /// <summary>
    /// Whether Windows is saving power, which is what turns the compositor's effects off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <em>effective</em> overlay rather than the actual one, and that distinction is the whole
    /// answer. Energy saving does not move the slider: it leaves the actual overlay wherever the reader
    /// put it and forces the effective one to power efficiency. Measured on a machine with energy saving
    /// on — actual <c>Best performance</c>, effective <c>Best power efficiency</c> — while the blur was
    /// definitely off.
    /// </para>
    /// <para>
    /// <b>Four more obvious sources were tried first and every one of them was wrong.</b> With energy
    /// saving on and the blur measurably filling flat black,
    /// <c>UISettings.AdvancedEffectsEnabled</c> read <c>true</c>,
    /// <c>PowerManager.EnergySaverStatus</c> read <c>Disabled</c>,
    /// <c>SYSTEM_POWER_STATUS.SystemStatusFlag</c> read <c>0</c>, and
    /// <c>GUID_ENERGY_SAVER_STATUS</c> — the setting named after the feature — read <c>0</c> as well.
    /// None of them is a substitute for this one, and none of them should be re-tried as a simplification.
    /// </para>
    /// </remarks>
    private static bool EnergySaverOn()
    {
        try
        {
            return PowerGetEffectiveOverlayScheme(out Guid scheme) == 0 && scheme == BestPowerEfficiency;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TransparencyOff()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency",
                defaultValue: 1);

            return value is int enabled && enabled == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
}
