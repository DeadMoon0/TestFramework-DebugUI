using System;
using System.Runtime.InteropServices;

namespace TestFramework.DebugUI
{
	public static class NativeMethods
	{
		[DllImport("shell32.dll", SetLastError = true)]
		static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

		public static string[] CommandLineToArgv(string args)
		{
			IntPtr argv = CommandLineToArgvW(args, out int argc);

			if (argv == IntPtr.Zero) throw new ArgumentException("Could not Pars args", nameof(args));

			string[] argvStrings = new string[argc];
			for (int i = 0; i < argc; i++)
			{
				IntPtr currentArg = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
				argvStrings[i] = Marshal.PtrToStringUni(currentArg);
			}
			return argvStrings;
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern int GetWindowThreadProcessId(IntPtr handle, out int processId);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetCursorPos(out POINT lpPoint);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr MonitorFromPoint(POINT pt, MonitorOptions dwFlags);

		public enum MonitorOptions : uint
		{
			MONITOR_DEFAULTTONULL = 0x00000000,
			MONITOR_DEFAULTTOPRIMARY = 0x00000001,
			MONITOR_DEFAULTTONEAREST = 0x00000002
		}

		[DllImport("user32.dll")]
		public static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;
			public int Y;

			public POINT(int x, int y)
			{
				this.X = x;
				this.Y = y;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct MINMAXINFO
		{
			public POINT ptReserved;
			public POINT ptMaxSize;
			public POINT ptMaxPosition;
			public POINT ptMinTrackSize;
			public POINT ptMaxTrackSize;
		};

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public class MONITORINFO
		{
			public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
			public RECT rcMonitor = new RECT();
			public RECT rcWork = new RECT();
			public int dwFlags = 0;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			public int Left, Top, Right, Bottom;

			public RECT(int left, int top, int right, int bottom)
			{
				this.Left = left;
				this.Top = top;
				this.Right = right;
				this.Bottom = bottom;
			}
		}

		/// <summary>
		/// Retrieves a handle to the top-level window whose class name and window name match 
		/// the specified strings.
		/// This function does not search child windows.
		/// This function does not perform a case-sensitive search.
		/// </summary>
		/// <param name="lpClassName">If lpClassName is null, it finds any window whose title matches
		/// the lpWindowName parameter.</param>
		/// <param name="lpWindowName">The window name (the window's title). If this parameter is null,
		/// all window names match.</param>
		/// <returns>If the function succeeds, the return value is a handle to the window 
		/// that has the specified class name and window name.</returns>
		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		/// <summary>
		/// Handle used to send the message to all windows
		/// </summary>
		public static IntPtr HWND_BROADCAST = new IntPtr(0xffff);

		/// <summary>
		/// An application sends the WM_COPYDATA message to pass data to another application.
		/// </summary>
		public static uint WM_COPYDATA = 0x004A;

		/// <summary>
		/// Contains data to be passed to another application by the WM_COPYDATA message.
		/// </summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct COPYDATASTRUCT
		{
			/// <summary>
			/// User defined data to be passed to the receiving application.
			/// </summary>
			public IntPtr dwData;

			/// <summary>
			/// The size, in bytes, of the data pointed to by the lpData member.
			/// </summary>
			public int cbData;

			/// <summary>
			/// The data to be passed to the receiving application. This member can be IntPtr.Zero.
			/// </summary>
			public IntPtr lpData;
		}

		/// <summary>
		/// Sends the specified message to a window or windows.
		/// </summary>
		/// <param name="hWnd">A handle to the window whose window procedure will receive the message.
		/// If this parameter is HWND_BROADCAST ((HWND)0xffff), the message is sent to all top-level
		/// windows in the system.</param>
		/// <param name="Msg">The message to be sent.</param>
		/// <param name="wParam">Additional message-specific information.</param>
		/// <param name="lParam">Additional message-specific information.</param>
		/// <returns>The return value specifies the result of the message processing; 
		/// it depends on the message sent.</returns>
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

		/// <summary>
		/// Values used in the struct CHANGEFILTERSTRUCT
		/// </summary>
		public enum MessageFilterInfo : uint
		{
			/// <summary>
			/// Certain messages whose value is smaller than WM_USER are required to pass 
			/// through the filter, regardless of the filter setting. 
			/// There will be no effect when you attempt to use this function to 
			/// allow or block such messages.
			/// </summary>
			None = 0,

			/// <summary>
			/// The message has already been allowed by this window's message filter, 
			/// and the function thus succeeded with no change to the window's message filter. 
			/// Applies to MSGFLT_ALLOW.
			/// </summary>
			AlreadyAllowed = 1,

			/// <summary>
			/// The message has already been blocked by this window's message filter, 
			/// and the function thus succeeded with no change to the window's message filter. 
			/// Applies to MSGFLT_DISALLOW.
			/// </summary>
			AlreadyDisAllowed = 2,

			/// <summary>
			/// The message is allowed at a scope higher than the window.
			/// Applies to MSGFLT_DISALLOW.
			/// </summary>
			AllowedHigher = 3
		}

		/// <summary>
		/// Values used by ChangeWindowMessageFilterEx
		/// </summary>
		public enum ChangeWindowMessageFilterExAction : uint
		{
			/// <summary>
			/// Resets the window message filter for hWnd to the default.
			/// Any message allowed globally or process-wide will get through,
			/// but any message not included in those two categories,
			/// and which comes from a lower privileged process, will be blocked.
			/// </summary>
			Reset = 0,

			/// <summary>
			/// Allows the message through the filter. 
			/// This enables the message to be received by hWnd, 
			/// regardless of the source of the message, 
			/// even it comes from a lower privileged process.
			/// </summary>
			Allow = 1,

			/// <summary>
			/// Blocks the message to be delivered to hWnd if it comes from
			/// a lower privileged process, unless the message is allowed process-wide 
			/// by using the ChangeWindowMessageFilter function or globally.
			/// </summary>
			DisAllow = 2
		}

		/// <summary>
		/// Contains extended result information obtained by calling 
		/// the ChangeWindowMessageFilterEx function.
		/// </summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct CHANGEFILTERSTRUCT
		{
			/// <summary>
			/// The size of the structure, in bytes. Must be set to sizeof(CHANGEFILTERSTRUCT), 
			/// otherwise the function fails with ERROR_INVALID_PARAMETER.
			/// </summary>
			public uint size;

			/// <summary>
			/// If the function succeeds, this field contains one of the following values, 
			/// <see cref="MessageFilterInfo"/>
			/// </summary>
			public MessageFilterInfo info;
		}

		/// <summary>
		/// Modifies the User Interface Privilege Isolation (UIPI) message filter for a specified window
		/// </summary>
		/// <param name="hWnd">
		/// A handle to the window whose UIPI message filter is to be modified.</param>
		/// <param name="msg">The message that the message filter allows through or blocks.</param>
		/// <param name="action">The action to be performed, and can take one of the following values
		/// <see cref="MessageFilterInfo"/></param>
		/// <param name="changeInfo">Optional pointer to a 
		/// <see cref="CHANGEFILTERSTRUCT"/> structure.</param>
		/// <returns>If the function succeeds, it returns TRUE; otherwise, it returns FALSE. 
		/// To get extended error information, call GetLastError.</returns>
		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg,
		ChangeWindowMessageFilterExAction action, ref CHANGEFILTERSTRUCT changeInfo);


		[DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(IntPtr hWnd);

		// The enum flag for DwmSetWindowAttribute's second parameter, which tells the function what attribute to set.
		// Copied from dwmapi.h
		public enum DWMWINDOWATTRIBUTE
		{
			DWMWA_WINDOW_CORNER_PREFERENCE = 33
		}

		// The DWM_WINDOW_CORNER_PREFERENCE enum for DwmSetWindowAttribute's third parameter, which tells the function
		// what value of the enum to set.
		// Copied from dwmapi.h
		public enum DWM_WINDOW_CORNER_PREFERENCE
		{
			DWMWCP_DEFAULT = 0,
			DWMWCP_DONOTROUND = 1,
			DWMWCP_ROUND = 2,
			DWMWCP_ROUNDSMALL = 3
		}

		// Import dwmapi.dll and define DwmSetWindowAttribute in C# corresponding to the native function.
		[DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
		public static extern void DwmSetWindowAttribute(IntPtr hwnd,
														 DWMWINDOWATTRIBUTE attribute,
														 ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute,
														 uint cbAttribute);
	}
}
