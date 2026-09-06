using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// Dragging a panel from where it is to where it is being put.
/// </summary>
/// <remarks>
/// <para>
/// Three things have to be true at once for this to feel like moving a panel rather than fighting one:
/// the same press that reveals a panel must not also move it, the answer to "where would this land"
/// must be available on every mouse move, and letting go over nothing must mean a window of its own
/// rather than nothing at all. Each of those is a rule about the gesture, which is why they are here
/// and not spread through the thing being dragged.
/// </para>
/// <para>
/// Card positions are remembered rather than worked out: a card has no position until the layout has
/// arranged it, and a drag needs positions thousands of times while a redraw happens once.
/// </para>
/// </remarks>
internal sealed class DockDragController
{
    /// <summary>The clipboard format a dragged panel travels under.</summary>
    /// <remarks>
    /// Named for this application, so a drag from somewhere else cannot be mistaken for one of ours — and so ours
    /// is refused by everything else.
    /// </remarks>
    private const string PanelFormat = "TestFramework.DebugUI.Panel";

    /// <summary>How far the pointer travels on a header before it counts as a drag rather than a click.</summary>
    /// <remarks>
    /// Clicking a header reveals its panel and dragging it moves the panel, so one gesture has to be told from the
    /// other. Below this it is a click, which is why a slightly unsteady press still selects rather than moves.
    /// </remarks>
    private const double DragThreshold = 6;

    private readonly FrameworkElement surface;
    private readonly Panel hints;

    /// <summary>Every card on screen, collected as they are drawn so a drag has an answer for every pixel.</summary>
    private readonly List<DockCard> cards = [];

    /// <summary>Where the pointer went down on a header, until it has moved far enough to mean a drag.</summary>
    private Point? pressedAt;

    /// <summary>Takes drops for one host, and draws its hints.</summary>
    /// <param name="surface">
    /// The host. It takes the drop rather than each card, because most of the answers are about the space between
    /// cards and the edges of the window — places no card owns.
    /// </param>
    /// <param name="hints">The canvas a hint is drawn into.</param>
    public DockDragController(FrameworkElement surface, Panel hints)
    {
        this.surface = surface;
        this.hints = hints;

        surface.AllowDrop = true;
        surface.DragOver += WhileDragging;
        surface.Drop += OnDropped;
        surface.DragLeave += (_, _) => ClearHint();
    }

    /// <summary>
    /// Asks the host to note where the cards are, just before a drag begins.
    /// </summary>
    /// <remarks>
    /// Positions are refreshed on every layout pass anyway, so this is belt and braces — and it is worth
    /// keeping: a drag started from a card that moved without the layout running would otherwise be
    /// answered against where the cards used to be.
    /// </remarks>
    public Action? Refresh { get; set; }

    /// <summary>The panel a drag is carrying, or null when the drag is not one of ours.</summary>
    public static PanelId? Carried(DragEventArgs e)
        => e?.Data?.GetDataPresent(PanelFormat) == true && e.Data.GetData(PanelFormat) is PanelId panel ? panel : null;

    /// <summary>
    /// Makes a header or a tab something a panel can be dragged by.
    /// </summary>
    /// <remarks>
    /// The header is where a window is dragged by everywhere else, so it is where a panel is dragged by here. The
    /// press is remembered rather than acted on, because the same press is also how a panel is revealed — only
    /// once the pointer has travelled does it become a move.
    /// </remarks>
    public void MakeDraggable(FrameworkElement handle, PanelId panel)
    {
        // Measured against the handle rather than the host: the same headers and tabs are drawn inside floating
        // windows, and asking for a position in another window's tree is not a question that has an answer.
        handle.PreviewMouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(handle);
        handle.PreviewMouseLeftButtonUp += (_, _) => pressedAt = null;

        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || pressedAt is not { } from)
                return;

            Point now = e.GetPosition(handle);

            if (Math.Abs(now.X - from.X) < DragThreshold && Math.Abs(now.Y - from.Y) < DragThreshold)
                return;

            pressedAt = null;

            Refresh?.Invoke();

            DataObject carried = new(PanelFormat, panel);

            // Blocks until the drag ends, which is what lets the arrangement be changed exactly once, on release.
            DragDropEffects taken = DragDrop.DoDragDrop(handle, carried, DragDropEffects.Move);

            ClearHint();

            // Nothing accepted it. That is either a drop into empty space — which means a window of its own — or a
            // drag the reader abandoned with Escape, and the two are told apart by where the pointer ended up
            // rather than by the result, because cancelling reports exactly the same thing as dropping nowhere.
            if (taken == DragDropEffects.None && OutsideEveryWindow(out Point where))
                Arrangement.Apply(layout => layout.Float(panel, Near(where)));
        };
    }

    /// <summary>
    /// Notes where every card ended up, once the layout has actually placed them.
    /// </summary>
    /// <remarks>
    /// After layout rather than during it: a card has no position until it has been arranged, and a drag needs
    /// positions rather than the tree it could work them out from. Recorded once per redraw and read on every
    /// mouse move, which is the right way round for the thing that happens thousands of times.
    /// </remarks>
    /// <param name="drawn">What is currently drawn, which is what says where a panel belongs.</param>
    /// <param name="faces">The card each panel is drawn in.</param>
    public void Remember(DockLayout? drawn, IEnumerable<KeyValuePair<PanelId, FrameworkElement>> faces)
    {
        cards.Clear();

        if (drawn is null || surface.ActualWidth <= 0)
            return;

        foreach (KeyValuePair<PanelId, FrameworkElement> held in faces)
        {
            if (!held.Value.IsVisible)
                continue;

            DockLocation at = drawn.Locate(held.Key);

            if (at.Placement != DockPlacement.Side)
                continue;

            try
            {
                Point corner = held.Value.TransformToAncestor(surface).Transform(new Point(0, 0));

                cards.Add(new DockCard
                {
                    Panel = held.Key,
                    Side = at.Side,
                    Index = at.Index,
                    Bounds = new Rect(corner, new Size(held.Value.ActualWidth, held.Value.ActualHeight))
                });
            }
            catch (InvalidOperationException)
            {
                // Not in this visual tree yet. It will be by the next redraw, and a drag before then simply
                // finds one card fewer rather than failing.
            }
        }
    }

    /// <summary>
    /// What a point on the screen would do, in the host's own terms.
    /// </summary>
    /// <remarks>
    /// Screen coordinates because that is what a window being dragged reports. Outside the host it answers
    /// nothing, which is what keeps a float dragged across the desktop from docking itself into a window it is
    /// nowhere near.
    /// </remarks>
    public DockDrop? Resolve(Point onScreen, PanelId carried)
    {
        if (!surface.IsVisible || surface.ActualWidth <= 0)
            return null;

        Point here = surface.PointFromScreen(onScreen);

        if (here.X < 0 || here.Y < 0 || here.X > surface.ActualWidth || here.Y > surface.ActualHeight)
            return null;

        return DockDrop.Resolve(here, new Size(surface.ActualWidth, surface.ActualHeight), cards, carried);
    }

    /// <summary>
    /// Draws the hint, as a region to fill or a line to slot into.
    /// </summary>
    /// <remarks>
    /// Two shapes because they answer two different questions. A filled region says the panel will occupy this
    /// space; a line says it will go between these two. Drawing an insertion as a region would claim it was about
    /// to cover the card it is only going above.
    /// </remarks>
    public void ShowHint(DockDrop? drop)
    {
        hints.Children.Clear();

        if (drop is null)
            return;

        Brush accent = (Brush)surface.FindResource(ThemeKeys.Accent);

        Border hint = new()
        {
            Width = Math.Max(0, drop.Hint.Width),
            Height = Math.Max(0, drop.Hint.Height),
            CornerRadius = new CornerRadius(drop.IsInsertion ? 2 : DockMetrics.CardRadius),
            Background = drop.IsInsertion ? accent : ThemeFollow.Wash(ThemeKeys.Accent, 0x30),
            BorderBrush = drop.IsInsertion ? null : accent,
            BorderThickness = new Thickness(drop.IsInsertion ? 0 : 1.5)
        };

        Canvas.SetLeft(hint, drop.Hint.Left);
        Canvas.SetTop(hint, drop.Hint.Top);

        hints.Children.Add(hint);
    }

    /// <summary>Takes the hint away.</summary>
    public void ClearHint() => hints.Children.Clear();

    /// <summary>Shows where the panel under the pointer would land.</summary>
    private void WhileDragging(object sender, DragEventArgs e)
    {
        if (Carried(e) is not { } dragged)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        DockDrop? drop = DockDrop.Resolve(
            e.GetPosition(surface),
            new Size(surface.ActualWidth, surface.ActualHeight),
            cards,
            dragged);

        ShowHint(drop);

        e.Effects = drop is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Moves the panel to wherever the pointer let go.</summary>
    private void OnDropped(object sender, DragEventArgs e)
    {
        ClearHint();

        if (Carried(e) is not { } dragged)
            return;

        DockDrop? drop = DockDrop.Resolve(
            e.GetPosition(surface),
            new Size(surface.ActualWidth, surface.ActualHeight),
            cards,
            dragged);

        if (drop is null)
            return;

        e.Handled = true;

        DockSide side = drop.Side;
        int index = drop.Index;

        Arrangement.Apply(layout => layout.Move(dragged, side, index));
    }

    /// <summary>
    /// Whether the pointer is outside every window of this application, and where.
    /// </summary>
    /// <remarks>
    /// Asked at the end of a drag nothing accepted. A pointer still over one of our windows means the reader
    /// cancelled — Escape reports the same "nothing took it" as a drop into the desktop — and cancelling must not
    /// leave a new window behind.
    /// </remarks>
    private static bool OutsideEveryWindow(out Point where)
    {
        where = NativeMethods.CursorPosition();

        foreach (Window window in Application.Current.Windows)
        {
            if (!window.IsVisible)
                continue;

            // Read from Win32 rather than from Left and Top, so the comparison happens in the same physical
            // pixels the pointer was reported in. The two spaces part company the moment a second monitor runs
            // at a different scale, which is precisely when somebody is dragging a panel onto it.
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out NativeMethods.RECT at))
                continue;

            Rect bounds = new(at.Left, at.Top, at.Right - at.Left, at.Bottom - at.Top);

            if (bounds.Contains(where))
                return false;
        }

        return true;
    }

    /// <summary>A float placed under the pointer rather than at its corner.</summary>
    /// <remarks>
    /// Offset by a little, so the new window appears where the panel was let go instead of hanging its title bar
    /// off the cursor — and so the pointer is over the window's body, ready to drag it again.
    /// </remarks>
    private static DockBounds Near(Point where) => new DockBounds
    {
        Left = where.X - 60,
        Top = where.Y - 14,
        Width = 480,
        Height = 560
    }.Sane();
}
