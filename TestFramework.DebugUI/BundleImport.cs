using System;
using System.Collections.Generic;
using System.IO;
using TestFramework.DebugUI.State.Bundles;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI;

/// <summary>
/// Takes in a shared run, however the reader asked for it.
/// </summary>
/// <remarks>
/// One path for the file dialog and for a double-click. They arrive differently and mean the same thing, and a
/// second copy of this would be a second set of rules about duplicates, corrupt files and what to select.
/// </remarks>
internal static class BundleImport
{
    /// <summary>
    /// Opens a bundle, reports what happened, and selects a run from it.
    /// </summary>
    /// <returns>Whether anything new was taken in, which is what tells a caller to get out of the way.</returns>
    internal static bool Open(string bundlePath)
    {
        if (string.IsNullOrWhiteSpace(bundlePath))
            return false;

        if (MainWindow.Shell.RunsDirectory is not { } runsDirectory)
        {
            Report(FeedSeverity.Warning, "There is nowhere to put an imported run.", "No journal folder could be found on this machine.");
            return false;
        }

        try
        {
            RunBundleReader.Result result = RunBundleReader.Read(bundlePath, runsDirectory);

            MainWindow.Shell.RefreshRecordedRuns();

            if (result.FirstSessionId is { } sessionId)
                MainWindow.Shell.SelectRun(sessionId);

            Report(Severity(result), Describe(result), Concerns(result));

            return result.Imported.Count > 0;
        }
        catch (RunBundleReader.UnreadableException unreadable)
        {
            Report(FeedSeverity.Error, "That file could not be opened as a shared run.", unreadable.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report(FeedSeverity.Error, "That file could not be read.", exception.Message);
        }

        return false;
    }

    /// <summary>An import that lost something is not routine progress.</summary>
    private static FeedSeverity Severity(RunBundleReader.Result result)
        => result.Corrupt.Count > 0 || result.Missing.Count > 0 ? FeedSeverity.Warning : FeedSeverity.Info;

    /// <summary>What an import did, in one line.</summary>
    private static string Describe(RunBundleReader.Result result)
    {
        if (result.Imported.Count == 0 && result.AlreadyPresent.Count > 0)
            return result.AlreadyPresent.Count == 1
                ? "That run is already here."
                : "Those runs are already here.";

        if (result.Imported.Count == 0)
            return "That bundle held nothing this tool could read.";

        string subject = result.Imported.Count == 1 ? "1 run" : $"{result.Imported.Count} runs";

        return result.Manifest.IsAnonymous
            ? $"Opened {subject}, shared anonymously."
            : $"Opened {subject}.";
    }

    /// <summary>Anything about an import the reader should not have to discover later.</summary>
    private static string? Concerns(RunBundleReader.Result result)
    {
        List<string> parts = [];

        if (result.AlreadyPresent.Count > 0 && result.Imported.Count > 0)
            parts.Add($"{result.AlreadyPresent.Count} were already here.");

        if (result.Missing.Count > 0)
            parts.Add($"{result.Missing.Count} artifact file(s) were never sent.");

        if (result.Corrupt.Count > 0)
            parts.Add($"{result.Corrupt.Count} artifact file(s) did not arrive intact: {string.Join(", ", result.Corrupt)}.");

        if (result.Manifest.MachineName is { Length: > 0 } machine)
            parts.Add($"Recorded on {machine}.");

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static void Report(FeedSeverity severity, string title, string? detail)
        => MainWindow.Shell.Report(new FeedEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = severity,
            Source = FeedSource.App,
            Title = title,
            Detail = detail
        });
}
