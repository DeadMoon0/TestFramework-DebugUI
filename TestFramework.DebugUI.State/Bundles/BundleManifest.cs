using System;
using System.Collections.Immutable;

namespace TestFramework.DebugUI.State.Bundles;

/// <summary>
/// What a bundle says about itself, before anything is unpacked.
/// </summary>
/// <remarks>
/// Read first, so the importer can refuse a format it does not understand, tell the reader what is inside,
/// and check every file against the hash the journal already recorded — rather than discovering a truncated
/// artifact later, when it looks like a corrupt value instead of a bad download.
/// </remarks>
public sealed record BundleManifest
{
    /// <summary>Gets the layout version of the bundle.</summary>
    public int FormatVersion { get; init; } = BundleFormat.Version;

    /// <summary>Gets the tool that wrote it.</summary>
    public string Tool { get; init; } = "TestFramework.DebugUI";

    /// <summary>Gets the tool's version, when it reported one.</summary>
    public string? ToolVersion { get; init; }

    /// <summary>Gets when the bundle was written.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets who exported it, or null on an anonymous export.</summary>
    public string? ExportedBy { get; init; }

    /// <summary>Gets the machine it came from, or null on an anonymous export.</summary>
    public string? MachineName { get; init; }

    /// <summary>Gets a value indicating whether identifying fields were redacted.</summary>
    /// <remarks>
    /// Stated rather than inferred, so the recipient can see that paths were rewritten on purpose instead of
    /// wondering why every path begins with a placeholder.
    /// </remarks>
    public bool IsAnonymous { get; init; }

    /// <summary>Gets a value indicating whether artifact files were included.</summary>
    public bool IncludesArtifacts { get; init; }

    /// <summary>Gets a value indicating whether the marks drawn on the runs were included.</summary>
    public bool IncludesAnnotations { get; init; }

    /// <summary>Gets the runs inside.</summary>
    public ImmutableList<BundleRun> Runs { get; init; } = [];
}

/// <summary>One run inside a bundle.</summary>
public sealed record BundleRun
{
    /// <summary>Gets the run's session identifier, which is what makes it the same run on both machines.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the run's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the fully qualified test name, when the run carried one.</summary>
    public string? FullyQualifiedName { get; init; }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Gets when the run stopped, when it is known to have stopped.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>
    /// Gets the journal's file name, kept as it was.
    /// </summary>
    /// <remarks>
    /// Preserved rather than regenerated so the sidecar, which names the journal, stays correct without being
    /// rewritten. The name already carries a timestamp and the session id, so it cannot collide with another
    /// run — only with the same run imported twice, which is detected on its own terms.
    /// </remarks>
    public required string JournalFileName { get; init; }

    /// <summary>Gets the sidecar's file name, kept as it was.</summary>
    public required string MetadataFileName { get; init; }

    /// <summary>Gets the artifact files carried with the run.</summary>
    public ImmutableList<BundleFile> Files { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether somebody had drawn on this run.
    /// </summary>
    /// <remarks>
    /// Stated in the manifest so a reader can be told a bundle carries somebody's notes before unpacking it — the
    /// marks are usually the reason it was sent.
    /// </remarks>
    public bool HasAnnotations { get; init; }
}

/// <summary>One artifact file carried with a run.</summary>
public sealed record BundleFile
{
    /// <summary>Gets the path the journal refers to it by, such as <c>values/orderIds.json</c>.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the size the journal recorded.</summary>
    public long SizeInBytes { get; init; }

    /// <summary>Gets the hash the journal recorded, which is what arrival is checked against.</summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file could not be found when the bundle was written.
    /// </summary>
    /// <remarks>
    /// Recorded rather than omitted. Test output gets cleaned up, so a journal can name a file that no longer
    /// exists; saying so lets the recipient learn it was never sent instead of wondering why it will not open.
    /// </remarks>
    public bool WasMissing { get; init; }
}
