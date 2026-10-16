using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI.Launcher;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// Covers what the launcher finds on disk, and the recording marker it must leave behind once.
/// </summary>
public sealed class VersionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tf-launcher-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AFreshMachineGetsRecordingSwitchedOn()
    {
        // The launcher's reason for existing, as much as the updating is. Core decides whether to
        // record a run by whether the marker is there, so until something creates it every run in
        // the world goes unrecorded and the UI's whole replay path can never fire.
        Store().ArmRecordingOnce();

        Assert.True(File.Exists(Paths().RecordingMarker));
        Assert.True(File.Exists(Paths().RecordingArmedFlag));
    }

    [Fact]
    public void RecordingSwitchedOffStaysOffOnTheNextStart()
    {
        // The UI's switch deletes the marker. A launcher that put it back on every start would make
        // "off" mean "off until the tool is next opened".
        Store().ArmRecordingOnce();
        File.Delete(Paths().RecordingMarker);

        Store().ArmRecordingOnce();

        Assert.False(File.Exists(Paths().RecordingMarker));
        Assert.True(Directory.Exists(Paths().JournalFolder));
    }

    [Fact]
    public void AMachineArmedByTheFolderAloneGetsTheMarkerOnce()
    {
        // An older launcher created only the folder, which used to be the switch. Such a machine has
        // been recording, so it must go on recording after the update rather than fall silent.
        Directory.CreateDirectory(Paths().JournalFolder);

        Store().ArmRecordingOnce();

        Assert.True(File.Exists(Paths().RecordingMarker));
    }

    [Fact]
    public void ArmingTwiceIsNotAnError()
    {
        // It runs on every start, and the second start is the normal one.
        Store().ArmRecordingOnce();
        Store().ArmRecordingOnce();

        Assert.True(File.Exists(Paths().RecordingMarker));
    }

    [Fact]
    public void InstalledVersionsComeBackNewestFirst()
    {
        Install("0.3.0");
        Install("0.5.0");
        Install("0.4.0");

        Assert.Equal([Version.Parse("0.5.0"), Version.Parse("0.4.0"), Version.Parse("0.3.0")], Store().Installed());
    }

    [Fact]
    public void AFolderWithNoApplicationInItIsNotAVersion()
    {
        // What an interrupted extraction leaves behind. Reporting it as installed would mean trying
        // to start something that is not there.
        Directory.CreateDirectory(Path.Combine(Paths().VersionsFolder, "0.4.0"));

        Assert.Empty(Store().Installed());
    }

    [Fact]
    public void AFolderThatIsNotNamedAfterAVersionIsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(Paths().VersionsFolder, "scratch"));
        File.WriteAllText(Path.Combine(Paths().VersionsFolder, "scratch", LauncherPaths.ApplicationExecutable), "x");

        Assert.Empty(Store().Installed());
    }

    [Fact]
    public void NothingInstalledIsAnEmptyListRatherThanAFailure()
    {
        // The state of a machine that has just installed the launcher and nothing else.
        Assert.Empty(Store().Installed());
    }

    [Fact]
    public void PruningKeepsTheNewestFewAndLeavesTheRunningOne()
    {
        for (int minor = 1; minor <= 5; minor++)
            Install($"0.{minor}.0");

        Store().Prune(running: Version.Parse("0.5.0"));

        Assert.Equal(
            [Version.Parse("0.5.0"), Version.Parse("0.4.0"), Version.Parse("0.3.0")],
            Store().Installed());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void APackageMatchingTheDigestTheReleaseNamedIsAccepted()
    {
        string archive = Payload("the real package");

        Assert.True(VersionStore.Verify(archive, "sha256:" + Sha256Of("the real package")));
    }

    [Fact]
    public void APackageThatIsNotTheOneTheReleaseDescribesIsRefused()
    {
        // The one point where the launcher takes code off the internet and puts it somewhere it will
        // later run. "The transport was encrypted" is a weaker claim than "the bytes are the ones
        // the release names", and this is the difference between them.
        string archive = Payload("something else entirely");

        Assert.Throws<InvalidDataException>(() => VersionStore.Verify(archive, "sha256:" + Sha256Of("the real package")));
    }

    [Fact]
    public void APackageWithNoDigestToCheckAgainstIsAccepted()
    {
        // Releases cut before the feed reported a digest have none. Refusing those would turn a
        // hardening step into a way of stranding people on old builds.
        string archive = Payload("anything");

        // Accepted, and said: false means nothing was checked, so the launcher tells the person.
        Assert.False(VersionStore.Verify(archive, null));
        Assert.False(VersionStore.Verify(archive, "   "));
    }

    [Fact]
    public void ADigestInSomeAlgorithmWeDoNotKnowIsIgnoredRatherThanFailed()
    {
        // Being handed an algorithm this launcher cannot compute is not evidence of tampering, and
        // treating it as such would break every existing launcher the day the feed changed format.
        string archive = Payload("anything");

        Assert.False(VersionStore.Verify(archive, "sha512:whatever"));
    }

    [Fact]
    public async Task AReleaseOfferedOverPlainHttpIsNotDownloaded()
    {
        // What the launcher downloads it then starts; over plain http, whoever sits on the path chooses it.
        ReleaseInfo release = new() { Version = new Version(9, 9, 9), DownloadUrl = new Uri("http://example.invalid/TestFramework.DebugUI.zip") };
        using HttpClient client = new();

        InvalidDataException refusal = await Assert.ThrowsAsync<InvalidDataException>(
            () => Store().InstallAsync(release, client, progress: null, CancellationToken.None));

        Assert.Contains("not https", refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Paths().FolderFor(release.Version)));
    }

    private string Payload(string content)
    {
        Directory.CreateDirectory(root);

        string path = Path.Combine(root, "package.zip");
        File.WriteAllText(path, content);

        return path;
    }

    private static string Sha256Of(string content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));

    private LauncherPaths Paths() => new(root);

    private VersionStore Store() => new(Paths());

    private void Install(string version)
    {
        string folder = Paths().FolderFor(Version.Parse(version));

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, LauncherPaths.ApplicationExecutable), "x");
    }
}
