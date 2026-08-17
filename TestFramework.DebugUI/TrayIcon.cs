using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TestFramework.DebugUI;

/// <summary>
/// A notification-area icon, and the notifications it raises.
/// </summary>
/// <remarks>
/// <para>
/// Written against <c>Shell_NotifyIcon</c> directly rather than pulling in WinForms or a tray package.
/// This application already does its own interop for the acrylic backdrop and the maximise behaviour,
/// so the style is established; and a tray icon is a handful of calls, which is a poor trade for a
/// framework reference in every published build.
/// </para>
/// <para>
/// The icon owns a hidden message-only window of its own rather than borrowing the main window's
/// handle. Tray callbacks arrive as a private window message, and hanging that off the visible window
/// would mean the icon stopped working the moment that window's handle was recreated — which is
/// exactly what happens when a WPF window with <c>AllowsTransparency</c> changes state.
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    /// <summary>The special parent that makes a window message-only, with no presence on screen.</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    private const int WM_TRAYCALLBACK = 0x0400 + 1024;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    /// <summary>
    /// Sent when the user clicks the notification itself rather than the icon.
    /// </summary>
    /// <remarks>
    /// The notification is the affordance that matters in watch mode - it is what appears when a run
    /// finishes, and it is what the user reaches for. Handling only the icon meant clicking the thing
    /// the tool had just put on screen did nothing at all.
    /// </remarks>
    private const int NIN_BALLOONUSERCLICK = 0x0405;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;
    private const uint NIIF_ERROR = 0x00000003;

    private readonly HwndSource messageWindow;
    private readonly IntPtr icon;

    private bool added;
    private bool disposed;

    /// <summary>Creates the icon, showing it immediately.</summary>
    /// <param name="tooltip">What hovering over it says.</param>
    public TrayIcon(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        // A genuine message-only window, parented to HWND_MESSAGE. Left top-level it is a zero-sized
        // ordinary window, and Windows gives those a taskbar button - so the application still appeared
        // on the taskbar while supposedly hidden in the tray, which defeats the point of hiding it.
        messageWindow = new HwndSource(new HwndSourceParameters("TestFrameworkDebuggerTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage
        });

        messageWindow.AddHook(OnMessage);

        icon = LoadApplicationIcon();
        added = Notify(NIM_ADD, Describe(tooltip));
    }

    /// <summary>Raised when the icon is clicked with the left button.</summary>
    public event Action? Activated;

    /// <summary>Raised when the icon is clicked with the right button.</summary>
    public event Action? ContextRequested;

    /// <summary>How urgent a notification is, which decides its glyph.</summary>
    public enum Level
    {
        Information,
        Warning
    }

    /// <summary>Changes the hover text, so it can report what is being watched.</summary>
    public void SetTooltip(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        if (disposed || !added)
            return;

        Notify(NIM_MODIFY, Describe(tooltip));
    }

    /// <summary>
    /// Raises a notification from the icon.
    /// </summary>
    /// <remarks>
    /// Best-effort by design: notifications can be turned off system-wide, suppressed by focus assist,
    /// or dropped while the shell is restarting. None of that is worth surfacing as an error — the run
    /// itself is still there in the window.
    /// </remarks>
    public void Notify(string title, string message, Level level)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);

        if (disposed || !added)
            return;

        NOTIFYICONDATA data = Describe(string.Empty);
        data.uFlags = NIF_INFO;
        data.szInfoTitle = Trim(title, 63);
        data.szInfo = Trim(message, 255);
        data.dwInfoFlags = level == Level.Warning ? NIIF_WARNING : NIIF_INFO;

        Notify(NIM_MODIFY, data);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (added)
        {
            Notify(NIM_DELETE, Describe(string.Empty));
            added = false;
        }

        messageWindow.RemoveHook(OnMessage);
        messageWindow.Dispose();

        if (icon != IntPtr.Zero)
            DestroyIcon(icon);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_TRAYCALLBACK)
            return IntPtr.Zero;

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
            case NIN_BALLOONUSERCLICK:
                Activated?.Invoke();
                handled = true;
                break;

            case WM_RBUTTONUP:
                ContextRequested?.Invoke();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private NOTIFYICONDATA Describe(string tooltip)
    {
        NOTIFYICONDATA data = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = messageWindow.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYCALLBACK,
            hIcon = icon,
            szTip = Trim(tooltip, 127),
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        return data;
    }

    private bool Notify(uint message, NOTIFYICONDATA data)
    {
        try
        {
            return Shell_NotifyIcon(message, ref data);
        }
        catch (Exception e)
        {
            // The shell can be mid-restart, in which case the call fails and the icon simply is not
            // there. Nothing about that should reach the user as an error.
            Debug.WriteLine(e);
            return false;
        }
    }

    /// <summary>
    /// The application's own icon, so the tray shows what the taskbar shows.
    /// </summary>
    /// <remarks>
    /// Loaded from the executable rather than from a resource: the icon is already embedded there by
    /// <c>ApplicationIcon</c>, and reading it back avoids shipping the same image twice.
    /// </remarks>
    private static IntPtr LoadApplicationIcon()
    {
        try
        {
            string? executable = Process.GetCurrentProcess().MainModule?.FileName;

            if (string.IsNullOrEmpty(executable))
                return IntPtr.Zero;

            return ExtractIcon(IntPtr.Zero, executable, 0);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return IntPtr.Zero;
        }
    }

    /// <summary>Cuts a string to what the struct's fixed buffer holds.</summary>
    /// <remarks>
    /// Silently, and deliberately. These are fixed-size character arrays in the native struct; a
    /// longer string does not fail, it corrupts the call.
    /// </remarks>
    private static string Trim(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum];

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
    }
}
