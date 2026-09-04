using System;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// The draggable edge of a well, and the arithmetic of how much of the window each side has.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a fraction. A side with nothing in it weighs nothing, so an empty well takes no
/// room without needing to be hidden; the middle takes what is left; and because they are all star
/// weights the whole arrangement rescales with the window on its own. Nothing here reads a size,
/// which is what stops it feeding back into the layout that called it — an early version did, and
/// drove a well to 2380 pixels inside a 1000-pixel window.
/// </para>
/// <para>
/// The host keeps the one thing that is genuinely its own: setting the stars on its own grid. What
/// arrives here is the gesture, the clamping, and the question of how much the pinned wells have
/// taken from the canvas.
/// </para>
/// </remarks>
internal sealed class DockSplitters
{
    /// <summary>How thick the draggable edge of a well is.</summary>
    /// <remarks>
    /// Six, straddling the boundary, for the reason the step panel's own handle already gives: half over the
    /// panel and half over what is beside it is where a pointer aiming at an edge actually ends up.
    /// </remarks>
    private const double GripThickness = 6;

    private readonly FrameworkElement surface;
    private readonly Func<DockLayout?> drawn;
    private readonly Action<double, double, double> weigh;

    private DockSide sizing;
    private bool isSizing;
    private double sizingFrom;
    private double sizingExtent;

    /// <summary>Sizes the wells of one host.</summary>
    /// <param name="surface">What a drag is measured against, which is the host itself.</param>
    /// <param name="drawn">What is currently drawn, read rather than held so it is never stale.</param>
    /// <param name="weigh">Sets the three weights on the host's own grid, and gives the middle the rest.</param>
    public DockSplitters(FrameworkElement surface, Func<DockLayout?> drawn, Action<double, double, double> weigh)
    {
        this.surface = surface;
        this.drawn = drawn;
        this.weigh = weigh;
    }

    /// <summary>Raised with what fraction of the window the pinned wells have reserved.</summary>
    public event Action<DockInsets>? InsetsChanged;

    /// <summary>The edge of one side's well, which is what a reader drags to resize it.</summary>
    public Border Grip(DockSide side)
    {
        bool horizontal = side != DockSide.Bottom;

        Border grip = new()
        {
            Background = Brushes.Transparent,
            Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            Width = horizontal ? GripThickness : double.NaN,
            Height = horizontal ? double.NaN : GripThickness,
            HorizontalAlignment = side switch
            {
                DockSide.Left => HorizontalAlignment.Right,
                DockSide.Right => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Stretch
            },
            VerticalAlignment = side == DockSide.Bottom ? VerticalAlignment.Top : VerticalAlignment.Stretch
        };

        grip.MouseLeftButtonDown += (_, e) =>
        {
            isSizing = true;
            sizing = side;
            sizingExtent = Arrangement.Current.At(side).Extent;
            sizingFrom = horizontal ? e.GetPosition(surface).X : e.GetPosition(surface).Y;
            grip.CaptureMouse();
        };

        grip.MouseMove += (_, e) =>
        {
            if (!isSizing || sizing != side)
                return;

            double span = horizontal ? surface.ActualWidth : surface.ActualHeight;

            if (span <= 0)
                return;

            double moved = (horizontal ? e.GetPosition(surface).X : e.GetPosition(surface).Y) - sizingFrom;

            // Left grows to the right; right and bottom grow the other way.
            double delta = (side == DockSide.Left ? moved : -moved) / span;

            sizingExtent = Math.Clamp(sizingExtent + delta, DockWell.MinimumExtent, DockWell.MaximumExtent);
            sizingFrom = horizontal ? e.GetPosition(surface).X : e.GetPosition(surface).Y;

            Preview(side, sizingExtent);
        };

        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!isSizing)
                return;

            isSizing = false;
            grip.ReleaseMouseCapture();

            DockSide settled = sizing;
            double extent = sizingExtent;

            Arrangement.Apply(layout => layout.Resize(settled, extent));
        };

        return grip;
    }

    /// <summary>
    /// Gives every side its share, as a weight rather than a measurement.
    /// </summary>
    /// <remarks>
    /// A side with nothing in it weighs nothing, so an empty well takes no room without needing to be hidden. The
    /// middle takes what is left, and because these are all stars the whole thing rescales with the window on its
    /// own — nothing here reads a size, which is what stops this feeding back into the layout that called it.
    /// </remarks>
    public void Apply()
    {
        if (drawn() is null)
            return;

        weigh(Share(DockSide.Left), Share(DockSide.Right), Share(DockSide.Bottom));

        InsetsChanged?.Invoke(Insets());
    }

    /// <summary>
    /// Moves one edge while it is being dragged, without touching the arrangement.
    /// </summary>
    /// <remarks>
    /// The arrangement is written to the settings file whenever it changes, so committing on every mouse move
    /// would write the file forty times a second. The drag moves the weights; releasing the pointer records where
    /// it ended up — the same call the step panel's own handle used to make.
    /// </remarks>
    public void Preview(DockSide side, double extent)
    {
        if (drawn() is null)
            return;

        weigh(
            side == DockSide.Left ? extent : Share(DockSide.Left),
            side == DockSide.Right ? extent : Share(DockSide.Right),
            side == DockSide.Bottom ? extent : Share(DockSide.Bottom));
    }

    /// <summary>What fraction of the window a side takes, or nothing when it holds nothing.</summary>
    private double Share(DockSide side)
    {
        DockWell well = drawn()?.At(side) ?? DockWell.Empty;

        return well.IsEmpty ? 0 : well.Extent;
    }

    /// <summary>
    /// How much the pinned wells have taken from the canvas.
    /// </summary>
    /// <remarks>
    /// A fraction of the window rather than pixels, applied by the window to the board's own margin. Reading a
    /// size here is what the first version got wrong, so this reports proportions and lets the caller turn them
    /// into a margin against a size it already has.
    /// </remarks>
    private DockInsets Insets() => new()
    {
        Left = Reserved(DockSide.Left),
        Right = Reserved(DockSide.Right),
        Bottom = Reserved(DockSide.Bottom)
    };

    private double Reserved(DockSide side)
    {
        DockWell well = drawn()?.At(side) ?? DockWell.Empty;

        return well.Pinned && !well.IsEmpty ? well.Extent : 0;
    }

    /// <summary>
    /// The handle between two stacked panels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Straddles the boundary, half over each card, because that is where a pointer aiming at the space between
    /// two things actually ends up. Invisible until it is worth noticing: a permanent seam between cards would
    /// undo the point of drawing them as separate things in the first place.
    /// </para>
    /// <para>
    /// Moves the rows while it is dragged and commits to the arrangement on release, for the same reason the
    /// well's own edge does — the arrangement is written to the settings file whenever it changes, and a file
    /// written forty times a second is a file written for no reason.
    /// </para>
    /// </remarks>
    public Border Boundary(DockSide side, Grid rail, int boundary)
    {
        Border grip = new()
        {
            Height = GripThickness * 2,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNS,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(DockMetrics.CardGap, -GripThickness, DockMetrics.CardGap, 0),
            ToolTip = "Drag to share the space between these panels"
        };

        Grid.SetRow(grip, boundary + 1);

        double from = 0;
        double moved = 0;
        bool sizing = false;

        grip.MouseLeftButtonDown += (_, e) =>
        {
            sizing = true;
            moved = 0;
            from = e.GetPosition(rail).Y;
            grip.CaptureMouse();
        };

        grip.MouseMove += (_, e) =>
        {
            if (!sizing || rail.ActualHeight <= 0)
                return;

            double now = e.GetPosition(rail).Y;

            moved += (now - from) / rail.ActualHeight;
            from = now;

            Preview(rail, Arrangement.Current.At(side).Divide(boundary, moved).Shares);
        };

        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!sizing)
                return;

            sizing = false;
            grip.ReleaseMouseCapture();

            double settled = moved;

            Arrangement.Apply(layout => layout.Divide(side, boundary, settled));
        };

        return grip;
    }

    /// <summary>Shows a division while it is being dragged, without touching the arrangement.</summary>
    public static void Preview(Grid rail, ImmutableList<double> shares)
    {
        for (int row = 0; row < rail.RowDefinitions.Count && row < shares.Count; row++)
            rail.RowDefinitions[row].Height = new GridLength(shares[row], GridUnitType.Star);
    }
}
