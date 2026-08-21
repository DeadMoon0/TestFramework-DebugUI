using System;

namespace TestFramework.DebugUI.State.Shell.Feed;

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
