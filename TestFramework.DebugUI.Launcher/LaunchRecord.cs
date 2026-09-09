using System;
using System.Collections.Generic;
using System.Linq;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// What the launcher remembers between starts.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the folder listing cannot answer the question that matters. A version folder
/// with an application in it means the download finished — nothing more. Whether that version has
/// ever managed to open a window is a different fact, it is not on disk anywhere, and it is the one
/// that decides whether offering it again is helpful or is handing someone the same broken build
/// every time they click the icon.
/// </para>
/// <para>
/// Two fields, and the pair is the whole mechanism. <see cref="Attempted"/> is written before a
/// version is started and cleared once it has been seen to survive, so finding it still set on the
/// next start is exactly the evidence that the version did not come up. <see cref="Quarantined"/> is
/// where it goes then, because the absence of a confirmation stops being evidence as soon as it has
/// been acted on once.
/// </para>
/// </remarks>
public sealed record LaunchRecord
{
    /// <summary>What a machine that has never run the launcher remembers.</summary>
    public static readonly LaunchRecord Empty = new();

    /// <summary>The version that was started and not yet seen to survive.</summary>
    public Version? Attempted { get; init; }

    /// <summary>Versions that were started and did not stay running.</summary>
    public IReadOnlyList<Version> Quarantined { get; init; } = [];

    /// <summary>Records that a version is about to be started.</summary>
    public LaunchRecord Attempting(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return this with { Attempted = version };
    }

    /// <summary>
    /// Records that a version came up and stayed up.
    /// </summary>
    /// <remarks>
    /// Clears the quarantine for that version as well as the attempt. A version that works now is
    /// not suspect however it behaved before — the machine it failed on may simply have been missing
    /// a runtime that has since been installed, and holding that against it forever would leave
    /// someone permanently on an older build for a reason that no longer exists.
    /// </remarks>
    public LaunchRecord Survived(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new LaunchRecord
        {
            Attempted = null,
            Quarantined = [.. Quarantined.Where(quarantined => quarantined != version)]
        };
    }

    /// <summary>Records that a version was started and did not stay running.</summary>
    public LaunchRecord Quarantining(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new LaunchRecord
        {
            Attempted = null,
            Quarantined = Quarantined.Contains(version) ? Quarantined : [.. Quarantined, version]
        };
    }

    /// <summary>
    /// Turns an unconfirmed attempt into a quarantine, which is what reading the record means.
    /// </summary>
    /// <remarks>
    /// Called once, on start-up, before anything is decided. Separated from the decision so that the
    /// rule — an attempt nobody confirmed is a version that failed — is stated in one place rather
    /// than implied by the order two other things happen in.
    /// </remarks>
    public LaunchRecord Settled() => Attempted is null ? this : Quarantining(Attempted);
}
