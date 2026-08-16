using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.DebugUI.Launcher;

/// <summary>One published release of the application.</summary>
public sealed record ReleaseInfo
{
    /// <summary>Gets the version the release publishes.</summary>
    public required Version Version { get; init; }

    /// <summary>Gets where the packaged application can be downloaded from.</summary>
    public required Uri DownloadUrl { get; init; }
}

/// <summary>
/// Where the launcher asks what the newest release is.
/// </summary>
/// <remarks>
/// An interface because the rules around it — which is newer, what to do when the answer never comes
/// — are worth testing without a network, and because the answer being unavailable is a normal
/// outcome rather than an error.
/// </remarks>
public interface IReleaseSource
{
    /// <summary>
    /// Asks for the newest release.
    /// </summary>
    /// <returns>The newest release, or <see langword="null"/> when the feed could not be reached.</returns>
    /// <remarks>
    /// Returns null rather than throwing for an unreachable feed. Being offline is not exceptional
    /// here; it is Tuesday on a train, and the launcher's job is to carry on regardless.
    /// </remarks>
    Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads a version out of a release tag.
/// </summary>
/// <remarks>
/// Tags are written by people and by release tooling, so both <c>v0.4.1</c> and <c>0.4.1</c> turn up.
/// A tag that is not a version at all is not an error — it is simply not a release this launcher can
/// install, and is ignored rather than allowed to stop the start-up.
/// </remarks>
public static class ReleaseTag
{
    /// <summary>Reads the version a tag names, or nothing when it does not name one.</summary>
    public static Version? Read(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        string trimmed = tag.Trim();

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        return Version.TryParse(trimmed, out Version? version) ? version : null;
    }
}
