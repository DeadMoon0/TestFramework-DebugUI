using System;
using System.Windows;
using System.Windows.Input;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// One floating window, holding the panels of one <see cref="DockFloat"/>.
/// </summary>
/// <remarks>
/// <para>
/// One process, one store. A panel in here binds the same <c>StateStore</c> as a docked one, so it follows the
/// same run with nothing threaded through to it — which is the whole reason popping out is cheap in this
/// application and would not be in one that passed its data down a visual tree.
/// </para>
/// <para>
/// Owned by the main window and kept off the taskbar. Owned so it cannot be lost behind the window it belongs to,
/// which is a failure this tool already documents for its own placement; off the taskbar because a panel is part
/// of one application rather than a second thing the reader has open.
/// </para>
/// </remarks>
public partial class DockFloatWindow : Window
{
    /// <summary>Creates a floating window owned by the main one.</summary>
    public DockFloatWindow(Window owner)
    {
        InitializeComponent();

        Owner = owner;
    }

    /// <summary>Raised when the reader asks for the whole window to go away.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the window has been moved or resized, with where it now is.</summary>
    /// <remarks>
    /// Reported rather than written straight into the arrangement, so the host can ignore the events its own
    /// placement causes. A window told where to be, telling the arrangement where it is, is the shape of a loop.
    /// </remarks>
    public event Action<DockBounds>? Moved;

    /// <summary>Whether the window is currently being placed by the host rather than by the reader.</summary>
    private bool placing;

    /// <summary>Puts the float's tab strip and its panel into the window.</summary>
    /// <remarks>
    /// The strip is marked hit-test-visible in the chrome. It sits inside the caption, and without this the window
    /// chrome takes every press over it to drag the window — so the tabs could not be clicked and, worse, a panel
    /// could not be dragged back out of a float, because the gesture became "move this window" instead.
    /// </remarks>
    public void ShowContent(UIElement strip, UIElement body)
    {
        if (strip is not null)
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(strip, true);

        bStrip.Child = strip;
        bBody.Child = body;
    }

    /// <summary>
    /// Moves the window to where the arrangement says it is, without reporting it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Placed through Win32 in physical pixels rather than through <c>Left</c> and <c>Top</c>. Those are
    /// device-independent, and a float's bounds come from the pointer, which Windows reports in physical ones —
    /// mixing the two put a popped-out panel a thousand pixels from where it was dropped as soon as it landed on
    /// a monitor running at a different scale.
    /// </para>
    /// <para>
    /// Applied only when it actually differs. A window nudged by a fraction of a pixel on every redraw would
    /// report a move, which would change the arrangement, which would redraw — and the first version of the dock
    /// host is a standing reminder of where that ends.
    /// </para>
    /// </remarks>
    public void PlaceAt(DockBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        DockBounds now = Bounds();

        if (Same(now.Left, bounds.Left) && Same(now.Top, bounds.Top)
            && Same(now.Width, bounds.Width) && Same(now.Height, bounds.Height))
        {
            return;
        }

        IntPtr handle = Handle();

        if (handle == IntPtr.Zero)
            return;

        placing = true;

        try
        {
            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                (int)bounds.Left,
                (int)bounds.Top,
                (int)bounds.Width,
                (int)bounds.Height,
                NativeMethods.SWP_NOZORDER_NOACTIVATE);
        }
        finally
        {
            placing = false;
        }
    }

    /// <summary>Where the window is now, in the same physical pixels it is placed in.</summary>
    public DockBounds Bounds()
    {
        IntPtr handle = Handle();

        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out NativeMethods.RECT at))
            return new DockBounds();

        return new DockBounds
        {
            Left = at.Left,
            Top = at.Top,
            Width = at.Right - at.Left,
            Height = at.Bottom - at.Top
        };
    }

    private IntPtr Handle()
        => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    /// <inheritdoc />
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        Report();
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Report();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Escape closes the window, matching what it does to everything else in this tool: get this off my screen.
    /// The panels inside go back to being put away rather than being lost.
    /// </remarks>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e?.Key != Key.Escape)
            return;

        e.Handled = true;
        CloseRequested?.Invoke();
    }

    private void Report()
    {
        if (!placing && IsLoaded)
            Moved?.Invoke(Bounds());
    }

    private static bool Same(double one, double other) => Math.Abs(one - other) < 0.5;
}
