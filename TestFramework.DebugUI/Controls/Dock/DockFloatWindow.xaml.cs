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

        // The corners and the message hook both need the window to exist as a Win32 one, which it does not until
        // the source has been created.
        SourceInitialized += (_, _) =>
        {
            ArcylicManager.RoundCorners(Handle());

            if (System.Windows.Interop.HwndSource.FromHwnd(Handle()) is { } source)
                source.AddHook(OnMessage);
        };
    }

    /// <summary>Raised while the reader is dragging the window, with where the pointer is.</summary>
    /// <remarks>
    /// Moving a float over the main window is how a single panel gets docked again: there is no tab to drag, and
    /// the caption belongs to the window. So the move doubles as the drag, and this is what lets the host show
    /// where the panel would land.
    /// </remarks>
    public event Action<Point>? Dragging;

    /// <summary>Raised when the reader stops dragging the window, with where they let go.</summary>
    public event Action<Point>? Dropped;

    /// <summary>Whether the reader is dragging the window right now.</summary>
    private bool dragging;

    /// <summary>
    /// Watches for the start and end of a system move.
    /// </summary>
    /// <remarks>
    /// The two messages Windows sends around its own move loop. There is no WPF event for either, and the
    /// alternative — deciding a move has ended because the position stopped changing — guesses at a fact the
    /// system is willing to state.
    /// </remarks>
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        const int EnterSizeMove = 0x0231;
        const int ExitSizeMove = 0x0232;

        if (message == EnterSizeMove)
            dragging = true;

        if (message == ExitSizeMove && dragging)
        {
            dragging = false;
            Dropped?.Invoke(NativeMethods.CursorPosition());
        }

        return IntPtr.Zero;
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

    /// <summary>Puts the float's caption content and its panel into the window.</summary>
    /// <remarks>
    /// <para>
    /// Only what is meant to be clicked is taken out of the chrome's hands — the buttons, and the tabs when there
    /// is more than one. Everything else in the caption is left to the window, so the caption drags the window the
    /// way every other window's does.
    /// </para>
    /// <para>
    /// It was the whole strip to begin with, which meant the caption belonged to the panel and the window could
    /// not be moved at all. A popped-out panel is a window first; the arranging comes second.
    /// </para>
    /// </remarks>
    public void ShowContent(UIElement caption, UIElement body)
    {
        if (caption is not null)
            LetTheChromeHaveTheRest(caption);

        bStrip.Child = caption;
        bBody.Child = body;
    }

    /// <summary>
    /// Marks the pressable things in the caption, and nothing else.
    /// </summary>
    /// <remarks>
    /// A tab is a border rather than a button, so it has to be named as well — but a tab does not span the strip,
    /// which is what leaves the rest of the caption for the window to be dragged by.
    /// </remarks>
    private static void LetTheChromeHaveTheRest(DependencyObject caption)
    {
        int children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(caption);

        if (caption is System.Windows.Controls.Primitives.ButtonBase || Interactive(caption))
        {
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome((UIElement)caption, true);
            return;
        }

        for (int index = 0; index < children; index++)
            LetTheChromeHaveTheRest(System.Windows.Media.VisualTreeHelper.GetChild(caption, index));

        // A caption whose visual tree has not been built yet answers with no children, so the marking is done
        // again once it has been. Without this a float opens with unclickable tabs until something else redraws it.
        if (children == 0 && caption is FrameworkElement pending && !pending.IsLoaded)
            pending.Loaded += Mark;

        static void Mark(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            element.Loaded -= Mark;
            LetTheChromeHaveTheRest(element);
        }
    }

    /// <summary>Whether an element is something the reader is meant to press rather than drag the window by.</summary>
    private static bool Interactive(DependencyObject element)
        => element is FrameworkElement { Cursor: not null } framework
           && framework.Cursor == System.Windows.Input.Cursors.Hand;

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
        if (placing || !IsLoaded)
            return;

        Moved?.Invoke(Bounds());

        if (dragging)
            Dragging?.Invoke(NativeMethods.CursorPosition());
    }

    private static bool Same(double one, double other) => Math.Abs(one - other) < 0.5;
}
