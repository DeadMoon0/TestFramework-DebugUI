using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// Where the board is being looked at from: how far in, and over which part.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here knows what is drawn. It moves two transforms — a scale and a translation — which the
/// marks layer shares by reference, so a drawing stays locked to the run without either side having
/// to notice the other.
/// </para>
/// <para>
/// The pan deliberately does not begin on the press. Capturing the mouse there is what made a step
/// impossible to select: the press bubbles up from the card to the surface, and once the surface
/// holds the mouse every later event is routed to it, so the release never reached the card. The
/// capture is taken on the first real movement instead, which is the moment it is actually needed.
/// </para>
/// </remarks>
internal sealed class BoardViewport
{
    private const double MinimumZoom = 0.2;
    private const double MaximumZoom = 3.0;

    /// <summary>How far Fit is allowed to magnify a board smaller than the window.</summary>
    /// <remarks>
    /// Fit used to stop at 1.0, so a four-step run sat at its drawn size in the middle of a large
    /// screen and used about a quarter of it. Letting it grow uses the space that is there. Capped
    /// well below <see cref="MaximumZoom"/> because past roughly double, a short run stops looking
    /// fitted and starts looking magnified.
    /// </remarks>
    private const double MaximumFitZoom = 2.0;

    /// <summary>How much clear space a fitted board keeps between itself and the window edge.</summary>
    private const double FitMargin = 24;

    /// <summary>How far a press has to travel before it pans the board instead of selecting.</summary>
    private const double DragThreshold = 4;

    private readonly FrameworkElement view;
    private readonly ScaleTransform zoom;
    private readonly TranslateTransform pan;

    private Point origin;
    private bool pressed;
    private bool panning;

    /// <summary>Looks at one board through one pair of transforms.</summary>
    /// <param name="view">The area the board is seen through, which is what it is fitted to.</param>
    /// <param name="zoom">How far in, shared with the marks layer by reference.</param>
    /// <param name="pan">Where over, shared with the marks layer by reference.</param>
    public BoardViewport(FrameworkElement view, ScaleTransform zoom, TranslateTransform pan)
    {
        this.view = view;
        this.zoom = zoom;
        this.pan = pan;
    }

    /// <summary>
    /// Set while a board is waiting for the surface to have a size it can be fitted to.
    /// </summary>
    /// <remarks>
    /// A run almost always arrives before the surface has been measured, so the first attempt has
    /// nothing to fit to. Leaving this set means the size-changed handler finishes the job the moment
    /// there is a size — rather than silently doing nothing and leaving the board at full scale,
    /// showing two steps.
    /// </remarks>
    public bool NeedsFit { get; set; }

    /// <summary>Whether the pointer has travelled far enough that this is a pan rather than a click.</summary>
    public bool IsPanning => panning;

    /// <summary>Puts the whole board on screen, centred.</summary>
    public void Fit(Size board)
    {
        double viewWidth = view.ActualWidth;
        double viewHeight = view.ActualHeight;

        if (viewWidth <= 0 || viewHeight <= 0 || board.Width <= 0 || board.Height <= 0)
            return;

        NeedsFit = false;

        // Measured against the window less a margin, so a fitted board has room to breathe instead
        // of touching all four edges.
        double scale = Math.Clamp(
            Math.Min((viewWidth - (FitMargin * 2)) / board.Width, (viewHeight - (FitMargin * 2)) / board.Height),
            MinimumZoom,
            MaximumFitZoom);

        zoom.ScaleX = scale;
        zoom.ScaleY = scale;

        pan.X = (viewWidth - (board.Width * scale)) / 2;
        pan.Y = (viewHeight - (board.Height * scale)) / 2;
    }

    /// <summary>Zooms about a point, so whatever is under the pointer stays under it.</summary>
    /// <param name="pointer">Where the pointer is, in board coordinates.</param>
    /// <param name="delta">Which way the wheel turned.</param>
    public void ZoomAt(Point pointer, int delta)
    {
        double factor = delta > 0 ? 1.1 : 1 / 1.1;
        double scale = Math.Clamp(zoom.ScaleX * factor, MinimumZoom, MaximumZoom);

        pan.X -= pointer.X * (scale - zoom.ScaleX);
        pan.Y -= pointer.Y * (scale - zoom.ScaleY);

        zoom.ScaleX = scale;
        zoom.ScaleY = scale;
    }

    /// <summary>Notes a press, without claiming it — it may yet turn out to be a click.</summary>
    public void Press(Point at)
    {
        origin = at;
        pressed = true;
    }

    /// <summary>
    /// Moves the board with the pointer, once it has travelled far enough to mean it.
    /// </summary>
    /// <remarks>
    /// Below the threshold this is a click that wobbled, not a drag. Panning from there would move the
    /// board a pixel and swallow the selection, which reads as the board ignoring the click.
    /// </remarks>
    /// <param name="to">Where the pointer is now.</param>
    /// <param name="capture">What takes the mouse once this is genuinely a pan.</param>
    public void Move(Point to, IInputElement capture)
    {
        if (!pressed)
            return;

        if (!panning)
        {
            if (Math.Abs(to.X - origin.X) < DragThreshold && Math.Abs(to.Y - origin.Y) < DragThreshold)
                return;

            panning = true;
            capture?.CaptureMouse();
            origin = to;
        }

        pan.X += to.X - origin.X;
        pan.Y += to.Y - origin.Y;

        origin = to;
    }

    /// <summary>
    /// Ends a pan, whether or not one had started.
    /// </summary>
    /// <remarks>
    /// Deliberately not conditional on anything. This is the one place a pan is ended, and a release
    /// that fails to reach it leaves the board following the pointer with no button held — which is what
    /// happened while the release was a bubbling handler and a card marked the click as its own.
    /// </remarks>
    /// <param name="capture">What is holding the mouse, to give it back.</param>
    public void Release(UIElement capture)
    {
        pressed = false;

        if (!panning)
            return;

        panning = false;
        capture?.ReleaseMouseCapture();
    }

    /// <summary>
    /// Puts a point of the board in the middle of the view, without changing how far in it is.
    /// </summary>
    /// <remarks>
    /// The size is the caller's rather than the view element's, because the two are not the same
    /// thing: a board is fitted to the surface it is drawn on, and jumped to a step against the
    /// control's own bounds. They are usually equal and were never required to be.
    /// </remarks>
    /// <param name="centre">The point to land on, in board coordinates.</param>
    /// <param name="within">What to centre it inside.</param>
    public void CentreOn(Point centre, Size within)
    {
        pan.X = (within.Width / 2) - (centre.X * zoom.ScaleX);
        pan.Y = (within.Height / 2) - (centre.Y * zoom.ScaleY);
    }
}
