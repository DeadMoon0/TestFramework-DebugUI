using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Starts a test again, and says what happened.
/// </summary>
/// <remarks>
/// <para>
/// The run that results is not tracked here. It announces itself over the pipe like any other run,
/// so it arrives in the list on its own — which is the point: press the button, watch the run come
/// back. Watching the process from this side would be a second, worse source of the same truth.
/// </para>
/// <para>
/// What this does own is the part the pipe cannot report: that a test was asked for, and that asking
/// failed. A re-run that never starts produces no run and no signal, so without a word here it looks
/// exactly like a button that does nothing.
/// </para>
/// </remarks>
public sealed class TestRerunner(Action<FeedEntry> report)
{
    private readonly HashSet<string> running = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Starts a run of the test that produced this one.</summary>
    /// <returns>Whether a test process was started.</returns>
    public async Task<bool> RerunAsync(RunSummary? run)
    {
        if (run is null)
            return false;

        if (!RerunCommand.TryFor(run, out RerunCommand? command, out string reason))
        {
            Say(FeedSeverity.Warning, $"Cannot repeat {run.Test}", reason);
            return false;
        }

        lock (gate)
        {
            // One at a time per test. A test that takes a minute invites a second press, and the two
            // runs would then race for the same build output and fail in a way that looks like a
            // defect in the test.
            if (!running.Add(run.Test))
            {
                Say(FeedSeverity.Info, $"{run.Test} is already being repeated", null);
                return false;
            }
        }

        try
        {
            return await StartAsync(run, command!).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                running.Remove(run.Test);
            }
        }
    }

    private async Task<bool> StartAsync(RunSummary run, RerunCommand command)
    {
        Say(FeedSeverity.Info, $"Repeating {run.Test}", command.Display);

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(command.FileName, command.Arguments)
            {
                WorkingDirectory = command.WorkingDirectory,

                // No window, and no redirection: the run reports itself over the pipe, and reading
                // two pipes to repeat what the board already shows would only add a way to deadlock.
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                Say(FeedSeverity.Error, $"Could not repeat {run.Test}", "The test process did not start.");
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);

            // A non-zero exit is an ordinary failing test, not a fault in the re-run — the board will
            // show why. Only say something when nothing ran at all.
            if (process.ExitCode != 0 && !run.CanRerun)
                Say(FeedSeverity.Warning, $"{run.Test} exited with {process.ExitCode}", command.Display);

            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            Say(FeedSeverity.Error, $"Could not repeat {run.Test}", e.Message);
            return false;
        }
    }

    private void Say(FeedSeverity severity, string title, string? detail, string? sessionId = null) => report(new FeedEntry
    {
        AtUtc = DateTimeOffset.UtcNow,
        Severity = severity,
        Source = FeedSource.Rerun,
        Title = title,
        Detail = detail,
        SessionId = sessionId
    });
}
