using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestFramework.DebugUI.Editors;

/// <summary>
/// The icon inside a program, read out of the program.
/// </summary>
/// <remarks>
/// <para>
/// These icons belong to Microsoft and are not ours to redistribute, so none is shipped. Each is read from
/// the executable that was found on this machine, which is both the correct picture and the honest one: the
/// button shows the icon of the program it is actually going to start.
/// </para>
/// <para>
/// <see cref="Imaging.CreateBitmapSourceFromHIcon"/> rather than anything in System.Drawing, so this needs
/// no extra package — it is in the same assembly as the rest of the drawing here.
/// </para>
/// </remarks>
internal static class EditorIcon
{
    /// <summary>
    /// The program's icon, or null if it has none that can be read.
    /// </summary>
    /// <remarks>
    /// Frozen before it is returned. It is created off the UI thread while the editors are being found, and
    /// an unfrozen bitmap belongs to the thread that made it.
    /// </remarks>
    internal static ImageSource? From(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        IntPtr[] large = new IntPtr[1];
        IntPtr[] small = new IntPtr[1];

        try
        {
            // The large icon: these are drawn at 18 pixels, and shrinking the 32-pixel image is far kinder
            // than enlarging the 16-pixel one.
            if (ExtractIconEx(executablePath, 0, large, small, 1) == 0)
                return null;

            IntPtr handle = large[0] != IntPtr.Zero ? large[0] : small[0];

            if (handle == IntPtr.Zero)
                return null;

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();

            return source;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
        {
            // A button with no picture is a small problem. Failing to open the window is not.
            Debug.WriteLine(e);
            return null;
        }
        finally
        {
            // Both handles, including the one that was not used: ExtractIconEx hands over ownership of
            // everything it created.
            Release(large);
            Release(small);
        }
    }

    private static void Release(IntPtr[] icons)
    {
        foreach (IntPtr icon in icons)
        {
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[] largeIcons, IntPtr[] smallIcons, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
