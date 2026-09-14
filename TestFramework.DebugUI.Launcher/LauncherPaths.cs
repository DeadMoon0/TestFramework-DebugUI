using System;
using System.IO;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// Where the launcher keeps the application, and the folder it must create for runs to be recorded.
/// </summary>
/// <remarks>
/// <para>
/// The profile root, deliberately not <c>AppData</c>, and this is the project the choice is really
/// about, because this is the side that creates the journal folder below. Windows virtualizes a
/// <em>packaged</em> application's writes under <c>AppData</c> into a per-package store that no
/// unpackaged process can see: were the launcher ever packaged, it would create that folder into its
/// own store, Core would go on finding nothing, and run recording would read as switched off on
/// every machine in the world with nothing reporting a failure. Outside <c>AppData</c> there is one
/// folder and both processes see it, whatever either of them is packaged as.
/// </para>
/// <para>
/// It is not packaged today — this ships as a per-user installer, MSIX having been rejected because
/// it will not install at all without a trusted signature — so <c>AppData\Local</c> would work as
/// things stand. The point is that nothing here would have to change if that were revisited, and the
/// failure mode of getting it wrong is one nobody would be told about.
/// </para>
/// <para>
/// What that costs: a classic roaming profile excludes <c>AppData\Local</c> and does not exclude the
/// profile root, so the cached versions below can follow a user between machines, which is exactly
/// what the previous location was chosen to prevent. Accepted knowingly — a handshake that holds
/// whatever the packaging is beats a cache that stays put — and the retention limit keeps the size of
/// it bounded.
/// </para>
/// <para>
/// The journal folder is the load-bearing one. Core decides whether to record a run by whether that
/// folder exists — so until something creates it, every run in the world goes unrecorded and the
/// UI's whole replay path, which is written and tested, can never fire. Creating it is the
/// launcher's first job, ahead of any update check, because it must happen even on the runs where
/// the network is down and the update is skipped.
/// </para>
/// </remarks>
public sealed class LauncherPaths
{
    /// <remarks>
    /// Must match the root <c>TestFramework.Core</c> resolves the run journal under. The launcher
    /// does not reference Core — a dependency-free shell is the point of it — so the agreement cannot
    /// be a shared constant, and it is held by a test instead rather than by this comment.
    /// </remarks>
    private const string RootFolderName = ".testframework";

    private const string JournalFolderName = "Debug";
    private const string VersionsFolderName = "versions";
    private const string StagingFolderName = "staging";

    /// <summary>The name of the application the launcher starts.</summary>
    public const string ApplicationExecutable = "TestFramework.DebugUI.exe";

    /// <summary>Creates the paths under the current user's profile.</summary>
    public LauncherPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), RootFolderName))
    {
    }

    /// <summary>Creates the paths under an explicit root, which only tests need.</summary>
    public LauncherPaths(string root)
    {
        Root = root;
    }

    /// <summary>Everything the tool owns on this machine.</summary>
    public string Root { get; }

    /// <summary>The folder whose existence tells Core to record runs.</summary>
    public string JournalFolder => Path.Combine(Root, JournalFolderName);

    /// <summary>Where each installed version of the application lives, one folder per version.</summary>
    public string VersionsFolder => Path.Combine(Root, VersionsFolderName);

    /// <summary>Where a download is unpacked before it is moved into place.</summary>
    /// <remarks>
    /// Separate from the versions folder so a download interrupted half way cannot be mistaken for an
    /// installed version — the move into <see cref="VersionsFolder"/> is what marks it complete.
    /// </remarks>
    public string StagingFolder => Path.Combine(Root, StagingFolderName);

    /// <summary>The folder one version lives in.</summary>
    public string FolderFor(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return Path.Combine(VersionsFolder, version.ToString());
    }

    /// <summary>The application to start for one version.</summary>
    public string ExecutableFor(Version version) => Path.Combine(FolderFor(version), ApplicationExecutable);
}
