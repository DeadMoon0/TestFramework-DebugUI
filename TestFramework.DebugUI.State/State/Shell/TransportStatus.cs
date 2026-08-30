namespace TestFramework.DebugUI.State.Shell;

/// <summary>
/// How the UI is currently connected to runs.
/// </summary>
public enum TransportStatus
{
    /// <summary>Nothing has been started yet.</summary>
    Idle,

    /// <summary>The pipe server is accepting connections.</summary>
    Listening,

    /// <summary>At least one run is attached.</summary>
    Attached,

    /// <summary>The transport failed; see the feed for the reason.</summary>
    Faulted
}

/// <summary>
/// What the transport is, and how busy it is.
/// </summary>
/// <remarks>
/// Separate from <see cref="TransportStatus"/> because the two answer different questions and are read
/// in different places: the status is one word in the title bar, and this is what the popup behind it
/// says. Deliberately never composed into that word — pairing them once produced "Attached — nothing
/// attached", which was true of a sticky status and nonsense to read.
/// </remarks>
public sealed record TransportDetails
{
    /// <summary>What is known before a transport has been started.</summary>
    public static TransportDetails Unknown { get; } = new();

    /// <summary>Gets how many runs are attached right now.</summary>
    public int AttachedRuns { get; init; }

    /// <summary>Gets how many may be attached at once.</summary>
    public int MaxConcurrentRuns { get; init; }

    /// <summary>Gets the pipe being listened on.</summary>
    public string PipeName { get; init; } = string.Empty;
}
