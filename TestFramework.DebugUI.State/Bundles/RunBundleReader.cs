using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TestFramework.DebugUI.State.Annotations;

namespace TestFramework.DebugUI.State.Bundles;

/// <summary>
/// Reads a bundle someone else wrote and files its runs where the run list will find them.
/// </summary>
/// <remarks>
/// <para>
/// The journal and its sidecar go into the runs directory under the names they were exported with, so the
/// existing listing picks them up with no new transport and the sidecar's reference to its journal stays
/// correct without being rewritten.
/// </para>
/// <para>
/// Artifacts go into a folder beside the journal named after it, which is what lets a value's file be resolved
/// later from the journal's path alone.
/// </para>
/// </remarks>
public static class RunBundleReader
{
    /// <summary>What a bundle turned into.</summary>
    public sealed record Result
    {
        /// <summary>Gets the manifest that was read.</summary>
        public required BundleManifest Manifest { get; init; }

        /// <summary>Gets the runs that were written into the runs directory.</summary>
        public ImmutableList<BundleRun> Imported { get; init; } = [];

        /// <summary>
        /// Gets the runs that were already present and so were left alone.
        /// </summary>
        /// <remarks>
        /// A session identifier is the same on both machines, so importing the same bundle twice is the same
        /// run twice. Saying so is better than silently producing a duplicate of something already listed.
        /// </remarks>
        public ImmutableList<BundleRun> AlreadyPresent { get; init; } = [];

        /// <summary>Gets the artifact files whose content did not match the hash the journal recorded.</summary>
        public ImmutableList<string> Corrupt { get; init; } = [];

        /// <summary>Gets the artifact files the sender named but never sent.</summary>
        public ImmutableList<string> Missing { get; init; } = [];

        /// <summary>Gets the number of imported runs that arrived with marks drawn on them.</summary>
        public int Annotated => Imported.Count(run => run.HasAnnotations);

        /// <summary>Gets the session identifier worth selecting once the import is done.</summary>
        public string? FirstSessionId => Imported.FirstOrDefault()?.SessionId ?? AlreadyPresent.FirstOrDefault()?.SessionId;
    }

    /// <summary>Raised when a bundle cannot be read at all.</summary>
    public sealed class UnreadableException(string message) : Exception(message);

    /// <summary>Reads a bundle into a runs directory.</summary>
    public static Result Read(string bundlePath, string runsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runsDirectory);

        if (!File.Exists(bundlePath))
            throw new UnreadableException("There is no file at that path.");

        using ZipArchive archive = OpenArchive(bundlePath);

        BundleManifest manifest = ReadManifest(archive);

        if (manifest.FormatVersion > BundleFormat.Version)
        {
            throw new UnreadableException(
                $"This bundle was written by a newer version of the tool (format {manifest.FormatVersion}, this reads up to {BundleFormat.Version}).");
        }

        Directory.CreateDirectory(runsDirectory);

        List<BundleRun> imported = [];
        List<BundleRun> present = [];
        List<string> corrupt = [];
        List<string> missing = [];

        foreach (BundleRun run in manifest.Runs)
        {
            string metadataPath = Path.Combine(runsDirectory, run.MetadataFileName);
            string journalPath = Path.Combine(runsDirectory, run.JournalFileName);

            if (File.Exists(metadataPath) && File.Exists(journalPath))
            {
                present.Add(run);
                continue;
            }

            string folder = BundleFormat.FolderFor(run.SessionId);

            if (Entry(archive, $"{folder}/{run.JournalFileName}") is not { } journalEntry
                || Entry(archive, $"{folder}/{run.MetadataFileName}") is not { } metadataEntry)
            {
                continue;
            }

            // The artifacts land first. A run that appears in the list before its files are beside it is a run
            // whose values will not open for as long as the unpacking takes.
            string artifacts = BundleFormat.ArtifactFolderFor(journalPath);

            foreach (BundleFile file in run.Files)
            {
                if (file.WasMissing)
                {
                    missing.Add(file.RelativePath);
                    continue;
                }

                if (Entry(archive, $"{folder}/{file.RelativePath}") is not { } fileEntry)
                {
                    missing.Add(file.RelativePath);
                    continue;
                }

                string destination = Path.Combine(artifacts, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                fileEntry.ExtractToFile(destination, overwrite: true);

                if (!Matches(destination, file.ContentHash))
                    corrupt.Add(file.RelativePath);
            }

            // Before the journal is listed, like the artifacts: a run that appears with its marks arriving a
            // moment later would open showing an empty board somebody had annotated.
            if (Entry(archive, $"{folder}/{BundleFormat.AnnotationsFile}") is { } annotationsEntry)
                annotationsEntry.ExtractToFile(Annotations.AnnotationStore.PathFor(journalPath), overwrite: true);

            metadataEntry.ExtractToFile(metadataPath, overwrite: true);
            journalEntry.ExtractToFile(journalPath, overwrite: true);

            imported.Add(run);
        }

        return new Result
        {
            Manifest = manifest,
            Imported = [.. imported],
            AlreadyPresent = [.. present],
            Corrupt = [.. corrupt],
            Missing = [.. missing]
        };
    }

    /// <summary>Reads only the manifest, for showing someone what a bundle holds before taking it in.</summary>
    public static BundleManifest Describe(string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);

        using ZipArchive archive = OpenArchive(bundlePath);

        return ReadManifest(archive);
    }

    private static ZipArchive OpenArchive(string bundlePath)
    {
        try
        {
            return ZipFile.OpenRead(bundlePath);
        }
        catch (InvalidDataException)
        {
            // The extension is ours but the contents are a zip, so this is the case where someone renamed
            // something else, or the file arrived truncated.
            throw new UnreadableException("This file is not a run bundle, or it did not arrive intact.");
        }
    }

    private static BundleManifest ReadManifest(ZipArchive archive)
    {
        if (Entry(archive, BundleFormat.ManifestEntry) is not { } entry)
            throw new UnreadableException("This file has no manifest, so it is not a run bundle.");

        using Stream stream = entry.Open();
        using StreamReader reader = new(stream);

        return JsonConvert.DeserializeObject<BundleManifest>(reader.ReadToEnd())
               ?? throw new UnreadableException("The bundle's manifest could not be read.");
    }

    /// <summary>
    /// Finds an entry by name, tolerating the separator a writer happened to use.
    /// </summary>
    private static ZipArchiveEntry? Entry(ZipArchive archive, string name)
        => archive.GetEntry(name) ?? archive.GetEntry(name.Replace('/', '\\'));

    /// <summary>
    /// Whether a file arrived as the journal said it would.
    /// </summary>
    /// <remarks>
    /// The hash is the one the framework recorded when it wrote the value, so this checks the whole journey
    /// rather than just the zip's own integrity. A run with no hash recorded is accepted: it predates the
    /// field, and refusing it would make old runs unshareable.
    /// </remarks>
    private static bool Matches(string path, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
            return true;

        using FileStream stream = File.OpenRead(path);

        string actual = Convert.ToHexString(SHA256.HashData(stream));

        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
