using System;
using System.IO;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Where the tool keeps what belongs to the person using it.
/// </summary>
/// <remarks>
/// <para>
/// One place, because there were three: the log, the settings and the theme store each spelled the
/// same two folder names out for themselves, so moving the root meant finding all three — and the
/// launcher besides, which is in another project and does not reference this one.
/// </para>
/// <para>
/// Beside the run journal rather than inside the installed folder: the launcher keeps several
/// versions side by side and replaces them wholesale, so anything written into a version's own
/// directory is gone at the next update, which is the one moment a user is most likely to notice.
/// </para>
/// <para>
/// The profile root, deliberately not <c>AppData</c>, which follows the launcher rather than being
/// decided here: this folder sits beside the journal root, and that one cannot live under
/// <c>AppData</c> without risking a handshake that fails silently if either side is ever packaged
/// (<c>LauncherPaths</c> makes the argument). Two of the things kept here want it for their own sake
/// anyway — the log is at a path somebody is asked to send, and a theme file is one they drop in by
/// hand — and neither survives being virtualized into a per-package store the person cannot see.
/// </para>
/// </remarks>
public static class ToolPaths
{
    /// <remarks>
    /// A dot folder beside <c>.nuget</c> and <c>.dotnet</c>: the same kind of thing, and kept out of
    /// the way of anyone browsing their own profile. It has to match the root
    /// <c>TestFramework.Core</c> resolves the run journal under; the launcher's tests hold the two to
    /// each other, because nothing else can.
    /// </remarks>
    private const string RootFolderName = ".testframework";

    private const string ToolFolderName = "DebugUI";

    /// <summary>Everything the tool family keeps for this user, on this machine.</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        RootFolderName);

    /// <summary>Everything this tool in particular keeps.</summary>
    public static string ToolFolder => Path.Combine(Root, ToolFolderName);
}
