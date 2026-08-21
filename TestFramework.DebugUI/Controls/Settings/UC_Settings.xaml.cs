using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Settings;

namespace TestFramework.DebugUI.Controls.Settings;

/// <summary>
/// The settings panel.
/// </summary>
/// <remarks>
/// <para>
/// Holds no settings of its own and writes no files. It is told what the current values are, and it
/// reports what the user changed; the window owns the store. That keeps the one thing that touches disk
/// in one place, and it means this panel can be opened and closed without any question of whether it
/// saved.
/// </para>
/// <para>
/// There is no OK or Cancel. Every change applies and is written as it is made, because there is
/// nothing a Cancel could sensibly undo — turning watching off has already removed the icon.
/// </para>
/// </remarks>
public partial class UC_Settings : UserControl
{
    public UC_Settings()
    {
        InitializeComponent();

        tgWatch.Toggled += value => Raise(Current with { Enabled = value });
        tgNotify.Toggled += value => Raise(Current with { NotifyOnFinish = value });
        tgFailuresOnly.Toggled += value => Raise(Current with { NotifyOnlyOnFailure = value });

        tgAssociate.Toggled += OnAssociateToggled;

        // Its own event rather than a member of the watch settings: this governs what a run does when it
        // breaks, which has nothing to do with where the window goes when it is put away.
        tgBreakOnFailure.Toggled += value => BreakOnFailureChanged?.Invoke(value);

        Breakpoints.Changed += ShowBreakpointCount;
        Unloaded += (_, _) => Breakpoints.Changed -= ShowBreakpointCount;
    }

    /// <summary>Raised when the user changes something.</summary>
    public event Action<WatchSettings>? Changed;

    /// <summary>Raised when the panel should go away.</summary>
    public event Action? Closed;

    /// <summary>Raised when the user changes whether a failing step holds its run.</summary>
    public event Action<bool>? BreakOnFailureChanged;

    private WatchSettings Current { get; set; } = new();

    /// <summary>
    /// Fills the panel in from the settings as they stand.
    /// </summary>
    /// <remarks>
    /// Called every time the panel is shown rather than once, because the title bar's eye changes the
    /// same setting: a panel populated only on construction would open showing a stale switch.
    /// </remarks>
    public void Show(WatchSettings watch, string settingsPath, bool breakOnFailure)
    {
        ArgumentNullException.ThrowIfNull(watch);

        Current = watch;

        tgWatch.SetQuietly(watch.Enabled);
        tgNotify.SetQuietly(watch.NotifyOnFinish);
        tgFailuresOnly.SetQuietly(watch.NotifyOnlyOnFailure);
        tgBreakOnFailure.SetQuietly(breakOnFailure);

        ShowAssociation();

        tbSettingsPath.Text = settingsPath;
        tbRunsPath.Text = SafeRunsDirectory() ?? "Not available on this machine yet — it appears once the launcher has run.";

        ShowBreakpointCount();

        ShowShortcuts();
        Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows whether this tool opens shared runs.
    /// </summary>
    /// <remarks>
    /// Read from the registry rather than from a saved setting. The association lives there and can be changed
    /// from outside this application entirely, so a copy of the answer here would eventually be a wrong one.
    /// </remarks>
    private void ShowAssociation()
    {
        tgAssociate.SetQuietly(FileAssociation.IsRegistered());

        tbAssociateNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Claims or gives back the file type.
    /// </summary>
    /// <remarks>
    /// The switch is put back to what the registry actually says afterwards, so a refusal - a locked-down
    /// account can decline even its own classes key - leaves the panel telling the truth rather than showing an
    /// association that was never made.
    /// </remarks>
    private void OnAssociateToggled(bool wanted)
    {
        bool done = wanted
            ? Environment.ProcessPath is { Length: > 0 } path && FileAssociation.Register(path)
            : FileAssociation.Unregister();

        tgAssociate.SetQuietly(FileAssociation.IsRegistered());

        tbAssociateNote.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        tbAssociateNote.Text = done
            ? string.Empty
            : "Windows would not let this be changed for your account.";
    }

    private void Raise(WatchSettings next)
    {
        Current = next;
        Changed?.Invoke(next);
    }

    private void ShowBreakpointCount()
    {
        int count = Breakpoints.Snapshot().Count;

        tbBreakpoints.Text = count switch
        {
            0 => "None set",
            1 => "1 breakpoint set",
            _ => $"{count} breakpoints set"
        };

        btClearBreakpoints.IsEnabled = count > 0;
    }

    /// <summary>Lists the shortcuts, read off the commands the window binds.</summary>
    private void ShowShortcuts()
    {
        spShortcuts.Children.Clear();

        foreach (RoutedUICommand command in Shortcuts.All)
        {
            Grid row = new() { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock what = new()
            {
                Text = command.Text,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11
            };

            Border key = new()
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceRaised"),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = Shortcuts.GestureOf(command),
                    Foreground = (Brush)FindResource("TextPrimary"),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 10
                }
            };

            Grid.SetColumn(key, 1);
            row.Children.Add(what);
            row.Children.Add(key);
            spShortcuts.Children.Add(row);
        }
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    /// <summary>Clicking away from the panel dismisses it, which is what a dimmed backdrop implies.</summary>
    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Closed?.Invoke();

    /// <summary>
    /// Stops a click inside the panel reaching the scrim.
    /// </summary>
    /// <remarks>
    /// Without this, adjusting a setting closes the panel — the click bubbles from the row up to the
    /// backdrop, which reads as the panel rejecting the change it just accepted.
    /// </remarks>
    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void btClearBreakpoints_Click(object sender, RoutedEventArgs e) => Breakpoints.Clear();

    private void btOpenRuns_Click(object sender, RoutedEventArgs e) => Reveal(SafeRunsDirectory());

    private void btOpenSettings_Click(object sender, RoutedEventArgs e) => Reveal(Path.GetDirectoryName(tbSettingsPath.Text));

    /// <summary>
    /// Opens a folder in Explorer.
    /// </summary>
    /// <remarks>
    /// Creates it first when it is missing. The settings folder does not exist until something has been
    /// saved, and "Open folder" doing nothing at all is the least helpful possible answer.
    /// </remarks>
    private static void Reveal(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            // Not worth interrupting anyone over: the path is on screen and can be copied.
            Debug.WriteLine(e);
        }
    }

    private static string? SafeRunsDirectory()
    {
        try
        {
            return DebugJournal.RunsDirectory;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return null;
        }
    }
}
