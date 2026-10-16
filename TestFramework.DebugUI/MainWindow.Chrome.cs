using System;
using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.State.Theming;

namespace TestFramework.DebugUI;

// The window itself: its ground and corners, its caption buttons, and being brought to the front.
public partial class MainWindow
{
    /// <summary>
    /// Hooks the window once it has a handle.
    /// </summary>
    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        WindowChromeInterop.Attach(this);

        // Again, because the theme was chosen in the constructor and there was no handle to ground then.
        Ground(theme.Current);
    }

    /// <summary>
    /// Lays the window on its ground: a compositor blur if the theme shows anything behind it, its own
    /// flat tint if it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tint goes to one place or the other, never both. When the compositor is blurring it applies
    /// the tint to the blur, so <c>bTint</c> has to paint nothing or the window is tinted twice; when
    /// there is no blur there is nothing to hand it to, and <c>bTint</c> is the ground itself.
    /// </para>
    /// <para>
    /// Called from the theme change <em>and</em> from <c>SourceInitialized</c>, because neither happens
    /// reliably after the other: the first theme is chosen in the constructor, before there is a handle
    /// to ground, and every theme after it arrives long after the window has one.
    /// </para>
    /// </remarks>
    private void Ground(ThemeDefinition definition)
    {
        bool frosted = definition.ShowsWhatIsBehind;

        bTint.Background = frosted ? null : new SolidColorBrush(theme.WindowTint);
        WindowChromeInterop.Ground(this, frosted ? theme.WindowTint : null);
    }

    /// <summary>Keeps the window's corners in step with its state.</summary>
    private void Window_StateChanged(object sender, EventArgs e) => WindowChromeInterop.FollowState(this);

    public void OpenFromAnotherLaunch(string? bundlePath)
    {
        Surface();

        if (bundlePath is null)
            return;

        if (BundleImport.Open(bundlePath, Shell))
            Put(PanelId.Home);
    }

    /// <summary>
    /// Brings the window out of wherever it was put.
    /// </summary>
    /// <remarks>
    /// The topmost nudge is deliberate. A process cannot take the foreground from another one, so raising a
    /// window on request means briefly asking to be above everything and then giving that up again - which is
    /// what every application that can be summoned by a second launch ends up doing.
    /// </remarks>
    private void Surface()
    {
        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = saved.Window?.IsMaximized == true ? WindowState.Maximized : WindowState.Normal;

        Activate();

        Topmost = true;
        Topmost = false;

        watch.MarkShown();
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Close();

    private void btMaximizer_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;

    /// <summary>Minimises. Only reachable when watch mode is off, which is when the button exists.</summary>
    private void btMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
}
