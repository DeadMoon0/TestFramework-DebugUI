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
