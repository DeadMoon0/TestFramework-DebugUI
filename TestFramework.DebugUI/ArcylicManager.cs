using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using static TestFramework.DebugUI.BlurManager;
using static TestFramework.DebugUI.NativeMethods;

namespace TestFramework.DebugUI
{
	public static class ArcylicManager
	{
        public enum DwmSystemBackdropType
        {
            DWMSBT_DISABLE = 0,
            DWMSBT_MAINWINDOW = 1,
            DWMSBT_TRANSIENTWINDOW = 2,
            DWMSBT_TABBEDWINDOW = 3,
        }

        public enum DwmWindowAttribute
        {
            DWMWA_SYSTEMBACKDROP_TYPE = 38, // This is the attribute for system backdrop type
        }

        [DllImport("dwmapi.dll", PreserveSig = false)]
        public static extern void DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attr, ref int attrValue, uint attrSize);


        [DllImport("user32.dll", EntryPoint = "SetWindowCompositionAttribute")]
		private static extern int _SetWindowCompositionAttribute(IntPtr hWnd, ref WindowCompositionAttributeData data);

		[StructLayout(LayoutKind.Sequential)]
		public struct AccentPolicy
		{
			public AccentState AccentState;
			public AccentFlags AccentFlags;
			public uint GradientColor;
			public int AnimationId;
		}

		[Flags]
		public enum AccentFlags
		{
			None = 0,
			DrawLeftBorder = 0x20,
			DrawTopBorder = 0x40,
			DrawRightBorder = 0x80,
			DrawBottomBorder = 0x100,
			DrawAllBorders = (DrawLeftBorder | DrawTopBorder | DrawRightBorder | DrawBottomBorder)
		}

		public enum AccentState
		{
			ACCENT_DISABLED = 0,
			ACCENT_ENABLE_GRADIENT = 1,
			ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
			ACCENT_ENABLE_BLURBEHIND = 3,
			ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
			ACCENT_INVALID_STATE = 5
		}

		internal static void SetAccentPolicy(IntPtr hWnd, AccentState accentState, AccentFlags accentFlags, uint gradientColor)
		{

			var accent = new AccentPolicy
			{
				AccentState = accentState,
				AccentFlags = accentFlags,
				AnimationId = 0,
				GradientColor = gradientColor
			};

			var accentStructSize = Marshal.SizeOf(accent);
			var accentPtr = Marshal.AllocHGlobal(accentStructSize);
			Marshal.StructureToPtr(accent, accentPtr, false);

			var data = new WindowCompositionAttributeData
			{
				Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
				SizeOfData = accentStructSize,
				Data = accentPtr
			};

			_SetWindowCompositionAttribute(hWnd, ref data);

			Marshal.FreeHGlobal(accentPtr);
		}

		public static void EnableBlur(IntPtr hWnd, AccentState accentState, uint hex)
		{
			SetAccentPolicy(hWnd, accentState, AccentFlags.None, hex);
		}

		public static void EnableBlur(IntPtr hWnd, Color color)
		{
			EnableBlur(hWnd, AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, color.ToABGRhex());
			//SetSystemBackdropType(hWnd, DwmSystemBackdropType.DWMSBT_MAINWINDOW);
        }

		internal static uint ToABGRhex(this Color c)
		{
			return (uint)(((c.A << 0x18) | (c.B << 0x10) | (c.G << 8) | c.R) & 0xFFFFFFFF);
		}

        public static void SetSystemBackdropType(IntPtr hwnd, DwmSystemBackdropType backdropType)
        {
            int value = (int)backdropType; // Cast the enum to int
            DwmSetWindowAttribute(hwnd, DwmWindowAttribute.DWMWA_SYSTEMBACKDROP_TYPE, ref value, (uint)Marshal.SizeOf(typeof(int)));
        }
    }
}
