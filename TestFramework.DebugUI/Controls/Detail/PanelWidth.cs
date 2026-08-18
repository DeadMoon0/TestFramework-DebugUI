using System;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// How wide the step panel is allowed to be.
/// </summary>
/// <remarks>
/// <para>
/// The panel floats over the board, so every pixel it takes is a pixel of the run nobody can see. It is also
/// the panel holding a stack trace and a log, which is why it is draggable at all. The rules live here rather
/// than in the drag handler because a remembered width is applied on the next start too, on a window that may
/// be a different size on a different machine.
/// </para>
/// <para>
/// Nothing here throws on a nonsensical width. A settings file can hold anything, including a width saved on a
/// four-monitor desktop and read on a laptop, and a panel that refuses to appear is worse than a narrow one.
/// </para>
/// </remarks>
public static class PanelWidth
{
    /// <summary>The width the panel opens at before anyone has dragged it.</summary>
    public const double Default = 390;

    /// <summary>Narrower than this and the panel cannot hold a line of a stack trace.</summary>
    public const double Minimum = 300;

    /// <summary>Wider than this and it stops being a panel over a board.</summary>
    public const double Maximum = 900;

    /// <summary>How much of the window the board keeps whatever the panel is dragged to.</summary>
    /// <remarks>
    /// Roughly a step card's width. The board is the application; a panel that can be dragged over all of it
    /// is a panel that can hide the thing it is describing.
    /// </remarks>
    public const double Reserved = 200;

    /// <summary>
    /// The nearest width to the one asked for that is actually allowed.
    /// </summary>
    /// <param name="wanted">The width being dragged to, or the one read from settings.</param>
    /// <param name="available">
    /// How wide the window's content area is. Zero or <see cref="double.NaN"/> for a window that has not been
    /// measured yet, in which case only the fixed bounds apply.
    /// </param>
    public static double Clamp(double wanted, double available)
    {
        if (double.IsNaN(wanted) || wanted <= 0)
            wanted = Default;

        double ceiling = Maximum;

        if (!double.IsNaN(available) && available > 0)
        {
            // The reserve gives way before the minimum does. On a window too narrow to honour both, a panel
            // that is readable and covers most of the board beats one that is neither.
            ceiling = Math.Max(Minimum, Math.Min(Maximum, available - Reserved));
        }

        return Math.Clamp(wanted, Minimum, ceiling);
    }
}
