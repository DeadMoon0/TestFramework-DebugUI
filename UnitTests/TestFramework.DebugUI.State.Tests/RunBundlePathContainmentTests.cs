using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using TestFramework.DebugUI.State.Bundles;
using Xunit;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Pins that a bundle cannot write outside the folder it is imported into.
/// </summary>
/// <remarks>
/// The damage it prevents is a file written anywhere the user can write. A bundle is a file people send
/// each other and open with a double-click, and every path the importer writes comes from the bundle's own
/// manifest - so a manifest naming <c>..\..\</c> or a rooted path used to place a file of the sender's
/// choosing wherever it pointed, the Startup folder included.
/// </remarks>
public sealed class RunBundlePathContainmentTests : IDisposable
{
    private const string SessionId = "session-1";

    private readonly string root = Path.Combine(Path.GetTempPath(), "tfrun-containment-" + Guid.NewGuid().ToString("N"));

    private string Runs => Path.Combine(root, "recipient", "runs");

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    /// <summary>Stands for a file listed with no path at all.</summary>
    private const string NoPath = "no-path";

    [Fact]
    public void AFileListedWithoutAPathIsRefusedAsUnreadable()
    {
        string bundle = WriteBundle(journal: "run.jsonl", metadata: "run.meta.json", artifact: NoPath);

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));
    }

    [Fact]
    public void AnArtifactPathThatClimbsOutOfTheRunIsRefused()
    {
        string escaped = Path.Combine(root, "recipient", "escaped.txt");
        string bundle = WriteBundle(journal: "run.jsonl", metadata: "run.meta.json", artifact: @"..\..\escaped.txt");

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));

        Assert.False(File.Exists(escaped), "The bundle wrote a file outside the runs folder.");
    }

    [Fact]
    public void AnArtifactPathThatClimbsOutWithForwardSlashesIsRefused()
    {
        // The separator a zip entry and the writer both use, so the spelling a hand-built bundle is most
        // likely to carry.
        string escaped = Path.Combine(root, "recipient", "escaped.txt");
        string bundle = WriteBundle(journal: "run.jsonl", metadata: "run.meta.json", artifact: "../../escaped.txt");

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));

        Assert.False(File.Exists(escaped), "The bundle wrote a file outside the runs folder.");
    }

    [Fact]
    public void ARootedArtifactPathIsRefused()
    {
        string escaped = Path.Combine(root, "rooted.txt");
        string bundle = WriteBundle(journal: "run.jsonl", metadata: "run.meta.json", artifact: escaped);

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));

        Assert.False(File.Exists(escaped), "The bundle wrote a file outside the runs folder.");
    }

    [Fact]
    public void AJournalNameThatClimbsOutOfTheRunsFolderIsRefused()
    {
        string escaped = Path.Combine(root, "recipient", "escaped.jsonl");
        string bundle = WriteBundle(journal: @"..\escaped.jsonl", metadata: "run.meta.json", artifact: null);

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));

        Assert.False(File.Exists(escaped), "The bundle wrote a file outside the runs folder.");
    }

    [Fact]
    public void ARootedMetadataNameIsRefused()
    {
        string escaped = Path.Combine(root, "rooted.meta.json");
        string bundle = WriteBundle(journal: "run.jsonl", metadata: escaped, artifact: null);

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));

        Assert.False(File.Exists(escaped), "The bundle wrote a file outside the runs folder.");
    }

    [Fact]
    public void ARefusedBundleWritesNothingAtAll()
    {
        // The check runs before the first file lands: a bundle with one honest run and one hostile one must
        // not leave the honest half behind looking like a completed import.
        string bundle = WriteBundle(journal: "run.jsonl", metadata: "run.meta.json", artifact: @"..\..\escaped.txt", honestRunFirst: true);

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Runs));

        Assert.False(Directory.Exists(Runs) && Directory.GetFileSystemEntries(Runs).Length > 0, "A refused bundle still imported part of itself.");
    }

    private string WriteBundle(string journal, string metadata, string? artifact, bool honestRunFirst = false)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "hostile" + BundleFormat.Extension);

        BundleRun hostile = new()
        {
            SessionId = SessionId,
            Name = "hostile",
            JournalFileName = journal,
            MetadataFileName = metadata,
            Files = artifact is null ? [] : [new BundleFile { RelativePath = artifact }],
        };

        BundleRun honest = new()
        {
            SessionId = "session-0",
            Name = "honest",
            JournalFileName = "honest.jsonl",
            MetadataFileName = "honest.meta.json",
        };

        BundleManifest manifest = new()
        {
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            Runs = honestRunFirst ? [honest, hostile] : [hostile],
        };

        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            // A file the manifest lists without a path cannot be written by the serializer, so it is spelled out.
            Add(archive, BundleFormat.ManifestEntry, JsonConvert.SerializeObject(manifest).Replace($"\"RelativePath\":\"{NoPath}\"", "\"RelativePath\":null", StringComparison.Ordinal));

            foreach (BundleRun run in manifest.Runs)
            {
                string folder = BundleFormat.FolderFor(run.SessionId);
                Add(archive, $"{folder}/{run.JournalFileName}", "{}");
                Add(archive, $"{folder}/{run.MetadataFileName}", "{}");

                foreach (BundleFile file in run.Files)
                    Add(archive, $"{folder}/{file.RelativePath}", "written by the bundle");
            }
        }

        return path;
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
