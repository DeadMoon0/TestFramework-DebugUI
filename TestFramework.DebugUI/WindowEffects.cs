using System;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace TestFramework.DebugUI;

/// <summary>
/// The chrome effects Windows draws for a window, asked for rather than drawn.
/// </summary>
/// <remarks>
/// <para>
/// A custom-chromed window keeps square corners and a flat backdrop unless it says otherwise, and a
/// rounded rectangle drawn inside a square frame is not the same thing — the system's own rounding
/// clips the frame and casts the shadow to match, which is what makes a window look like a window.
/// </para>
/// <para>
/// One place for both, because there were two: two declarations of <c>DwmSetWindowAttribute</c> with
/// two spellings of the same enums, and the window called both — so its corners were rounded twice on
/// every start. Interop declared twice is interop that can disagree about a struct size, and that is a
/// crash rather than a cosmetic difference.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> Every one of these is a version-dependent nicety: a build of Windows that
/// does not know an attribute simply ignores it, and a window with square corners is not worth failing
/// to open over.
/// </para>
/// </remarks>
internal static class WindowEffects
{
    /// <summary>Whether Windows rounds the window's corners, and how much.</summary>
    private const int WindowCornerPreference = 33;

    /// <summary>The full radius the system uses for an ordinary window.</summary>
    private const int Round = 2;

    /// <summary>Square, as a custom-chromed window is without being asked.</summary>
    private const int DoNotRound = 1;

    private const int AccentPolicyAttribute = 19;

    /// <summary>The acrylic blur the system draws behind a window.</summary>
    private const int AcrylicBlurBehind = 4;

    /// <summary>
    /// Asks Windows to round a window's corners the way it rounds its own.
    /// </summary>
    /// <param name="handle">The window.</param>
    /// <param name="rounded">
    /// Whether to round. A maximized window says no: its edges are the screen's, and rounding them
    /// cuts the corners off the content for no visible frame.
    /// </param>
    public static void RoundCorners(IntPtr handle, bool rounded = true)
    {
        if (handle == IntPtr.Zero)
            return;

        try
        {
            int preference = rounded ? Round : DoNotRound;
            DwmSetWindowAttribute(handle, WindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // A version that does not know the attribute. Square corners it is.
        }
    }

    /// <summary>
    /// Puts an acrylic blur behind a window, tinted the given colour.
    /// </summary>
    public static void EnableBlur(IntPtr handle, Color tint)
    {
        if (handle == IntPtr.Zero)
            return;

        IntPtr policy = IntPtr.Zero;

        try
        {
            AccentPolicy accent = new()
            {
                AccentState = AcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = ToAbgr(tint),
                AnimationId = 0
            };

            int size = Marshal.SizeOf(accent);

            policy = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, policy, fDeleteOld: false);

            WindowCompositionAttributeData data = new()
            {
                Attribute = AccentPolicyAttribute,
                SizeOfData = size,
                Data = policy
            };

            SetWindowCompositionAttribute(handle, ref data);
        }
        catch (Exception)
        {
            // As above: a window without a blurred backdrop is still a window.
        }
        finally
        {
            if (policy != IntPtr.Zero)
                Marshal.FreeHGlobal(policy);
        }
    }

    /// <summary>The colour as the composition attribute wants it, which is ABGR rather than ARGB.</summary>
    private static uint ToAbgr(Color colour)
        => (uint)((colour.A << 24) | (colour.B << 16) | (colour.G << 8) | colour.R);

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, uint size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
