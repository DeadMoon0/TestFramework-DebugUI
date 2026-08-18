using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TestFramework.DebugUI.Copying;

/// <summary>
/// The acknowledgement a copy button gives.
/// </summary>
/// <remarks>
/// The clipboard is invisible, so a copy with no acknowledgement is indistinguishable from a button that
/// does nothing. Shared rather than written twice: the hover button on a field and the button beside a
/// heading are the same gesture, and they would eventually have disagreed about how long a tick lasts.
/// </remarks>
public static class CopyGlyph
{
    /// <summary>How long the tick stays up.</summary>
    public static readonly TimeSpan Acknowledgement = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Turns a copy glyph into a tick, and back again shortly after.
    /// </summary>
    /// <param name="glyph">The stroked path inside the button.</param>
    /// <param name="resources">
    /// Where to look the icons and brushes up. The glyph in a hover button is not in the visual tree yet, so
    /// it cannot find them itself; the field it was raised over can.
    /// </param>
    public static void Confirm(Path glyph, FrameworkElement resources)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(resources);

        Cancel(glyph);

        glyph.Data = (Geometry)resources.FindResource("IconTick");

        // The accent, not the green that means a step passed. This tick reports a clipboard, and a verdict
        // colour on it reads as a verdict about the thing that was copied.
        glyph.Stroke = (Brush)resources.FindResource("Accent");

        // The timer is parked on the glyph it belongs to, so a second copy before the first tick has faded
        // replaces that tick rather than racing it back to the copy icon.
        DispatcherTimer revert = new() { Interval = Acknowledgement };

        revert.Tick += (_, _) =>
        {
            Cancel(glyph);

            glyph.Data = (Geometry)resources.FindResource("IconCopy");
            glyph.Stroke = (Brush)resources.FindResource("TextSecondary");
        };

        glyph.Tag = revert;
        revert.Start();
    }

    /// <summary>Stops a pending revert, for a button being taken off screen before its tick faded.</summary>
    public static void Cancel(Path glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.Tag is DispatcherTimer pending)
        {
            pending.Stop();
            glyph.Tag = null;
        }
    }
}
