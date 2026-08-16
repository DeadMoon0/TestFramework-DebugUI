using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// The launcher: make the tool ready, then get out of the way.
/// </summary>
/// <remarks>
/// The order of the steps is the design. The journal folder is created before anything can fail,
/// because a run recorded is worth more than a version updated; the update check is bounded, because
/// it sits between a person and their tool; and every failure after the folder exists still ends in
/// the application starting, if there is one to start.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly LauncherPaths paths = new();

    /// <summary>Creates the window and begins.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) => await RunAsync().ConfigureAwait(true);
    }

    private async Task RunAsync()
    {
        VersionStore store = new(paths);

        // First, and outside the try: if this is all the launcher ever manages to do, runs on this
        // machine still get recorded, and that is the half nobody can do for themselves.
        Say("Preparing…");
        store.EnsureJournalFolder();

        using HttpClient client = new();
        using GitHubReleaseSource releases = new("DeadMoon0", "TestFramework-DebugUI", client);

        Say("Checking for updates…");
        ReleaseInfo? latest = await releases.LatestAsync(CancellationToken.None).ConfigureAwait(true);

        LaunchDecision decision = LaunchPlanner.Decide(store.Installed(), latest, Pinned());

        Say(decision.Reason);

        if (decision.Action == LaunchAction.Update)
        {
            if (!await TryInstallAsync(store, decision.Release!, client).ConfigureAwait(true))
            {
                // The download failed, so fall back to whatever is already here. An update that did
                // not arrive is a worse reason to stop than no update at all.
                decision = LaunchPlanner.Decide(store.Installed(), latest: null, Pinned());
                Say(decision.Reason);
            }
        }

        if (decision.Action == LaunchAction.Stuck || decision.Version is null)
        {
            Stop(decision.Reason);
            return;
        }

        store.Prune(decision.Version);

        if (!Start(decision.Version))
            return;

        Close();
    }

    private async Task<bool> TryInstallAsync(VersionStore store, ReleaseInfo release, HttpClient client)
    {
        try
        {
            Progress<double> progress = new(Report);

            pbProgress.IsIndeterminate = false;

            await store.InstallAsync(release, client, progress, CancellationToken.None).ConfigureAwait(true);

            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            pbProgress.IsIndeterminate = true;

            return false;
        }
    }

    /// <summary>
    /// Starts the application and leaves.
    /// </summary>
    /// <remarks>
    /// The launcher does not stay to supervise. Nothing it could do while watching would help, and a
    /// second window in the taskbar for the life of the session would be a nuisance.
    /// </remarks>
    private bool Start(Version version)
    {
        string executable = paths.ExecutableFor(version);

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = paths.FolderFor(version)
            })?.Dispose();

            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Stop($"Could not start {version}");

            return false;
        }
    }

    /// <summary>
    /// The version the person asked for, when they asked for one.
    /// </summary>
    /// <remarks>
    /// Held rather than clicked: the window is too small for a picker, and this is the rare path —
    /// the newest version broke and someone wants the one before it. Shift while starting, or
    /// <c>--version 0.4.1</c> for anyone scripting it.
    /// </remarks>
    private Version? Pinned()
    {
        string[] arguments = Environment.GetCommandLineArgs();

        int flag = Array.FindIndex(arguments, argument => string.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase));

        if (flag >= 0 && flag + 1 < arguments.Length && Version.TryParse(arguments[flag + 1], out Version? asked))
            return asked;

        if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            return null;

        // Shift means "not the newest one". The one before it is what a person reaching for shift
        // almost always wants, and choosing it needs no interface.
        return new VersionStore(paths).Installed().Skip(1).FirstOrDefault();
    }

    private void Report(double fraction) => pbProgress.Value = Math.Clamp(fraction, 0, 1) * 100;

    private void Say(string status) => tbStatus.Text = status;

    /// <summary>Shows why nothing started, and waits to be dismissed.</summary>
    /// <remarks>
    /// Deliberately not a message box and not a silent exit. A launcher that vanishes leaves someone
    /// clicking the icon again; one that says what is wrong lets them fix it.
    /// </remarks>
    private void Stop(string reason)
    {
        Say(reason);
        pbProgress.IsIndeterminate = false;
        pbProgress.Value = 0;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
