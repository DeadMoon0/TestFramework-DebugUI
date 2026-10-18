using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// The application versions on this machine: which are here, and how a new one arrives.
/// </summary>
/// <remarks>
/// A version is a folder named after it. That is the whole index — there is no manifest to fall out
/// of step with the disk, and a version can be removed by deleting its folder, which is what someone
/// will do regardless of what the launcher would prefer.
/// </remarks>
public sealed class VersionStore(LauncherPaths paths)
{
    /// <summary>How many versions are kept, newest first.</summary>
    /// <remarks>
    /// Enough to step back past a bad release without keeping every build ever downloaded. Three is
    /// the point where the previous one and the one before it are both still there, which covers "the
    /// update broke it" and "so did the one before".
    /// </remarks>
    public const int RetainedVersions = 3;

    /// <summary>Every version installed, newest first.</summary>
    /// <remarks>
    /// A folder whose name is not a version, or that holds no application, is ignored rather than
    /// reported: an interrupted extraction or a stray folder should not stop the tool starting.
    /// </remarks>
    public IReadOnlyList<Version> Installed()
    {
        if (!Directory.Exists(paths.VersionsFolder))
            return [];

        List<Version> versions = [];

        foreach (string folder in Directory.EnumerateDirectories(paths.VersionsFolder))
        {
            if (Version.TryParse(Path.GetFileName(folder), out Version? version)
                && File.Exists(Path.Combine(folder, LauncherPaths.ApplicationExecutable)))
            {
                versions.Add(version);
            }
        }

        versions.Sort();
        versions.Reverse();

        return versions;
    }

    /// <summary>
    /// Switches run recording on for this machine, unless the launcher has done so before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The launcher's first job, before any update check, because it has to happen on the starts
    /// where the network is down and the check is skipped.
    /// </para>
    /// <para>
    /// Once and never again: after that the marker belongs to the UI's recording switch, and a
    /// person who turned recording off must find it still off on the next start. A machine that
    /// already has the journal folder but no flag — one armed by an older launcher, when the folder
    /// was the switch — counts as never armed, so it gets the marker once and keeps recording as it
    /// did. The marker is written before the flag, so a start that dies between the two arms again
    /// next time rather than never.
    /// </para>
    /// </remarks>
    public void ArmRecordingOnce()
    {
        if (File.Exists(paths.RecordingArmedFlag))
            return;

        Directory.CreateDirectory(paths.JournalFolder);
        File.WriteAllText(paths.RecordingMarker, string.Empty);
        File.WriteAllText(
            paths.RecordingArmedFlag,
            "The launcher switched run recording on for this machine once and will not do so again. Delete this file to have it do so on its next start.");
    }

    /// <summary>
    /// Downloads a release and puts it in place.
    /// </summary>
    /// <remarks>
    /// Unpacked into staging first and moved in afterwards, so a download that dies half way leaves
    /// no folder that <see cref="Installed"/> would report as a working version. The move is the
    /// commit.
    /// </remarks>
    /// <returns>Whether the download was checked against a digest the release named.</returns>
    public Task<bool> InstallAsync(ReleaseInfo release, HttpClient client, IProgress<double>? progress, CancellationToken cancellationToken)
        => InstallAsync(release, client, progress, StallTimeout, cancellationToken);

    /// <summary>Downloads a release and puts it in place, giving up on a download silent for <paramref name="stallTimeout"/>.</summary>
    internal async Task<bool> InstallAsync(ReleaseInfo release, HttpClient client, IProgress<double>? progress, TimeSpan stallTimeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(client);

        // What is downloaded here is then started. Over anything but https, whoever sits on the path chooses it.
        if (!string.Equals(release.DownloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The release {release.Version} is offered from '{release.DownloadUrl}', which is not https, so it was not downloaded.");

        string staging = Path.Combine(paths.StagingFolder, release.Version.ToString());

        Clear(staging);
        Directory.CreateDirectory(staging);

        string archive = Path.Combine(staging, "package.zip");

        await DownloadAsync(release.DownloadUrl, archive, client, progress, stallTimeout, cancellationToken).ConfigureAwait(false);

        bool digestChecked = Verify(archive, release.Digest);

        string unpacked = Path.Combine(staging, "app");
        ZipFile.ExtractToDirectory(archive, unpacked, overwriteFiles: true);

        // Checked before the move, because the move is the commit: a folder without the application would be
        // ignored by Installed(), and the next start would download the same wrong package again, every start.
        string root = RootOf(unpacked);
        if (!File.Exists(Path.Combine(root, LauncherPaths.ApplicationExecutable)))
        {
            Clear(staging);
            throw new InvalidDataException($"The release {release.Version} package holds no {LauncherPaths.ApplicationExecutable}, so it was not installed.");
        }

        string destination = paths.FolderFor(release.Version);
        Clear(destination);
        Directory.CreateDirectory(paths.VersionsFolder);
        Directory.Move(root, destination);

        Clear(staging);

        return digestChecked;
    }

    /// <summary>
    /// Checks a downloaded package against the digest the release named.
    /// </summary>
    /// <remarks>
    /// Throws when they disagree, and the caller treats that like any other failed download: fall
    /// back to what is installed. Silence would be the wrong answer here — this is the one moment the
    /// launcher takes an executable off the internet and puts it somewhere it will later run — but so
    /// would refusing to start, so it fails the update rather than the launcher.
    /// </remarks>
    /// <exception cref="InvalidDataException">The package is not the one the release describes.</exception>
    /// <returns>True when the archive was checked; false when the release named nothing this can check.</returns>
    internal static bool Verify(string archive, string? digest)
    {
        // No digest is not a failure. Releases predating the feed reporting one have nothing to check
        // against, and rejecting those would strand anyone on an old build - but it is said, not silent.
        if (string.IsNullOrWhiteSpace(digest))
            return false;

        const string prefix = "sha256:";

        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string expected = digest[prefix.Length..].Trim();

        using FileStream file = File.OpenRead(archive);
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The download does not match the digest the release named. Expected {expected}, got {actual}.");

        return true;
    }

    /// <summary>
    /// Finds the folder the application actually sits in.
    /// </summary>
    /// <remarks>
    /// A release archive may wrap its contents in a single folder or not, depending on how it was
    /// built. Following a lone folder down means the launcher works with both instead of depending on
    /// how someone happened to zip it.
    /// </remarks>
    private static string RootOf(string unpacked)
    {
        if (File.Exists(Path.Combine(unpacked, LauncherPaths.ApplicationExecutable)))
            return unpacked;

        string[] folders = Directory.GetDirectories(unpacked);

        return folders.Length == 1 ? folders[0] : unpacked;
    }

    /// <summary>
    /// How long a download may go without receiving a byte before it counts as failed.
    /// </summary>
    /// <remarks>
    /// A stall, not a total: a slow connection still finishes, and only one that has stopped is given up on. The
    /// download used to wait without limit, so a stalled CDN kept the launcher on its progress bar and the installed
    /// version - the fallback - was never reached.
    /// </remarks>
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private static async Task DownloadAsync(Uri url, string destination, HttpClient client, IProgress<double>? progress, TimeSpan stallTimeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(stallTimeout);

        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        await using FileStream file = File.Create(destination);

        byte[] buffer = new byte[81_920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
        {
            // Every byte that arrives restarts the clock.
            stall.CancelAfter(stallTimeout);

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;

            // Only when the server said how big it is. Inventing a percentage from an unknown total
            // produces a bar that reaches the end and stays there, which reads as a hang.
            if (total is > 0)
                progress?.Report((double)written / total.Value);
        }
    }

    /// <summary>Deletes versions beyond the retained few.</summary>
    /// <remarks>
    /// Failure here is ignored on purpose: a version folder locked by a still-running instance is a
    /// perfectly ordinary state, and it is not a reason to refuse to start.
    /// </remarks>
    public void Prune(Version running)
    {
        foreach (Version version in LaunchPlanner.Prunable(Installed(), running, RetainedVersions))
            Clear(paths.FolderFor(version));
    }

    private static void Clear(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
