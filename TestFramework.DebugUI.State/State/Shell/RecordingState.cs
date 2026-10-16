namespace TestFramework.DebugUI.State.Shell;

/// <summary>
/// Whether test runs on this machine are being recorded, and where to.
/// </summary>
/// <remarks>
/// In the state rather than read off the disk by each surface, so the transport popup, the empty run
/// lists and the settings switch all say the same thing, and all change together when the switch is
/// used.
/// </remarks>
public sealed record RecordingState
{
    /// <summary>What is known before anything has looked, and when the journal root cannot be told.</summary>
    public static RecordingState Unknown { get; } = new();

    /// <summary>Gets whether recording is on.</summary>
    public bool IsOn { get; init; }

    /// <summary>Gets the journal root, or null when it cannot be told.</summary>
    public string? Root { get; init; }

    /// <summary>One line saying whether runs are recorded and where.</summary>
    public string Summary => Root is null
        ? "Recording: unavailable on this machine"
        : $"Recording: {(IsOn ? "on" : "off")} — {Root}";

    /// <summary>What an empty list of runs says, truthfully for the current switch.</summary>
    public string EmptyRuns => IsOn
        ? "No runs yet. Recording is on, so every test run started from now on is kept here, whether or not this window is open."
        : "No runs yet. Recording is off, so a test run shows here only while this window is open, and is not kept. It can be turned on in Settings.";
}
