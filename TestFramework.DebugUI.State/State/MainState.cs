using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TestFramework.DebugUI.State;

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
/// The root of the UI's state.
/// </summary>
/// <remarks>
/// A <c>record struct</c> with public fields, which is what Axiom's reflection-based cloner walks.
/// Everything here is either a scalar or a reference to an immutable graph, so a dispatch clones a
/// handful of nodes rather than the whole run — see <see cref="RunGraph"/> for why that split
/// exists.
/// </remarks>
public record struct MainState()
{
    /// <summary>Every run this UI knows about, live or replayed, newest first.</summary>
    public ImmutableList<RunSummary> Runs = ImmutableList<RunSummary>.Empty;

    /// <summary>The session currently rendered, or null when none is selected.</summary>
    public string? SelectedSessionId = null;

    /// <summary>
    /// The graph of the selected run.
    /// </summary>
    /// <remarks>
    /// Only the selected run is projected in full. Every other session contributes just a
    /// <see cref="RunSummary"/>, which is what keeps a suite running twenty timelines at once from
    /// inflating the per-dispatch clone.
    /// </remarks>
    public RunGraph ActiveRun = RunGraph.Empty;

    /// <summary>Transport and feed state.</summary>
    public ShellState Shell = new();
}

/// <summary>
/// State that belongs to the shell rather than to any one run.
/// </summary>
public record struct ShellState()
{
    /// <summary>How the UI is connected.</summary>
    public TransportStatus Transport = TransportStatus.Idle;

    /// <summary>
    /// The running record of what every session is doing, including unselected ones.
    /// </summary>
    /// <remarks>
    /// Bounded rather than unbounded: the previous UI appended to an ever-growing
    /// <c>DebugInfo</c> string for the life of the process.
    /// </remarks>
    public ImmutableList<FeedEntry> Feed = ImmutableList<FeedEntry>.Empty;

    /// <summary>Feed entries not yet seen by the user.</summary>
    public int UnreadFeedCount = 0;
}

/// <summary>
/// What a run picker needs to list a run, without projecting the run itself.
/// </summary>
public sealed record RunSummary
{
    /// <summary>Gets the session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the run's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Gets a value indicating whether the run is still producing events.</summary>
    public bool IsLive { get; init; }

    /// <summary>Gets a value indicating whether the run reached a terminal state.</summary>
    public bool IsFinished { get; init; }

    /// <summary>
    /// Gets a value indicating whether any of the run's steps is paused at a breakpoint.
    /// </summary>
    /// <remarks>
    /// Surfaced on the summary so the picker can badge which of several parallel runs is waiting
    /// for the user, without projecting them all.
    /// </remarks>
    public bool IsWaitingAtBreakpoint { get; init; }

    /// <summary>Gets the fully qualified test name, when the run could be identified.</summary>
    public string? FullyQualifiedName { get; init; }

    /// <summary>Gets a value indicating whether this run carries enough identity to be re-run.</summary>
    public bool CanRerun { get; init; }
}

/// <summary>How serious a feed entry is.</summary>
public enum FeedSeverity
{
    /// <summary>Routine progress.</summary>
    Info,

    /// <summary>Something worth noticing that did not fail.</summary>
    Warning,

    /// <summary>A tool failure. Never a failing test, which is content rather than an error.</summary>
    Error
}

/// <summary>Where a feed entry came from.</summary>
public enum FeedSource
{
    /// <summary>A run reported progress or an outcome.</summary>
    Run,

    /// <summary>The pipe transport.</summary>
    Transport,

    /// <summary>The run journal.</summary>
    Journal,

    /// <summary>A re-run the UI started.</summary>
    Rerun,

    /// <summary>The application itself.</summary>
    App
}

/// <summary>
/// One line in the message feed.
/// </summary>
/// <remarks>
/// Entries carry the session and step they refer to so the feed is navigable: clicking an entry
/// selects that run and lands on the step, which is what turns "run 3 of 4 failed" into one click
/// rather than a hunt.
/// </remarks>
public sealed record FeedEntry
{
    /// <summary>Gets when the entry was recorded.</summary>
    public DateTimeOffset AtUtc { get; init; }

    /// <summary>Gets the severity.</summary>
    public FeedSeverity Severity { get; init; }

    /// <summary>Gets what produced the entry.</summary>
    public FeedSource Source { get; init; }

    /// <summary>Gets the one-line summary.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the longer explanation, when there is one.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the run this entry refers to, when it refers to one.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the stage this entry refers to, when it refers to one.</summary>
    public string? Stage { get; init; }

    /// <summary>Gets the step this entry refers to, when it refers to one.</summary>
    public int? StepId { get; init; }
}
