using System;
using System.IO;

namespace TestFramework.DebugUI.State.Bundles;

/// <summary>
/// Finds the file a value was written to.
/// </summary>
/// <remarks>
/// <para>
/// A value too large to send is written into the run's own output, and the journal records both where it was
/// written and the relative path it is known by. On the machine that produced it the recorded path is simply
/// correct, and this changes nothing.
/// </para>
/// <para>
/// On a run that arrived from somewhere else the recorded path is the sender's, and points at a folder that
/// does not exist here. Rather than rewrite the journal — which is their record of what happened, not ours to
/// edit — the file is looked for beside the journal, where importing put it. One rule, two machines, and the
/// journal stays exactly as it was recorded.
/// </para>
/// </remarks>
public static class ValueFiles
{
    /// <summary>
    /// The file holding a value, or null when it cannot be found.
    /// </summary>
    /// <param name="recordedPath">The absolute path the journal recorded.</param>
    /// <param name="relativePath">The path the value is known by, such as <c>values/orderIds.json</c>.</param>
    /// <param name="journalPath">The journal the run was replayed from, when it came from disk.</param>
    public static string? Resolve(string? recordedPath, string? relativePath, string? journalPath)
    {
        // Where the producer put it. True for every run that has not travelled.
        if (!string.IsNullOrWhiteSpace(recordedPath) && File.Exists(recordedPath))
            return recordedPath;

        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(journalPath))
            return null;

        string beside = Path.Combine(
            BundleFormat.ArtifactFolderFor(journalPath),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(beside) ? beside : null;
    }

    /// <summary>Whether a value's file can be opened.</summary>
    public static bool Exists(string? recordedPath, string? relativePath, string? journalPath)
        => Resolve(recordedPath, relativePath, journalPath) is not null;
}
