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
    /// Reads a recorded run's events without raising anything.
    /// </summary>
    /// <remarks>
    /// For callers that want a run's contents rather than to replay it into the UI — comparing a run
    /// against an earlier one, for instance. Deliberately silent: a damaged line is skipped, and
    /// nothing reaches the feed, because this runs off the back of a selection the user did not
    /// explicitly make and a warning about a file they did not open would be noise. Returns empty when
    /// the journal is gone, which the caller reports in its own terms.
    /// </remarks>
    public static ImmutableList<DebugEnvelope> ReadEnvelopes(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);

        if (!File.Exists(journalPath))
            return ImmutableList<DebugEnvelope>.Empty;

        ImmutableList<DebugEnvelope>.Builder envelopes = ImmutableList.CreateBuilder<DebugEnvelope>();

        foreach (string line in ReadLines(journalPath))
        {
            DebugEnvelope? envelope = TryParse(line);

            if (envelope is not null)
                envelopes.Add(envelope);
        }

        return envelopes.ToImmutable();
    }

    /// <summary>
    /// Lists the runs recorded under a journal root, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the metadata sidecars, never from the journals themselves: listing runs must stay
    /// cheap no matter how much a run logged, and a picker that had to parse every event to show a
    /// name would get slower the more there is to look at.
    /// </para>
    /// <para>
    /// A recording made against a different protocol version is not listed. Its events would fail to decode
    /// one by one, which reads as a run that opens empty for no stated reason —
    /// <see cref="CountFromOtherBuilds"/> is how a caller says how many were left out.
    /// </para>
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

    /// <summary>
    /// How many recordings under this root were made against another protocol version.
    /// </summary>
    /// <remarks>
    /// Counted rather than listed, so the one thing said about them is said once: they exist, they are not
    /// readable by this build, and they are still on disk.
    /// </remarks>
    public static int CountFromOtherBuilds(string runsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runsDirectory);

        if (!Directory.Exists(runsDirectory))
            return 0;

        int count = 0;

        foreach (string metaPath in Directory.EnumerateFiles(runsDirectory, "*.meta.json"))
        {
            if (ReadMetadata(metaPath) is { } metadata && metadata.ProtocolVersion != DebugProtocol.Version)
                count++;
        }

        return count;
    }

    private static DebugRunMetadata? ReadMetadata(string metaPath)
    {
        try
        {
            using StreamReader reader = DebugJournal.OpenForReading(metaPath);

            return JsonConvert.DeserializeObject<DebugRunMetadata>(reader.ReadToEnd());
        }
        catch (Exception)
        {
            // One unreadable sidecar - half-written, or from a build that changed its shape - must not hide
            // every other run from the picker.
            return null;
        }
    }

    private static AvailableRun? TryReadMetadata(string runsDirectory, string metaPath)
    {
        if (ReadMetadata(metaPath) is not { } metadata)
            return null;

        // Not this protocol, not this build's to read. Nothing in the journal would decode, and offering the
        // run anyway would produce a board with a name and no content.
        if (metadata.ProtocolVersion != DebugProtocol.Version)
            return null;

        try
        {
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
                ProjectFilePath = metadata.Identity?.ProjectFilePath,
                CanRerun = metadata.Identity?.CanRerun ?? false,
                Source = SourceLocation.From(metadata.Identity?.SourceFilePath, metadata.Identity?.SourceLineNumber ?? 0),
                EventCount = metadata.EventCount,
                JournalPath = Path.Combine(runsDirectory, metadata.JournalFileName)
            };
        }
        catch (Exception)
        {
            // A sidecar naming a journal this platform cannot express as a path. One bad record must not hide
            // every other run from the picker.
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
