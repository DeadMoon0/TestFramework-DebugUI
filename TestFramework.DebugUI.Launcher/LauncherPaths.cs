using System;
using System.IO;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// Where the launcher keeps the application, and the folder it must create for runs to be recorded.
/// </summary>
/// <remarks>
/// <para>
/// Local application data, not roaming. Cached application versions and run journals are both large
/// and both about this machine; roaming them would drag the lot across a domain profile for no
/// benefit.
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
    private const string RootFolderName = "TestFramework";
    private const string JournalFolderName = "Debug";
    private const string VersionsFolderName = "versions";
    private const string StagingFolderName = "staging";

    /// <summary>The name of the application the launcher starts.</summary>
    public const string ApplicationExecutable = "TestFramework.DebugUI.exe";

    /// <summary>Creates the paths under the current user's local application data.</summary>
    public LauncherPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RootFolderName))
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
