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

    /// <summary>How long a started version is given to come up before it is treated as broken.</summary>
    /// <remarks>
    /// An upper bound, not a wait: the usual answer arrives in a fraction of it, as soon as the
    /// application's message loop is pumping. It only runs to the end when something is wrong, which
    /// is the one time waiting is worth it.
    /// </remarks>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(8);

    private async Task RunAsync()
    {
        VersionStore store = new(paths);
        LaunchRecordStore records = new(paths);

        // First, and outside the try: if this is all the launcher ever manages to do, runs on this
        // machine still get recorded, and that is the half nobody can do for themselves.
        Say("Preparing…");
        store.EnsureJournalFolder();

        // Settled() before anything is decided: an attempt still written from last time is a version
        // that was started and never seen to survive, and this is where that becomes a quarantine.
        // Written straight back so the conclusion survives even if this start goes no further.
        LaunchRecord record = records.Read().Settled();
        records.Write(record);

        using HttpClient client = new();
        using GitHubReleaseSource releases = new("DeadMoon0", "TestFramework-DebugUI", client);

        Say("Checking for updates…");
        ReleaseInfo? latest = await releases.LatestAsync(CancellationToken.None).ConfigureAwait(true);

        LaunchDecision decision = LaunchPlanner.Decide(store.Installed(), latest, record, Pinned());

        Say(decision.Reason);

        if (decision.Action == LaunchAction.Update)
        {
            if (!await TryInstallAsync(store, decision.Release!, client).ConfigureAwait(true))
            {
                // The download failed, so fall back to whatever is already here. An update that did
                // not arrive is a worse reason to stop than no update at all.
                decision = LaunchPlanner.Decide(store.Installed(), latest: null, record, Pinned());
                Say(decision.Reason);
            }
        }

        if (decision.Action == LaunchAction.Stuck)
        {
            Stop(decision.Reason);
            return;
        }

        if (decision.Version is not null)
            store.Prune(decision.Version);

        if (await StartFirstSurvivingAsync(store, records, record).ConfigureAwait(true))
            Close();
    }

    /// <summary>
    /// Works down the versions until one of them stays running.
    /// </summary>
    /// <remarks>
    /// The point of the watching. Without it a broken update is discovered by the person, who clicks
    /// the icon, sees nothing happen, and has no way to know that holding shift would help. With it
    /// the second candidate is tried before they have finished wondering, and the build that failed
    /// is remembered so the next start does not begin by repeating it.
    /// </remarks>
    private async Task<bool> StartFirstSurvivingAsync(VersionStore store, LaunchRecordStore records, LaunchRecord record)
    {
        foreach (Version version in LaunchPlanner.Candidates(store.Installed(), record, Pinned()))
        {
            Say($"Starting {version}…");

            // Written before the start, not after. A version that takes the whole process down with
            // it leaves nothing to write afterwards, and that case is the one this exists for.
            records.Write(record.Attempting(version));

            using Process? started = Start(version);

            if (started is not null && await SurvivedAsync(started).ConfigureAwait(true))
            {
                records.Write(record.Survived(version));
                return true;
            }

            record = record.Quarantining(version);
            records.Write(record);

            Say($"{version} did not start");
        }

        Stop("No installed version will start");

        return false;
    }

    /// <summary>
    /// Whether a started version came up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Process.WaitForInputIdle(int)"/> returns as soon as the application's message loop
    /// is pumping, which is the nearest thing to "a window appeared" that does not involve hunting
    /// for one. It answers in a fraction of a second when things are fine.
    /// </para>
    /// <para>
    /// The two ways of being lenient are deliberate. A process still running but not yet idle is
    /// treated as fine, because punishing a cold start on a loaded machine would quarantine a build
    /// that works. And a process that exited cleanly is treated as fine too: someone opening the tool
    /// and closing it inside eight seconds is their own business, not a broken version.
    /// </para>
    /// </remarks>
    private static async Task<bool> SurvivedAsync(Process started)
    {
        bool ready;

        try
        {
            // Off the UI thread: it blocks, and the whole point is that the status text keeps moving.
            ready = await Task.Run(() => started.WaitForInputIdle((int)Grace.TotalMilliseconds)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ready = false;
        }

        if (ready)
            return true;

        try
        {
            return !started.HasExited || started.ExitCode == 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
    /// Starts one version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The launcher used to start a version and leave immediately, on the grounds that nothing it
    /// could do while watching would help. That was wrong in one case, and it is the case that
    /// matters: a version which does not come up at all. So it now stays for as long as
    /// <see cref="Grace"/> — in practice a fraction of a second — and goes once something is running.
    /// It still does not supervise beyond that; a second taskbar entry for the life of the session
    /// would be a nuisance and would buy nothing.
    /// </para>
    /// <para>
    /// Returns the process rather than a verdict, because whether it counts as started is
    /// <see cref="SurvivedAsync"/>'s question and not this one's. Null means it could not be launched
    /// at all, which the caller treats exactly like a version that died.
    /// </para>
    /// </remarks>
    private Process? Start(Version version)
    {
        string executable = paths.ExecutableFor(version);

        try
        {
            return Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = paths.FolderFor(version)
            });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
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
