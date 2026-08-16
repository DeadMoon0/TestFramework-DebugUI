using System;

namespace TestFramework.DebugUI.Launcher;

/// <summary>What the launcher should do, once it knows what is installed and what is published.</summary>
public enum LaunchAction
{
    /// <summary>Start a version already on this machine.</summary>
    Start,

    /// <summary>Fetch a newer version, then start it.</summary>
    Update,

    /// <summary>There is nothing to start and nothing that can be fetched.</summary>
    Stuck
}

/// <summary>
/// The decision, and the reason for it.
/// </summary>
/// <remarks>
/// The reason is carried because it is what the launcher's one line of status text says. A launcher
/// that only ever reads "Starting…" is indistinguishable from one that silently failed to check.
/// </remarks>
public sealed record LaunchDecision
{
    /// <summary>Gets what to do.</summary>
    public required LaunchAction Action { get; init; }

    /// <summary>Gets the version to start, when there is one to start.</summary>
    public Version? Version { get; init; }

    /// <summary>Gets the release to fetch, when one is to be fetched.</summary>
    public ReleaseInfo? Release { get; init; }

    /// <summary>Gets the line to show the person waiting.</summary>
    public required string Reason { get; init; }
}
