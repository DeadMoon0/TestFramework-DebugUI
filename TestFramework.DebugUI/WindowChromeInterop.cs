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
    /// What the window lies on: a compositor blur when the theme lets anything through, nothing when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is layered, and that alone is what lets the desktop through: what it does not paint, it
    /// does not cover. The blur is then asked for rather than drawn — the compositor draws it behind the
    /// window, and it applies the tint to it, which is why the tint is handed over here instead of being
    /// painted on top. Painting it as well would darken the window twice.
    /// </para>
    /// <para>
    /// A theme that covers the desktop is told to stop: an opaque tint or a backdrop that paints wall to
    /// wall leaves nothing for a blur to show, and asking for one anyway is a compositor effect running
    /// for a result nobody can see.
    /// </para>
    /// <para>
    /// <b>Energy saver turns all of this off, silently.</b> Every compositor effect becomes a flat fill —
    /// this blur a black sheet, Mica and Acrylic flat fallback greys that pick up neither the windows
    /// behind nor the wallpaper — and no call reports a failure. Measured either side of the setting on
    /// build 26200, holding the window over four wide bands of red, green, blue and white and reading
    /// across the middle: with it on, a flat <c>0</c> everywhere; with it off, <c>#234023</c> to
    /// <c>#1E3132</c> to <c>#18186A</c>, the blend at the boundary being the blur doing its work. It is
    /// indistinguishable from the API having been withdrawn, and it is the first thing to rule out.
    /// </para>
    /// </remarks>
    /// <param name="window">The window.</param>
    /// <param name="tint">
    /// The colour to lay over the blur, or null for a theme that paints its own ground and wants none.
    /// </param>
    public static void Ground(Window window, Color? tint)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr handle = new WindowInteropHelper(window).Handle;

        // Explicitly none, so the corrected constants stay honest: the blur comes from the accent policy
        // below, and a system material drawn as well would sit between the two.
        WindowEffects.SetBackdrop(handle, WindowEffects.BackdropNone);

        if (tint is Color frost)
            WindowEffects.Frost(handle, frost);
        else
            WindowEffects.Unfrost(handle);
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
