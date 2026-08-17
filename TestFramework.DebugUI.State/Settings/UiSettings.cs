using System.Collections.Immutable;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Everything the tool remembers between sessions.
/// </summary>
/// <remarks>
/// <para>
/// One record rather than a setting per file, and plain data rather than anything WPF: it is written
/// as a whole, read as a whole, and the shape can be asserted without opening a window.
/// </para>
/// <para>
/// Every member has a usable default, because a settings file may be missing, older, or unreadable and
/// none of those may stop the tool from starting.
/// </para>
/// </remarks>
public sealed record UiSettings
{
    /// <summary>The shape this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Settings as they are before anything has been saved.</summary>
    public static UiSettings Defaults { get; } = new();

    /// <summary>
    /// Which shape the file on disk was written in.
    /// </summary>
    /// <remarks>
    /// Stored so a later build can migrate a file instead of discarding it. A file from a *newer*
    /// build than this one is read on a best-effort basis rather than rejected — the alternative is a
    /// downgrade silently wiping the settings the newer build wrote.
    /// </remarks>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Where and how big the window was.</summary>
    public WindowPlacement? Window { get; init; }

    /// <summary>
    /// The breakpoints the user set, so they survive a restart.
    /// </summary>
    /// <remarks>
    /// A breakpoint is keyed by stage and step precisely so it still applies the next time that test
    /// runs. Holding them only in memory meant that was true within a session and false across one,
    /// which is the half that matters least — you close the tool, fix the code, run again.
    /// </remarks>
    public ImmutableList<BreakpointMark> Breakpoints { get; init; } = ImmutableList<BreakpointMark>.Empty;

    /// <summary>How the tool behaves while it is watching rather than being looked at.</summary>
    public WatchSettings Watch { get; init; } = new();
}

/// <summary>Where the window was last left.</summary>
/// <remarks>
/// Restored only when it still lands on a monitor that exists. A window remembered onto a screen that
/// has since been unplugged is a window nobody can find, which reads as the app failing to start.
/// </remarks>
public sealed record WindowPlacement
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public bool IsMaximized { get; init; }
}

/// <summary>One step the user asked to stop at.</summary>
public sealed record BreakpointMark
{
    public required string Stage { get; init; }

    public required int StepId { get; init; }
}

/// <summary>
/// Watch mode: the tool keeps listening with its window out of the way, and speaks up when something
/// happens.
/// </summary>
/// <remarks>
/// The listening is not new — the transport runs for as long as the process does, and journalling is
/// independent of it. What watch mode adds is somewhere for the window to go that is not "closed", and
/// a way for the tool to say something while it is there.
/// </remarks>
public sealed record WatchSettings
{
    /// <summary>Whether the tool is watching.</summary>
    /// <remarks>
    /// <para>
    /// Turned on and off by a button in the title bar, and remembered, so someone who works this way
    /// finds it already armed next time. Off by default: the tool is normally opened to look at a run
    /// that has already happened, and putting an icon in someone's notification area is not something
    /// to do without being asked.
    /// </para>
    /// <para>
    /// While it is on, minimising hides the window to the notification area instead of the taskbar and
    /// a finished run raises a notification. Closing still closes — the mode governs where the window
    /// goes when it is put away, not whether the application can be quit.
    /// </para>
    /// </remarks>
    public bool Enabled { get; init; }

    /// <summary>Whether a finished run raises a notification.</summary>
    public bool NotifyOnFinish { get; init; } = true;

    /// <summary>Whether only runs that failed are worth interrupting for.</summary>
    /// <remarks>
    /// Off by default so the first thing a new user sees is that watching works at all. Anyone running
    /// a suite of hundreds will want it on, which is exactly why it is a setting.
    /// </remarks>
    public bool NotifyOnlyOnFailure { get; init; }
}
