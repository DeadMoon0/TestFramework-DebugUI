using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TestFramework.DebugUI.State.Settings;
using static TestFramework.DebugUI.NativeMethods;

namespace TestFramework.DebugUI;

/// <summary>
/// The window as the operating system sees it: its corners, its blur, and where it sits.
/// </summary>
/// <remarks>
/// <para>
/// A borderless window has to do for itself what a frame would otherwise do. Maximising one covers
/// the taskbar unless it is told the work area; rounding its corners is a call rather than a style;
/// and putting it back where it was means checking that "where it was" still exists.
/// </para>
/// <para>
/// Gathered here because none of it is about what the tool shows — it is the same for any borderless
/// window, and it is the only part of the window written in terms of handles and structs.
/// </para>
/// </remarks>
internal static class WindowChromeInterop
{
    /// <summary>
    /// Hooks a window so it behaves like a framed one, once it has a handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from <c>SourceInitialized</c> and not before: there is no handle to hook, round or blur
    /// until then.
    /// </para>
    /// <para>
    /// The tint used to be written here as <c>Color.FromArgb(200, 0, 0, 0)</c>, which meant every
    /// surface in the tool was sitting on a seventy-eight percent black wash that no palette could see
    /// or account for — and a light theme drawn without knowing that would have come out grey. Taking
    /// it from the theme is also the whole of what makes a see-through theme possible: lower the alpha
    /// and the desktop is the background.
    /// </para>
    /// </remarks>
    /// <param name="window">The window.</param>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr handle = new WindowInteropHelper(window).Handle;

        HwndSource.FromHwnd(handle).AddHook(new HwndSourceHook(WindowProc));

        // Asked for once the handle exists, so the window and its popped-out panels are rounded the same way.
        WindowEffects.RoundCorners(handle);
    }

    /// <summary>
    /// What the window lies on, which on this build is nothing the system provides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is layered, and that alone is what lets the desktop through: what it does not paint, it
    /// does not cover. How much shows is the theme's <c>WindowTint</c>, painted by the window itself.
    /// </para>
    /// <para>
    /// <b>There is no blur, and the reason is the operating system rather than anything here.</b> Asking
    /// for one is a request — neither <c>SetWindowCompositionAttribute</c> nor <c>DwmSetWindowAttribute</c>
    /// renders anything into this window, they ask the compositor to draw behind it — and on Windows 11
    /// build 26200 nothing the compositor offers samples what is behind.
    /// </para>
    /// <para>
    /// Measured in a bare ninety-line WPF window with none of this tool in it, over a maximised page of
    /// twelve-pixel black-and-white stripes, reading the spread of a five-hundred-pixel run across the
    /// middle. A blur flattens the stripes; a fill flattens them too, so the fills are told apart from
    /// each other and from the sharp control by their value:
    /// <list type="bullet">
    /// <item>no accent: spread <c>254</c>. The stripes come through sharp, so the layering itself works.</item>
    /// <item><c>ACCENT_ENABLE_TRANSPARENTGRADIENT</c>: flat <c>177</c>, the same at every gradient alpha.</item>
    /// <item><c>ACCENT_ENABLE_BLURBEHIND</c>: flat <c>0</c>. The gradient paints at <em>full</em> opacity
    /// whatever alpha it carries, so it fills rather than tints.</item>
    /// <item><c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c>: flat <c>0</c>. This is the call the tool originally
    /// shipped with, and it did blur on an older build — the code was never wrong.</item>
    /// <item><c>ACCENT_ENABLE_HOSTBACKDROP</c>: spread <c>254</c>, indistinguishable from no accent.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <c>DWMWA_SYSTEMBACKDROP_TYPE</c> needs a window that is not layered <em>and</em> a client area the
    /// frame has been extended into; without <c>DwmExtendFrameIntoClientArea</c> it draws nothing at all,
    /// which is what made an earlier reading of it look like a dead end. With the frame extended it does
    /// draw, and draws each material distinctly — Mica <c>#202020</c>, Acrylic <c>#545454</c>, Mica Alt
    /// <c>#202020</c> — but every one of them is a flat fallback colour rather than a material. Held over
    /// four wide bands of red, green, blue and white it picks up none of them, and it is the same value
    /// active as inactive, with transparency effects on and on mains power. It is not sampling the
    /// wallpaper either, which on this machine is deep blue.
    /// </para>
    /// <para>
    /// So every route the system offers has been tried and none of them blurs. What is left is to capture
    /// what lies behind the window and blur it in the tool's own tree, which is a different thing to
    /// build: it costs a capture per move, per resize and per frame behind, and it is the one thing the
    /// original never did. Real acrylic without that means the Windows App SDK compositor, whose
    /// <c>DesktopWindowTarget</c> is absent from the projected metadata at both 1.8 and 2.4 and so would
    /// have to be reached through hand-written COM.
    /// </para>
    /// </remarks>
    /// <param name="window">The window.</param>
    public static void Ground(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Explicitly none. The window inherits nothing, but saying so is what keeps the corrected
        // constants honest - and asking for a material here does not work, for the reasons above.
        WindowEffects.SetBackdrop(new WindowInteropHelper(window).Handle, WindowEffects.BackdropNone);
    }

    /// <summary>
    /// Keeps the corners in step with the window's state.
    /// </summary>
    /// <remarks>
    /// A maximized window's edges are the screen's, and rounding them cuts the corners off the content
    /// with no frame to show for it.
    /// </remarks>
    /// <param name="window">The window.</param>
    public static void FollowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowEffects.RoundCorners(
            new WindowInteropHelper(window).Handle,
            rounded: window.WindowState == WindowState.Normal);
    }

    /// <summary>
    /// Puts the window back where it was, if that is still somewhere reachable.
    /// </summary>
    /// <remarks>
    /// Checked against the virtual screen rather than trusted: a window restored onto a monitor that
    /// has since been unplugged is a window nobody can find, and it reads as the application failing
    /// to start rather than as a misplaced window.
    /// </remarks>
    /// <param name="window">The window.</param>
    /// <param name="placement">Where it was, or null on a first run.</param>
    public static void Restore(Window window, WindowPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (placement is null || placement.Width < window.MinWidth || placement.Height < window.MinHeight)
            return;

        if (!IsOnScreen(placement))
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = placement.Width;
        window.Height = placement.Height;

        if (placement.IsMaximized)
            window.WindowState = WindowState.Maximized;
    }

    /// <summary>Where the window is now, for putting it back next time.</summary>
    public static WindowPlacement Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new WindowPlacement
        {
            // RestoreBounds rather than Left/Top/Width/Height: while maximised those report the maximised
            // frame, so saving them would lose the size the window had before it was maximised.
            Left = window.RestoreBounds.Left,
            Top = window.RestoreBounds.Top,
            Width = window.RestoreBounds.Width,
            Height = window.RestoreBounds.Height,
            IsMaximized = window.WindowState == WindowState.Maximized
        };
    }

    private static bool IsOnScreen(WindowPlacement placement)
    {
        // Enough of the title bar has to land inside the virtual desktop to be grabbable.
        const double MinimumVisible = 120;

        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double right = left + SystemParameters.VirtualScreenWidth;
        double bottom = top + SystemParameters.VirtualScreenHeight;

        return placement.Left + MinimumVisible < right
            && placement.Left + placement.Width - MinimumVisible > left
            && placement.Top + 1 < bottom
            && placement.Top + placement.Height - 1 > top;
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x0024:
                WmGetMinMaxInfo(hwnd, lParam);
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Tells a maximising window how big it is allowed to get.
    /// </summary>
    /// <remarks>
    /// Without this a borderless window maximises over the taskbar, because the frame that would
    /// otherwise have accounted for it is the thing this window does not have.
    /// </remarks>
    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        GetCursorPos(out POINT lMousePosition);

        IntPtr lPrimaryScreen = MonitorFromPoint(new POINT(0, 0), MonitorOptions.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO lPrimaryScreenInfo = new();
        if (GetMonitorInfo(lPrimaryScreen, lPrimaryScreenInfo) == false)
            return;

        IntPtr lCurrentScreen = MonitorFromPoint(lMousePosition, MonitorOptions.MONITOR_DEFAULTTONEAREST);

        MINMAXINFO lMmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

        if (lPrimaryScreen.Equals(lCurrentScreen))
        {
            lMmi.ptMaxPosition.X = lPrimaryScreenInfo.rcWork.Left;
            lMmi.ptMaxPosition.Y = lPrimaryScreenInfo.rcWork.Top;
            lMmi.ptMaxSize.X = lPrimaryScreenInfo.rcWork.Right - lPrimaryScreenInfo.rcWork.Left;
            lMmi.ptMaxSize.Y = lPrimaryScreenInfo.rcWork.Bottom - lPrimaryScreenInfo.rcWork.Top;
        }
        else
        {
            lMmi.ptMaxPosition.X = lPrimaryScreenInfo.rcMonitor.Left;
            lMmi.ptMaxPosition.Y = lPrimaryScreenInfo.rcMonitor.Top;
            lMmi.ptMaxSize.X = lPrimaryScreenInfo.rcMonitor.Right - lPrimaryScreenInfo.rcMonitor.Left;
            lMmi.ptMaxSize.Y = lPrimaryScreenInfo.rcMonitor.Bottom - lPrimaryScreenInfo.rcMonitor.Top;
        }

        Marshal.StructureToPtr(lMmi, lParam, true);
    }
}
