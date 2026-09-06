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
            _ = DwmSetWindowAttribute(handle, WindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // A version that does not know the attribute. Square corners it is.
        }
    }

    /// <summary>Which material the system draws behind the whole window. Windows 11 build 22621 and later.</summary>
    private const int SystemBackdropType = 38;

    /// <summary>Whether the system draws this window's chrome dark.</summary>
    private const int ImmersiveDarkMode = 20;

    /// <summary>
    /// The system-drawn materials, spelled as the Win32 header spells them.
    /// </summary>
    /// <remarks>
    /// The tool carried these once with every value one too low - <c>MAINWINDOW</c> was 1, which is
    /// actually <c>NONE</c>. Anyone who tried Mica with that table asked for no backdrop and watched
    /// nothing happen.
    /// </remarks>
    public const int BackdropAuto = 0;
    public const int BackdropNone = 1;
    public const int BackdropMica = 2;
    public const int BackdropAcrylic = 3;
    public const int BackdropMicaAlt = 4;

    /// <summary>Asks the Desktop Window Manager for a material, and says what it answered.</summary>
    public static int SetBackdrop(IntPtr handle, int kind)
    {
        if (handle == IntPtr.Zero)
            return -1;

        try
        {
            int value = kind;

            return DwmSetWindowAttribute(handle, SystemBackdropType, ref value, sizeof(int));
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Tells the system to draw this window's chrome dark, which Mica follows.</summary>
    public static void SetDarkMode(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero)
            return;

        try
        {
            int value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The window attribute that carries an accent policy.</summary>
    private const int AccentPolicyAttribute = 19;

    /// <summary>No accent: the window is composited plainly.</summary>
    private const int AccentDisabled = 0;

    /// <summary>Blur what is behind the window and tint it. What the tool has always asked for.</summary>
    private const int AccentAcrylicBlurBehind = 4;

    /// <summary>
    /// Asks the compositor to blur whatever is behind the window and tint the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This does not render a blur into the window. It asks the compositor to draw one behind it, which
    /// is why the window still has to be see-through for any of it to show, and why the tint is handed
    /// over here rather than painted: the compositor applies it to the blurred sample, so painting it
    /// again on top would darken the window twice.
    /// </para>
    /// <para>
    /// <b>This is silently disabled by Windows' energy saver</b>, which turns every compositor effect
    /// into a flat fill — a black sheet here, and flat fallback colours for Mica and Acrylic. It reads
    /// exactly like the API having been removed, and it is worth ruling out before concluding anything
    /// about a build of Windows.
    /// </para>
    /// </remarks>
    /// <param name="handle">The window.</param>
    /// <param name="tint">The colour laid over the blur. Its alpha is how much of the blur survives.</param>
    public static void Frost(IntPtr handle, Color tint)
        => Accent(handle, AccentAcrylicBlurBehind, Gradient(tint));

    /// <summary>The gradient is ABGR, which is the easiest thing here to get wrong and the hardest to see.</summary>
    private static uint Gradient(Color tint)
        => (uint)tint.A << 24 | (uint)tint.B << 16 | (uint)tint.G << 8 | tint.R;

    /// <summary>Takes the blur away, for a theme that paints its own ground.</summary>
    public static void Unfrost(IntPtr handle)
        => Accent(handle, AccentDisabled, 0);

    private static void Accent(IntPtr handle, int state, uint gradient)
    {
        if (handle == IntPtr.Zero)
            return;

        IntPtr policy = IntPtr.Zero;

        try
        {
            AccentPolicy accent = new() { AccentState = state, AccentFlags = 0, GradientColor = gradient, AnimationId = 0 };

            policy = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            Marshal.StructureToPtr(accent, policy, fDeleteOld: false);

            WindowCompositionAttributeData data = new()
            {
                Attribute = AccentPolicyAttribute,
                Data = policy,
                SizeOfData = Marshal.SizeOf(accent)
            };

            _ = SetWindowCompositionAttribute(handle, ref data);
        }
        catch (Exception)
        {
            // Undocumented, and so allowed to not be there. A window without a blur is still a window.
        }
        finally
        {
            if (policy != IntPtr.Zero)
                Marshal.FreeHGlobal(policy);
        }
    }

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

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, uint size);

}
