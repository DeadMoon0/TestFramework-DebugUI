using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Media;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Diagnostics;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Settings;

/// <summary>
/// The settings panel.
/// </summary>
/// <remarks>
/// <para>
/// Holds no settings of its own and writes no files. It is told what the current values are, and it
/// reports what the user changed; the window owns the store. That keeps the one thing that touches disk
/// in one place, and it means this panel can be opened and closed without any question of whether it
/// saved. The two machine switches are the exception, because neither is a setting of this tool: the
/// file association lives in the registry and recording lives in Core's marker file, and each is read
/// back from where it lives rather than remembered here.
/// </para>
/// <para>
/// There is no OK or Cancel. Every change applies and is written as it is made, because there is
/// nothing a Cancel could sensibly undo — turning watching off has already removed the icon.
/// </para>
/// </remarks>
public partial class UC_Settings : UserControl
{
    private BreakpointService? breakpoints;

    private RecordingSwitch? recording;

    public UC_Settings()
    {
        InitializeComponent();

        tgWatch.Toggled += value => Raise(Current with { Enabled = value });
        tgNotify.Toggled += value => Raise(Current with { NotifyOnFinish = value });
        tgFailuresOnly.Toggled += value => Raise(Current with { NotifyOnlyOnFailure = value });

        tgAssociate.Toggled += OnAssociateToggled;

        tgRecording.Toggled += OnRecordingToggled;

        // Its own event rather than a member of the watch settings: this governs what a run does when it
        // breaks, which has nothing to do with where the window goes when it is put away.
        tgBreakOnFailure.Toggled += value => BreakOnFailureChanged?.Invoke(value);

        Unloaded += (_, _) =>
        {
            if (breakpoints is not null)
                breakpoints.Changed -= ShowBreakpointCount;
        };
    }

    /// <summary>
    /// Gives the panel the marks it counts and clears.
    /// </summary>
    public void UseBreakpoints(BreakpointService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (breakpoints is not null)
            breakpoints.Changed -= ShowBreakpointCount;

        breakpoints = service;
        breakpoints.Changed += ShowBreakpointCount;

        ShowBreakpointCount();
    }

    /// <summary>
    /// Gives the panel the switch it shows and flips, or null when this machine's journal root cannot be told.
    /// </summary>
    public void UseRecording(RecordingSwitch? value) => recording = value;

    /// <summary>Raised when the user changes something.</summary>
    public event Action<WatchSettings>? Changed;

    /// <summary>Raised after the user has flipped the recording switch, with what it now says.</summary>
    public event Action<RecordingState>? RecordingChanged;

    /// <summary>Raised when the panel should go away.</summary>
    public event Action? Closed;

    /// <summary>Raised when the user changes whether a failing step holds its run.</summary>
    public event Action<bool>? BreakOnFailureChanged;

    /// <summary>Raised when the user picks a theme, with its id.</summary>
    public event Action<string>? ThemeChosen;

    /// <summary>Raised when the user asks for the themes folder to be read again.</summary>
    public event Action? ThemesReloadRequested;

    /// <summary>
    /// Raised when the user asks to see the themes folder.
    /// </summary>
    /// <remarks>
    /// The window handles it rather than this panel opening the folder itself, because the folder has
    /// to have something in it first: an empty folder is not an explanation of a file format.
    /// </remarks>
    public event Action? ThemesFolderRequested;

    private WatchSettings Current { get; set; } = new();

    /// <summary>
    /// Fills the panel in from the settings as they stand.
    /// </summary>
    /// <remarks>
    /// Called every time the panel is shown rather than once, because the title bar's eye changes the
    /// same setting: a panel populated only on construction would open showing a stale switch.
    /// </remarks>
    public void Show(
        WatchSettings watch,
        string settingsPath,
        bool breakOnFailure,
        IReadOnlyList<ThemeDefinition> themes,
        string currentThemeId,
        string themesPath,
        BlurBlock block)
    {
        ArgumentNullException.ThrowIfNull(watch);

        Current = watch;

        ShowThemes(themes, currentThemeId, block);
        tbThemesPath.Text = themesPath;

        tgWatch.SetQuietly(watch.Enabled);
        tgNotify.SetQuietly(watch.NotifyOnFinish);
        tgFailuresOnly.SetQuietly(watch.NotifyOnlyOnFailure);
        tgBreakOnFailure.SetQuietly(breakOnFailure);

        ShowAssociation();
        ShowRecording();

        tbSettingsPath.Text = settingsPath;
        ShowRunsFolder();

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

    /// <summary>
    /// Shows whether runs are recorded, read off the marker rather than remembered, for the same reason as the
    /// association: something outside this window can change it.
    /// </summary>
    private void ShowRecording()
    {
        tgRecording.IsEnabled = recording is not null;
        tgRecording.SetQuietly(recording?.IsOn ?? false);

        tbRecordingNote.Visibility = recording is null ? Visibility.Visible : Visibility.Collapsed;
        tbRecordingNote.Text = recording is null
            ? "Where runs are recorded could not be told on this machine."
            : string.Empty;
    }

    /// <summary>
    /// Switches recording on or off, then shows what the marker actually says.
    /// </summary>
    /// <remarks>
    /// Put back to what is on disk afterwards, so a refusal - a locked profile, a read-only folder - leaves the
    /// switch telling the truth rather than showing a state that was never reached.
    /// </remarks>
    private void OnRecordingToggled(bool wanted)
    {
        if (recording is null)
            return;

        bool done;

        try
        {
            if (wanted)
                recording.TurnOn();
            else
                recording.TurnOff();

            done = true;
        }
        catch (Exception e)
        {
            Log.Write(e);
            done = false;
        }

        RecordingState now = recording.Read();

        tgRecording.SetQuietly(now.IsOn);

        tbRecordingNote.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        tbRecordingNote.Text = done
            ? string.Empty
            : $"The marker file could not be changed: {recording.Marker}";

        ShowRunsFolder();

        RecordingChanged?.Invoke(now);
    }

    /// <summary>
    /// Shows where recorded runs go, and offers to open the folder only when there is one.
    /// </summary>
    /// <remarks>
    /// Never creates it. The folder appears with the first recorded run, and opening it is not a reason for it to
    /// exist - an empty folder made by looking would read as recording having happened.
    /// </remarks>
    private void ShowRunsFolder()
    {
        string? runs = SafeRunsDirectory();
        bool exists = runs is not null && Directory.Exists(runs);

        tbRunsPath.Text = runs is null
            ? "Not available on this machine."
            : exists ? runs : $"{runs} (does not exist yet - it appears with the first recorded run)";

        btOpenRuns.IsEnabled = exists;
    }

    private void Raise(WatchSettings next)
    {
        Current = next;
        Changed?.Invoke(next);
    }

    private void ShowBreakpointCount()
    {
        int count = breakpoints?.Snapshot().Count ?? 0;

        tbBreakpoints.Text = count switch
        {
            0 => "None set",
            1 => "1 breakpoint set",
            _ => $"{count} breakpoints set"
        };

        btClearBreakpoints.IsEnabled = count > 0;
    }

    /// <summary>
    /// Lays out the themes on offer, with the current one marked.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than updated. A chip is a picture of a theme and the marked one is drawn
    /// differently from the rest, so "which is chosen" is not a property to toggle — and there are ten
    /// of them, once, when a panel opens.
    /// </remarks>
    /// <param name="themes">Everything on offer.</param>
    /// <param name="currentThemeId">The one that is on.</param>
    /// <param name="block">
    /// Why the compositor is not blurring, if it is not. A see-through theme cannot be delivered while
    /// it holds, so its chip is shown faded and says why rather than being hidden — a theme that
    /// disappears from the picker reads as a theme that was removed.
    /// </param>
    public void ShowThemes(IReadOnlyList<ThemeDefinition> themes, string currentThemeId, BlurBlock block = BlurBlock.None)
    {
        ArgumentNullException.ThrowIfNull(themes);

        wpThemes.Children.Clear();

        foreach (ThemeDefinition theme in themes)
        {
            bool chosen = string.Equals(theme.Id, currentThemeId, StringComparison.OrdinalIgnoreCase);

            Button chip = ThemeChip.Build(theme, chosen, this, block);
            string id = theme.Id;

            chip.Click += (_, _) => ThemeChosen?.Invoke(id);

            wpThemes.Children.Add(chip);
        }
    }

    private void btOpenThemes_Click(object sender, RoutedEventArgs e) => ThemesFolderRequested?.Invoke();

    private void btReloadThemes_Click(object sender, RoutedEventArgs e) => ThemesReloadRequested?.Invoke();

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
                Foreground = (Brush)FindResource(ThemeKeys.TextSecondary),
                FontSize = 11
            };

            Border key = new()
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource(ThemeKeys.SurfaceRaised),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = Shortcuts.GestureOf(command),
                    Foreground = (Brush)FindResource(ThemeKeys.TextPrimary),
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

    private void btClearBreakpoints_Click(object sender, RoutedEventArgs e) => breakpoints?.Clear();

    private void btOpenRuns_Click(object sender, RoutedEventArgs e)
    {
        string? runs = SafeRunsDirectory();

        if (runs is not null && Directory.Exists(runs))
            Open(runs);
        else
            ShowRunsFolder();
    }

    private void btOpenSettings_Click(object sender, RoutedEventArgs e) => Reveal(Path.GetDirectoryName(tbSettingsPath.Text));

    /// <summary>
    /// Opens a folder in Explorer.
    /// </summary>
    /// <remarks>
    /// Creates it first when it is missing. The settings folder does not exist until something has been
    /// saved, and "Open folder" doing nothing at all is the least helpful possible answer. Only for the
    /// tool's own folders: the runs folder is opened without this, because it sits in the journal root
    /// and is evidence of recording, which looking must not fake.
    /// </remarks>
    internal static void Reveal(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            // Not worth interrupting anyone over: the path is on screen and can be copied.
            Log.Write(e);
            return;
        }

        Open(folder);
    }

    /// <summary>Opens a folder that is already there in Explorer.</summary>
    private static void Open(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            // Not worth interrupting anyone over: the path is on screen and can be copied.
            Log.Write(e);
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
            Log.Write(e);
            return null;
        }
    }
}
