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

    /// <summary>Creates the folder that tells Core to record runs.</summary>
    /// <remarks>
    /// The launcher's first job, before any update check, because it has to happen on the starts
    /// where the network is down and the check is skipped.
    /// </remarks>
    public void EnsureJournalFolder() => Directory.CreateDirectory(paths.JournalFolder);

    /// <summary>
    /// Downloads a release and puts it in place.
    /// </summary>
    /// <remarks>
    /// Unpacked into staging first and moved in afterwards, so a download that dies half way leaves
    /// no folder that <see cref="Installed"/> would report as a working version. The move is the
    /// commit.
    /// </remarks>
    public async Task InstallAsync(ReleaseInfo release, HttpClient client, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(client);

        string staging = Path.Combine(paths.StagingFolder, release.Version.ToString());

        Clear(staging);
        Directory.CreateDirectory(staging);

        string archive = Path.Combine(staging, "package.zip");

        await DownloadAsync(release.DownloadUrl, archive, client, progress, cancellationToken).ConfigureAwait(false);

        string unpacked = Path.Combine(staging, "app");
        ZipFile.ExtractToDirectory(archive, unpacked, overwriteFiles: true);

        string destination = paths.FolderFor(release.Version);
        Clear(destination);
        Directory.CreateDirectory(paths.VersionsFolder);
        Directory.Move(RootOf(unpacked), destination);

        Clear(staging);
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

    private static async Task DownloadAsync(Uri url, string destination, HttpClient client, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream file = File.Create(destination);

        byte[] buffer = new byte[81_920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
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
