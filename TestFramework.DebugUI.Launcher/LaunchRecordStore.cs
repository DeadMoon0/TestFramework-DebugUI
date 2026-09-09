using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// Reads and writes the launcher's <see cref="LaunchRecord"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here throws at the caller.</b> The record is an optimisation — it makes the launcher
/// stop offering a build that does not run — and no optimisation is worth a tool that will not start.
/// An unreadable file, a half-written one, a read-only folder: each resolves to
/// <see cref="LaunchRecord.Empty"/> or to a write that quietly did not happen, and the launcher
/// carries on doing what it did before this file existed.
/// </para>
/// <para>
/// Versions are stored as strings rather than as serialized <see cref="Version"/> objects, so the
/// file stays something a person can read and correct — deleting a line out of the quarantine is a
/// reasonable thing to want to do by hand — and so the parsing lives at this boundary instead of
/// leaking into the rules.
/// </para>
/// </remarks>
public sealed class LaunchRecordStore(LauncherPaths paths)
{
    private const string FileName = "launcher.json";

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath => Path.Combine(paths.Root, FileName);

    /// <summary>Reads the record, or an empty one when there is nothing usable to read.</summary>
    public LaunchRecord Read()
    {
        try
        {
            if (!File.Exists(FilePath))
                return LaunchRecord.Empty;

            Stored? stored = JsonConvert.DeserializeObject<Stored>(File.ReadAllText(FilePath));

            if (stored is null)
                return LaunchRecord.Empty;

            return new LaunchRecord
            {
                Attempted = Read(stored.Attempted),

                // A name that is not a version is dropped rather than rejected. The file may have
                // been edited by hand, and one bad line is not a reason to forget the rest.
                Quarantined = [.. (stored.Quarantined ?? []).Select(Read).Where(version => version is not null).Select(version => version!)]
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return LaunchRecord.Empty;
        }
    }

    /// <summary>Writes the record, and says nothing if it could not.</summary>
    public void Write(LaunchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            Directory.CreateDirectory(paths.Root);

            Stored stored = new()
            {
                Attempted = record.Attempted?.ToString(),
                Quarantined = [.. record.Quarantined.Select(version => version.ToString())]
            };

            File.WriteAllText(FilePath, JsonConvert.SerializeObject(stored, Formatting.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private static Version? Read(string? value) => Version.TryParse(value, out Version? version) ? version : null;

    /// <summary>The shape on disk, which is deliberately not the shape in memory.</summary>
    private sealed class Stored
    {
        public string? Attempted { get; set; }

        public List<string>? Quarantined { get; set; }
    }
}
