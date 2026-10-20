using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// The drop hint for a floating window being moved, drawn above that window.
/// </summary>
/// <remarks>
/// <para>
/// A float is carried under the pointer, so the hint the host draws for it lies underneath the float itself - and an
/// insertion line is four pixels tall. The host's own canvas cannot be lifted above another window, so the hint is
/// given a window of its own for the length of the move.
/// </para>
/// <para>
/// See-through by <c>AllowsTransparency</c>, which suits this window and would not suit the float: this one has no
/// frame, no shadow and nothing to click, so it loses nothing by it. The float would lose its rounded frame, and the
/// layered style it would otherwise need is refused for a window created without it.
/// </para>
/// </remarks>
internal sealed class DockHintWindow : Window
{
    private readonly Border hint = new();

    /// <summary>Creates the overlay, hidden until a hint is shown.</summary>
    public DockHintWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;

        Content = hint;
    }

    /// <summary>Shows a hint over a rectangle of the screen, in physical pixels.</summary>
    /// <param name="onScreen">Where the hint goes.</param>
    /// <param name="insertion">Whether it is a line between two cards rather than a region to fill.</param>
    /// <param name="accent">The accent the hint is drawn in.</param>
    /// <param name="wash">The accent, washed, that a region is filled with.</param>
    public void ShowAt(Rect onScreen, bool insertion, Brush accent, Brush wash)
    {
        hint.CornerRadius = new CornerRadius(insertion ? 2 : DockMetrics.CardRadius);
        hint.Background = insertion ? accent : wash;
        hint.BorderBrush = insertion ? null : accent;
        hint.BorderThickness = new Thickness(insertion ? 0 : 1.5);

        if (!IsVisible)
            Show();

        IntPtr handle = new WindowInteropHelper(this).Handle;

        // Through Win32 in physical pixels, like the float it sits over: the rectangle comes from the screen, and a
        // window's own Left and Top are device-independent.
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            (int)onScreen.Left,
            (int)onScreen.Top,
            Math.Max(1, (int)onScreen.Width),
            Math.Max(1, (int)onScreen.Height),
            NativeMethods.SWP_NOZORDER_NOACTIVATE);
    }
}
