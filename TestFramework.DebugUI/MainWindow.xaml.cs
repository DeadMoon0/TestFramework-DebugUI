using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.DebugUI;
using TestFramework.DebugUI.State;
using WpfStateService.Dispatching;
using static TestFramework.DebugUI.NativeMethods;

namespace TestFrameworkDebugUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow Instance;

        public static MainState State = new MainState();

        public MainWindow()
        {
            StateCommonDispatcher.StateDispatcher = new StateDispatcher();

            Instance = this;
            InitializeComponent();

            new DebugPipeTranslator().Begin();

            //var stage = new DebugStageState
            //{
            //    Name = "Stage",
            //    Description = "Description",

            //    Steps =
            //    [
            //        new DebugStepState
            //        {
            //            Name = "Step1",
            //            Description = "Description1",
            //            DoesReturn = true,
            //            ErrorHandlingOptions = null!,
            //            ExecutionOptions = null!,
            //            IOContract = null!,
            //            LabelOptions = null!,
            //            RetryOptions = null!,
            //            TimeOutOptions = null!,
            //        },
            //        new DebugStepState
            //        {
            //            Name = "Step2",
            //            Description = "Description2",
            //            DoesReturn = true,
            //            ErrorHandlingOptions = null!,
            //            ExecutionOptions = null!,
            //            IOContract = null!,
            //            LabelOptions = null!,
            //            RetryOptions = null!,
            //            TimeOutOptions = null!,
            //        }
            //    ]
            //};

            //State.ActiveRun = new RunState
            //{
            //    Name = "Name",
            //    ProjectPath = "",
            //    Structure = new TestFrameworkCore.Debugger.TimelineRunStructure
            //    {
            //        Stages = [stage],
            //        Artifacts = new Dictionary<ArtifactIdentifier, TestFrameworkCore.Debugger.ArtifactState>(),
            //        Variables = new Dictionary<VariableIdentifier, VariableState>()
            //    }
            //};
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            IntPtr mWindowHandle = (new WindowInteropHelper(this)).Handle;
            HwndSource.FromHwnd(mWindowHandle).AddHook(new HwndSourceHook(WindowProc));

            var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
            var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));

            //BlurManager.EnableBlur(this);
            ArcylicManager.EnableBlur(mWindowHandle, System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
        }

        private static System.IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:
                    WmGetMinMaxInfo(hwnd, lParam);
                    break;
            }
            return IntPtr.Zero;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                IntPtr mWindowHandle = (new WindowInteropHelper(this)).Handle;

                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));

                //Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
            }
            else
            {
                IntPtr mWindowHandle = (new WindowInteropHelper(this)).Handle;

                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));

                //Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
            }
        }

        private static void WmGetMinMaxInfo(System.IntPtr hwnd, System.IntPtr lParam)
        {
            POINT lMousePosition;
            GetCursorPos(out lMousePosition);

            IntPtr lPrimaryScreen = MonitorFromPoint(new POINT(0, 0), MonitorOptions.MONITOR_DEFAULTTOPRIMARY);
            MONITORINFO lPrimaryScreenInfo = new MONITORINFO();
            if (GetMonitorInfo(lPrimaryScreen, lPrimaryScreenInfo) == false)
            {
                return;
            }

            IntPtr lCurrentScreen = MonitorFromPoint(lMousePosition, MonitorOptions.MONITOR_DEFAULTTONEAREST);

            MINMAXINFO lMmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            if (lPrimaryScreen.Equals(lCurrentScreen) == true)
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

        private void btClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btMaximizer_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void btMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //e.Cancel = true;
            //Hide();
        }
    }
}