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

        EnsureFormatIsReadable(manifest);
        EnsureEveryPathStaysInside(manifest, runsDirectory);

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

            if (TryImportRun(archive, run, metadataPath, journalPath, corrupt, missing))
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

    private static void EnsureFormatIsReadable(BundleManifest manifest)
    {
        if (manifest.FormatVersion > BundleFormat.Version)
        {
            throw new UnreadableException(
                $"This bundle was written by a newer version of the tool (format {manifest.FormatVersion}, this reads up to {BundleFormat.Version}).");
        }
    }

    /// <summary>
    /// Unpacks one run beside the others, reporting whether the bundle carried enough of it to list.
    /// </summary>
    private static bool TryImportRun(
        ZipArchive archive,
        BundleRun run,
        string metadataPath,
        string journalPath,
        List<string> corrupt,
        List<string> missing)
    {
        string folder = BundleFormat.FolderFor(run.SessionId);

        if (Entry(archive, $"{folder}/{run.JournalFileName}") is not { } journalEntry
            || Entry(archive, $"{folder}/{run.MetadataFileName}") is not { } metadataEntry)
        {
            return false;
        }

        // The artifacts land first. A run that appears in the list before its files are beside it is a run
        // whose values will not open for as long as the unpacking takes.
        ExtractArtifacts(archive, run, folder, BundleFormat.ArtifactFolderFor(journalPath), corrupt, missing);

        // Before the journal is listed, like the artifacts: a run that appears with its marks arriving a
        // moment later would open showing an empty board somebody had annotated.
        if (Entry(archive, $"{folder}/{BundleFormat.AnnotationsFile}") is { } annotationsEntry)
            annotationsEntry.ExtractToFile(Annotations.AnnotationStore.PathFor(journalPath), overwrite: true);

        metadataEntry.ExtractToFile(metadataPath, overwrite: true);
        journalEntry.ExtractToFile(journalPath, overwrite: true);

        return true;
    }

    private static void ExtractArtifacts(
        ZipArchive archive,
        BundleRun run,
        string folder,
        string artifacts,
        List<string> corrupt,
        List<string> missing)
    {
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
    }

    /// <summary>
    /// Refuses the whole bundle when any path its manifest names would land outside the runs folder.
    /// </summary>
    /// <remarks>
    /// Every path the import writes comes from the bundle itself, and a bundle is a file someone else wrote.
    /// A journal or metadata name must be a plain file name, and an artifact must resolve inside its run's
    /// artifact folder: <c>..</c>, a rooted path or a drive-relative one is how a manifest used to write a file
    /// of the sender's choosing anywhere the user could write. Checked for every run before the first file
    /// lands, so a refused bundle leaves nothing behind that looks like half an import.
    /// </remarks>
    private static void EnsureEveryPathStaysInside(BundleManifest manifest, string runsDirectory)
    {
        string runs = Path.GetFullPath(runsDirectory);

        foreach (BundleRun run in manifest.Runs)
        {
            string journalPath = PlainFileIn(runs, run.JournalFileName, run.Name);
            PlainFileIn(runs, run.MetadataFileName, run.Name);

            string artifacts = Path.GetFullPath(BundleFormat.ArtifactFolderFor(journalPath));

            foreach (BundleFile file in run.Files)
            {
                // Required in the format, but the reader does not enforce it: a manifest without one is refused
                // like any other path that does not name a file in the run.
                if (string.IsNullOrWhiteSpace(file.RelativePath))
                    throw Outside(run.Name, file.RelativePath ?? "(none)");

                string relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(relative) || !IsInside(artifacts, Path.GetFullPath(Path.Combine(artifacts, relative))))
                    throw Outside(run.Name, file.RelativePath);
            }
        }
    }

    private static string PlainFileIn(string runs, string name, string runName)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name)
            throw Outside(runName, name);

        string path = Path.GetFullPath(Path.Combine(runs, name));
        if (!IsInside(runs, path))
            throw Outside(runName, name);

        return path;
    }

    private static bool IsInside(string folder, string path)
        => path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static UnreadableException Outside(string runName, string path)
        => new($"This bundle was not imported: its run '{runName}' names the file '{path}', which would land outside the runs folder. A bundle the tool wrote never does that, so this one was altered or built by hand.");

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
