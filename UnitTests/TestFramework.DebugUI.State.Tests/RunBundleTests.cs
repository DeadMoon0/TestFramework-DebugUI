using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using TestFramework.DebugUI.State.Bundles;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers writing a run into a bundle and reading it back somewhere else.
/// </summary>
/// <remarks>
/// Written against real files in a temporary folder rather than an abstraction over the file system. The whole
/// point of the feature is that a file leaves one machine and arrives on another intact, and a fake file system
/// would test everything except that.
/// </remarks>
public sealed class RunBundleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfrun-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the folder the whole test writes under.</summary>
    public RunBundleTests() => Directory.CreateDirectory(root);

    private string Sender => Ensure(Path.Combine(root, "sender", "runs"));

    private string Recipient => Ensure(Path.Combine(root, "recipient", "runs"));

    private string Output => Ensure(Path.Combine(root, "sender", "output", "values"));

    [Fact]
    public void ARunTravelsWithItsArtifacts()
    {
        string journal = WriteRun("alpha", artifact: "orderIds.json", content: "[1,2,3]");

        RunBundleWriter.Result written = Export(journal);

        Assert.Equal(1, written.RunCount);
        Assert.Equal(1, written.FileCount);
        Assert.Empty(written.MissingFiles);

        RunBundleReader.Result read = RunBundleReader.Read(written.Path, Recipient);

        BundleRun run = Assert.Single(read.Imported);

        Assert.Equal("alpha", run.SessionId);
        Assert.True(File.Exists(Path.Combine(Recipient, run.JournalFileName)), "the journal should be listed");
        Assert.True(File.Exists(Path.Combine(Recipient, run.MetadataFileName)), "the sidecar is what the list reads");
        Assert.Empty(read.Corrupt);
    }

    [Fact]
    public void AnImportedValueIsFoundBesideItsJournal()
    {
        // The recorded path is the sender's and does not exist here. This is the rule that makes an imported
        // run's values openable without rewriting the journal.
        string journal = WriteRun("beta", artifact: "report.txt", content: "hello");

        RunBundleReader.Result read = RunBundleReader.Read(Export(journal).Path, Recipient);
        BundleRun run = Assert.Single(read.Imported);

        string localJournal = Path.Combine(Recipient, run.JournalFileName);
        string sendersPath = Path.Combine(Output, "report.txt");

        Assert.Null(ValueFiles.Resolve(sendersPath + ".gone", "values/report.txt", journalPath: null));

        string? resolved = ValueFiles.Resolve(sendersPath + ".gone", "values/report.txt", localJournal);

        Assert.NotNull(resolved);
        Assert.Equal("hello", File.ReadAllText(resolved));
    }

    [Fact]
    public void TheRecordedPathWinsWhenItStillExists()
    {
        // On the machine that produced the run nothing should change.
        string file = Path.Combine(Output, "kept.txt");
        File.WriteAllText(file, "original");

        Assert.Equal(file, ValueFiles.Resolve(file, "values/kept.txt", journalPath: "/nowhere/x.ndjson"));
    }

    [Fact]
    public void ImportingTheSameBundleTwiceDoesNotDuplicateTheRun()
    {
        // A session id is the same on both machines, so the second import is the same run again.
        string bundle = Export(WriteRun("gamma", "a.txt", "x")).Path;

        Assert.Single(RunBundleReader.Read(bundle, Recipient).Imported);

        RunBundleReader.Result again = RunBundleReader.Read(bundle, Recipient);

        Assert.Empty(again.Imported);
        Assert.Single(again.AlreadyPresent);
    }

    [Fact]
    public void AFileTheJournalNamesButThatIsGoneIsReportedRatherThanSkipped()
    {
        string journal = WriteRun("delta", artifact: "vanished.bin", content: "gone soon");

        File.Delete(Path.Combine(Output, "vanished.bin"));

        RunBundleWriter.Result written = Export(journal);

        Assert.Equal("vanished.bin", Path.GetFileName(Assert.Single(written.MissingFiles)));

        RunBundleReader.Result read = RunBundleReader.Read(written.Path, Recipient);

        Assert.Single(read.Missing);
    }

    [Fact]
    public void AnArtifactThatArrivedWrongIsCalledOut()
    {
        // The hash is the framework's own, recorded when the value was written, so this checks the journey and
        // not merely the zip.
        string journal = WriteRun("epsilon", "payload.json", "correct", hash: "00" + new string('A', 62));

        RunBundleReader.Result read = RunBundleReader.Read(Export(journal).Path, Recipient);

        Assert.Equal("values/payload.json", Assert.Single(read.Corrupt));
    }

    [Fact]
    public void ArtifactsCanBeLeftOut()
    {
        string journal = WriteRun("zeta", "big.bin", "lots of bytes");

        RunBundleWriter.Result written = Export(journal, artifacts: false);

        Assert.Equal(0, written.FileCount);
        Assert.False(written.Manifest.IncludesArtifacts);

        RunBundleReader.Result read = RunBundleReader.Read(written.Path, Recipient);

        Assert.Single(read.Imported);
        Assert.Empty(read.Corrupt);
    }

    [Fact]
    public void ANormalExportCarriesTheJournalThroughUntouched()
    {
        // The journal is the framework's record of what happened, and a share must not edit it. Re-serialising
        // it did: Newtonsoft reads a timestamp as a date and writes it back in the exporter's own time zone, so
        // every "AtUtc" arrived carrying a local offset, drifted again on each hop, and disclosed the sender's
        // zone even on an anonymous export. Byte equality is the only assertion that catches that.
        string journal = WriteRun("mu", "a.txt", "1", stamp: "2026-08-18T12:00:00.1234567+00:00");

        byte[] before = File.ReadAllBytes(journal);

        RunBundleReader.Result read = RunBundleReader.Read(Export(journal).Path, Recipient);
        BundleRun run = Assert.Single(read.Imported);

        byte[] after = File.ReadAllBytes(Path.Combine(Recipient, run.JournalFileName));

        Assert.Equal(before, after);
    }

    [Fact]
    public void EvenAnAnonymousExportLeavesTheTimestampsAlone()
    {
        // Anonymity redacts named path fields. It must not quietly re-render every date on the way past, which
        // would leak the one thing an anonymous export is for hiding: where the sender is.
        string journal = WriteRun("nu", "a.txt", "1", stamp: "2026-08-18T12:00:00.1234567+00:00");

        RunBundleReader.Result read = RunBundleReader.Read(Export(journal, anonymous: true).Path, Recipient);

        string text = File.ReadAllText(Path.Combine(Recipient, Assert.Single(read.Imported).JournalFileName));

        Assert.Contains("2026-08-18T12:00:00.1234567+00:00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousExportKeepsThePathsTailAndDropsItsHead()
    {
        string journal = WriteRun("eta", "v.txt", "x");

        RunBundleWriter.Result written = Export(journal, anonymous: true);

        Assert.True(written.Manifest.IsAnonymous);
        Assert.Null(written.Manifest.ExportedBy);
        Assert.Null(written.Manifest.MachineName);

        RunBundleReader.Result read = RunBundleReader.Read(written.Path, Recipient);
        BundleRun run = Assert.Single(read.Imported);

        string text = File.ReadAllText(Path.Combine(Recipient, run.MetadataFileName));

        Assert.DoesNotContain("SecretPerson", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(RunAnonymiser.UserPlaceholder, text, StringComparison.Ordinal);

        // The tail is what lets the recipient find the same project, so it has to survive.
        Assert.Contains("Suite.Tests.csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousExportLeavesTheLogsAloneAndSaysSo()
    {
        // The line the user drew: rewrite the envelope, never the evidence — but do not pretend the evidence
        // was checked.
        string journal = WriteRun("theta", "v.txt", "x", logMessage: @"copied C:\Users\SecretPerson\thing.txt");

        RunBundleWriter.Result written = Export(journal, anonymous: true);

        AnonymityWarning warning = Assert.Single(written.Warnings.Where(entry => entry.Field == "Message"));

        Assert.Equal(1, warning.Occurrences);

        RunBundleReader.Result read = RunBundleReader.Read(written.Path, Recipient);
        string text = File.ReadAllText(Path.Combine(Recipient, Assert.Single(read.Imported).JournalFileName));

        Assert.Contains("SecretPerson", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANormalExportChangesNothingAndWarnsAboutNothing()
    {
        string journal = WriteRun("iota", "v.txt", "x", logMessage: @"copied C:\Users\SecretPerson\thing.txt");

        RunBundleWriter.Result written = Export(journal);

        Assert.False(written.Manifest.IsAnonymous);
        Assert.Equal("Someone", written.Manifest.ExportedBy);
        Assert.Empty(written.Warnings);
    }

    [Fact]
    public void SomethingThatIsNotABundleIsRefusedWithAnExplanation()
    {
        string notABundle = Path.Combine(root, "notes.tfrun");
        File.WriteAllText(notABundle, "just some text");

        RunBundleReader.UnreadableException error =
            Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(notABundle, Recipient));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void ABundleFromANewerToolIsRefusedRatherThanHalfRead()
    {
        string bundle = Export(WriteRun("kappa", "v.txt", "x")).Path;

        // Rewrite the manifest to claim a format this reader does not know.
        Bump(bundle, BundleFormat.Version + 5);

        Assert.Throws<RunBundleReader.UnreadableException>(() => RunBundleReader.Read(bundle, Recipient));
    }

    [Fact]
    public void ManyRunsShareOneBundle()
    {
        string first = WriteRun("one", "a.txt", "1");
        string second = WriteRun("two", "b.txt", "2");

        RunBundleWriter.Result written = Export(first, second);

        Assert.Equal(2, written.RunCount);
        Assert.Equal(2, RunBundleReader.Read(written.Path, Recipient).Imported.Count);
    }

    [Fact]
    public void ABundleCanBeDescribedWithoutBeingImported()
    {
        string bundle = Export(WriteRun("lambda", "a.txt", "1")).Path;

        BundleManifest manifest = RunBundleReader.Describe(bundle);

        Assert.Equal(BundleFormat.Version, manifest.FormatVersion);
        Assert.Equal("lambda", Assert.Single(manifest.Runs).SessionId);
        Assert.Empty(Directory.Exists(Recipient) ? Directory.GetFiles(Recipient) : []);
    }

    /// <summary>Writes a journal, its sidecar and one artifact, the way a real run leaves them.</summary>
    private string WriteRun(string sessionId, string artifact, string content, string? hash = null, string? logMessage = null, string? stamp = null)
    {
        string file = Path.Combine(Output, artifact);
        File.WriteAllText(file, content);

        string project = @"C:\Users\SecretPerson\source\repos\Thing\Suite.Tests\Suite.Tests.csproj";

        JObject structure = new()
        {
            ["Kind"] = 1,
            ["SessionId"] = sessionId,
            ["ProjectPath"] = project,
            ["Body"] = new JObject
            {
                ["Path"] = file,
                ["RelativePath"] = "values/" + artifact,
                ["SizeInBytes"] = content.Length,
                ["ContentHash"] = hash ?? Sha256(content)
            }
        };

        JObject log = new()
        {
            ["Kind"] = 5,
            ["SessionId"] = sessionId,
            ["AtUtc"] = stamp ?? "2026-08-18T12:00:00+00:00",
            ["Message"] = logMessage ?? "nothing to see"
        };

        string journalName = $"20260818-120000-{sessionId}.ndjson";
        string journalPath = Path.Combine(Sender, journalName);

        File.WriteAllLines(journalPath, [structure.ToString(Newtonsoft.Json.Formatting.None), log.ToString(Newtonsoft.Json.Formatting.None)]);

        JObject metadata = new()
        {
            ["ProtocolVersion"] = 2,
            ["SessionId"] = sessionId,
            ["Name"] = "TestNamed" + sessionId,
            ["MachineName"] = "SENDER-PC",
            ["StartedAtUtc"] = "2026-08-18T12:00:00+00:00",
            ["JournalFileName"] = journalName,
            ["Identity"] = new JObject
            {
                ["FullyQualifiedName"] = "Suite.Tests.LoginTests.Works",
                ["ProjectFilePath"] = project,
                ["SourceFilePath"] = @"C:\Users\SecretPerson\source\repos\Thing\Suite.Tests\LoginTests.cs",
                ["CanRerun"] = true
            }
        };

        File.WriteAllText(RunBundleWriter.MetadataPathFor(journalPath), metadata.ToString());

        return journalPath;
    }

    private RunBundleWriter.Result Export(params string[] journals) => Export(true, false, journals);

    private RunBundleWriter.Result Export(string journal, bool artifacts = true, bool anonymous = false)
        => Export(artifacts, anonymous, journal);

    private RunBundleWriter.Result Export(bool artifacts, bool anonymous, params string[] journals)
        => RunBundleWriter.Write(
            Path.Combine(root, $"bundle-{Guid.NewGuid():N}{BundleFormat.Extension}"),
            new RunBundleWriter.Request
            {
                JournalPaths = [.. journals],
                IncludeArtifacts = artifacts,
                Anonymous = anonymous,
                ExportedBy = "Someone",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                Identity = new RunAnonymiser.Identity
                {
                    UserProfilePath = @"C:\Users\SecretPerson",
                    UserName = "SecretPerson",
                    MachineName = "SENDER-PC"
                }
            });

    /// <summary>Rewrites a bundle's manifest to claim a different format version.</summary>
    private static void Bump(string bundlePath, int version)
    {
        using System.IO.Compression.ZipArchive archive =
            System.IO.Compression.ZipFile.Open(bundlePath, System.IO.Compression.ZipArchiveMode.Update);

        System.IO.Compression.ZipArchiveEntry entry = archive.GetEntry(BundleFormat.ManifestEntry)!;

        string json;
        using (StreamReader reader = new(entry.Open()))
            json = reader.ReadToEnd();

        JObject manifest = JObject.Parse(json);
        manifest["FormatVersion"] = version;

        entry.Delete();

        using Stream stream = archive.CreateEntry(BundleFormat.ManifestEntry).Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(false));

        writer.Write(manifest.ToString());
    }

    private static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not worth failing a test over.
        }
    }
}
