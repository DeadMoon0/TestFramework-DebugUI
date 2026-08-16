using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// Replays a recorded run from its journal.
/// </summary>
/// <remarks>
/// This is what makes a run inspectable after the test host exited, and it is the only way to open a
/// run the UI was never connected for. Because journal lines are the same envelopes the pipe
/// carries, replaying one exercises the live path rather than a parallel one.
/// </remarks>
public sealed class JournalRunEventSource : IRunEventSource
{
    private readonly string journalPath;

    /// <summary>Creates a source that replays the journal at the given path.</summary>
    public JournalRunEventSource(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        this.journalPath = journalPath;
    }

    /// <inheritdoc />
    public event Action<DebugEnvelope>? EnvelopeReceived;

    /// <inheritdoc />
    public event Action<FeedEntry>? Notice;

    /// <inheritdoc />
    public void Start()
    {
        if (!File.Exists(journalPath))
        {
            // The sidecar listed it but the journal is gone — pruned by a later run, or deleted by
            // hand. Silently showing an empty board would read as a run that did nothing.
            Report(FeedSeverity.Error, "The recorded run could not be opened.", $"No journal at {journalPath}.");
            return;
        }

        int damagedLines = 0;

        foreach (string line in ReadLines(journalPath))
        {
            DebugEnvelope? envelope = TryParse(line);

            if (envelope is null)
            {
                damagedLines++;
                continue;
            }

            EnvelopeReceived?.Invoke(envelope);
        }

        if (damagedLines > 0)
        {
            // Said once, with a count, rather than per line: a version mismatch damages every line
            // in the file, and a feed entry for each would bury everything else.
            Report(
                FeedSeverity.Warning,
                "Part of the recorded run could not be read.",
                $"{damagedLines} event(s) were skipped. A run whose host was killed ends in a partial line; a whole file of them means it was recorded by a different protocol version.");
        }
    }

    /// <summary>
    /// Lists the runs recorded under a journal root, newest first.
    /// </summary>
    /// <remarks>
    /// Read from the metadata sidecars, never from the journals themselves: listing runs must stay
    /// cheap no matter how much a run logged, and a picker that had to parse every event to show a
    /// name would get slower the more there is to look at.
    /// </remarks>
    public static ImmutableList<AvailableRun> ListRuns(string runsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runsDirectory);

        if (!Directory.Exists(runsDirectory))
            return ImmutableList<AvailableRun>.Empty;

        ImmutableList<AvailableRun>.Builder runs = ImmutableList.CreateBuilder<AvailableRun>();

        // The stem is a sortable timestamp, so ordinal descending is newest first without reading
        // any file to find out.
        foreach (string metaPath in Directory.EnumerateFiles(runsDirectory, "*.meta.json")
                     .OrderByDescending(path => path, StringComparer.Ordinal))
        {
            AvailableRun? run = TryReadMetadata(runsDirectory, metaPath);
            if (run is not null)
                runs.Add(run);
        }

        return runs.ToImmutable();
    }

    private static AvailableRun? TryReadMetadata(string runsDirectory, string metaPath)
    {
        try
        {
            using StreamReader reader = DebugJournal.OpenForReading(metaPath);
            DebugRunMetadata? metadata = JsonConvert.DeserializeObject<DebugRunMetadata>(reader.ReadToEnd());

            if (metadata is null)
                return null;

            return new AvailableRun
            {
                SessionId = metadata.SessionId,
                Name = metadata.Name,
                StartedAtUtc = metadata.StartedAtUtc,

                // A sidecar still reading Running means the producer never closed it, which is how a
                // killed test host looks from here.
                IsFinished = metadata.Outcome == DebugRunOutcome.Finished,
                FinishedAtUtc = metadata.FinishedAtUtc,
                FullyQualifiedName = metadata.Identity?.FullyQualifiedName,
                // The test's own project or assembly first: under a test runner the announced path
                // is the host process, and every run would file under "testhost".
                ProjectPath = metadata.Identity?.ProjectFilePath
                              ?? metadata.Identity?.AssemblyName
                              ?? metadata.Identity?.AssemblyPath
                              ?? metadata.ProjectPath,
                JournalPath = Path.Combine(runsDirectory, metadata.JournalFileName)
            };
        }
        catch (Exception)
        {
            // One unreadable sidecar — half-written, or from a newer build — must not hide every
            // other run from the picker.
            return null;
        }
    }

    private void Report(FeedSeverity severity, string title, string detail)
    {
        try
        {
            Notice?.Invoke(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = severity,
                Source = FeedSource.Journal,
                Title = title,
                Detail = detail
            });
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine(e);
        }
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using StreamReader reader = DebugJournal.OpenForReading(path);

        while (reader.ReadLine() is string line)
        {
            if (line.Length > 0)
                yield return line;
        }
    }

    private static DebugEnvelope? TryParse(string line)
    {
        try
        {
            return DebugEnvelopeCodec.Deserialize(line);
        }
        catch (Exception)
        {
            // A journal's last line can be torn if the host died mid-write, and a line from another
            // protocol version is unreadable by design. Neither should discard the run's history up
            // to that point.
            return null;
        }
    }
}
