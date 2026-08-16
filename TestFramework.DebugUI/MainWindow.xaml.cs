using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;
using static TestFramework.DebugUI.NativeMethods;

namespace TestFramework.DebugUI;

/// <summary>
/// The application window.
/// </summary>
/// <remarks>
/// The store is built here, before anything is loaded, and reached everywhere else as
/// <c>StateStore&lt;MainState&gt;.Default</c>. Views bind to it in their own constructors; there is
/// no DataContext and no view model, which is the convention the rest of these applications follow.
/// </remarks>
public partial class MainWindow : Window
{
    private static ShellController? shell;

    /// <summary>
    /// Gets the controller the views drive.
    /// </summary>
    /// <remarks>
    /// Static for the same reason the store is: the window is a singleton, and threading it through
    /// every control's constructor buys nothing when there is only ever one.
    /// </remarks>
    public static ShellController Shell => shell ?? throw new InvalidOperationException("The window has not been created yet.");

    /// <summary>Creates the window and the store behind it.</summary>
    public MainWindow()
    {
        StateStore<MainState>.Create()
            .AddReducer(new MainReducer())
            .UseSynchronizationContext(SynchronizationContext.Current!)
            .BuildAndMakeDefault();

        shell = new ShellController(StateStore<MainState>.Default)
        {
            // Every step asks before it runs, so this is consulted constantly. It answers from the
            // breakpoints the user has set, and with none set nothing is ever held.
            PauseAtBreakpoint = Breakpoints.ShouldPause
        };

        // A crash must not take the window down. This tool exists to show things going wrong, and
        // the most likely moment for it to be handed something malformed is exactly when the run it
        // is watching is misbehaving.
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;

        InitializeComponent();

        // The summary is a foreground question, so the board asks for it and the window decides
        // where it goes — the board has no business knowing what else is on screen.
        ucBoard.SummaryRequested += () => ucSummary.Visibility = Visibility.Visible;
        ucSummary.Closed += () => ucSummary.Visibility = Visibility.Collapsed;

        ucValues.ValueOpened += (key, isArtifact) => ucValueInspector.Show(key, isArtifact);
        ucValueInspector.Closed += () => ucValueInspector.Visibility = Visibility.Collapsed;

        // The home page is shown on purpose and hidden on purpose. It is deliberately not tied to
        // whether a run is selected: the first live run selects itself, and having the page vanish
        // under the reader because a test started elsewhere would be the tool moving on its own.
        ucHome.Closed += () => ucHome.Visibility = Visibility.Collapsed;

        shell.Start();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        shell?.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = FeedSeverity.Error,
            Source = FeedSource.App,
            Title = "Something in the window failed.",
            Detail = e.Exception.Message
        });
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        IntPtr mWindowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(mWindowHandle).AddHook(new HwndSourceHook(WindowProc));

        DWMWINDOWATTRIBUTE attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
        DWM_WINDOW_CORNER_PREFERENCE preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));

        ArcylicManager.EnableBlur(mWindowHandle, System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
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

    private void Window_StateChanged(object sender, EventArgs e)
    {
        IntPtr mWindowHandle = new WindowInteropHelper(this).Handle;

        DWMWINDOWATTRIBUTE attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
        DWM_WINDOW_CORNER_PREFERENCE preference = WindowState == WindowState.Normal
            ? DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND
            : DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;

        DwmSetWindowAttribute(mWindowHandle, attribute, ref preference, sizeof(uint));
    }

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

    private void btNavRuns_Click(object sender, RoutedEventArgs e) => ucHome.Visibility = Visibility.Visible;

    private void btClose_Click(object sender, RoutedEventArgs e) => Close();

    private void btMaximizer_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;

    private void btMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Application.Current.DispatcherUnhandledException -= OnDispatcherUnhandledException;

        shell?.Dispose();
        shell = null;

        StateStore<MainState>.Default?.Dispose();
    }
}
