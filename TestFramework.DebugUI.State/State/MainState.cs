using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TestFramework.Core.Debugger;

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

    /// <summary>
    /// How the selected run's values stand against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// Kept beside the graph rather than inside it: the graph is what the run reported, and this is a
    /// statement about two runs. Computing it needs a journal read, so it arrives after the board
    /// does and must be cleared whenever the selection changes — a diff left over from the previous
    /// run would badge the wrong values.
    /// </remarks>
    public ValueDiff ActiveDiff = ValueDiff.None;

    /// <summary>
    /// How long the selected run's steps took against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="ActiveDiff"/> and cleared with it, for the same reasons: it is a statement about two
    /// runs rather than about this one, it arrives after the board because it needs a journal read, and a
    /// timing left over from the previous selection would badge the wrong steps as slow.
    /// </remarks>
    public TimingDiff ActiveTiming = TimingDiff.None;

    /// <summary>The step whose detail is shown, or null when none is.</summary>
    public StepSelection? SelectedStep = null;

    /// <summary>Transport and feed state.</summary>
    public ShellState Shell = new();
}

/// <summary>
/// Which step the detail panel is showing.
/// </summary>
/// <remarks>
/// A stage name and an index rather than the step itself: the graph is replaced wholesale as events
/// arrive, so holding the node would pin a stale copy and the panel would stop updating exactly when
/// the step started doing something.
/// </remarks>
public sealed record StepSelection
{
    /// <summary>Gets the stage containing the step.</summary>
    public required string StageName { get; init; }

    /// <summary>Gets the step's index within its stage.</summary>
    public required int StepId { get; init; }
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

    /// <summary>
    /// The test a re-run was asked for, until its run arrives.
    /// </summary>
    /// <remarks>
    /// Someone who pressed "re-run" is waiting to watch that run, so the run that answers is shown
    /// rather than filed silently behind the one already on screen. Held as the test's name because
    /// that is the only thing known about a run before it exists — the session it will announce
    /// itself under is decided by the test host, not here.
    /// </remarks>
    public string? AwaitedRerun = null;
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

    /// <summary>Gets the assembly or host path the run came from.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>
    /// Gets the journal this run was replayed from, when it came from disk.
    /// </summary>
    /// <remarks>
    /// Carried so a value's file can be found. An imported run's files sit in a folder beside its journal, and
    /// the recorded absolute path in the journal is the sender's — so the journal's own location is what turns
    /// a relative path back into a file that exists here.
    /// </remarks>
    public string? JournalPath { get; init; }

    /// <summary>
    /// Gets the project a reader would name, taken from the end of the path.
    /// </summary>
    /// <remarks>
    /// A full path is unreadable in a list and identical across every run of one suite up to its last
    /// segment. What distinguishes them is the file name, which is what a person calls the project.
    /// The shortening itself is Core's, so a run filed under a name here and named in a log line
    /// cannot come out differently.
    /// </remarks>
    public string Project
        => string.IsNullOrWhiteSpace(ProjectPath) ? "Unknown project" : TestIdentity.ShortNameOf(ProjectPath);

    /// <summary>
    /// Gets the test this run is an execution of.
    /// </summary>
    /// <remarks>
    /// The identity a run groups under: one test run five times is five runs of one test, and a list
    /// that cannot say that is a list of twenty rows with four distinct names in it.
    /// </remarks>
    public string Test => TestNameOf(FullyQualifiedName, Name);

    /// <summary>
    /// The name a run is filed under wherever something is keyed on the test rather than the run.
    /// </summary>
    /// <remarks>
    /// Stated once because two sides compute it and they must agree. The board sets a breakpoint against the
    /// selected run; the transport answers a step of a run that need not be selected, from what that run
    /// announced. Two spellings of this rule is a mark that gets set and never hit.
    /// </remarks>
    public static string TestNameOf(string? fullyQualifiedName, string? runName)
        => string.IsNullOrWhiteSpace(fullyQualifiedName) ? runName ?? string.Empty : fullyQualifiedName;

    /// <summary>
    /// Gets the shortest label that still identifies the run to a reader.
    /// </summary>
    /// <remarks>
    /// For somewhere with no room for a fully qualified name — a notification, a tooltip, a narrow
    /// column. <see cref="Test"/> is the identity and is often eighty characters of namespace; put
    /// that in a notification and the one word that matters wraps off the bottom.
    /// </remarks>
    public string ShortName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            string test = Test;
            int lastDot = test.LastIndexOf('.');

            return lastDot >= 0 && lastDot < test.Length - 1 ? test[(lastDot + 1)..] : test;
        }
    }

    /// <summary>Gets a value indicating whether this run carries enough identity to be re-run.</summary>
    public bool CanRerun { get; init; }

    /// <summary>
    /// Gets where in the code the run was started, when the run recorded it.
    /// </summary>
    /// <remarks>
    /// Captured by Core at compile time from the call site, so it is the line that built the timeline
    /// rather than a guess made from the test's name. Null for a run whose producer predates it, and for
    /// an exported run, which strips it deliberately along with every other local path.
    /// </remarks>
    public SourceLocation? Source { get; init; }

    /// <summary>
    /// Gets how many events the run's recording holds, for a run that was found on disk.
    /// </summary>
    /// <remarks>
    /// Null for a live run, and honestly so: the producer is still writing the sidecar this is counted in,
    /// so any number shown while the run is going would be the number as of whenever the file was last read.
    /// </remarks>
    public long? EventCount { get; init; }

    /// <summary>
    /// Gets the project file the test lives in, when the run reported one.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="ProjectPath"/>, which is for reading and may be an assembly name.
    /// Repeating a run needs something that can be built, and only a project file is that.
    /// </remarks>
    public string? ProjectFilePath { get; init; }

    /// <summary>Gets when the run stopped, when it is known to have stopped.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>
    /// Gets what the run has done so far, counted as its events arrive.
    /// </summary>
    /// <remarks>
    /// Kept for every session, not only the selected one — it is what lets the home page show a
    /// status per run without projecting a board for each. Null for a run that was found on disk and
    /// never opened, which is honestly "not read yet" rather than a run in which nothing failed.
    /// </remarks>
    public RunProgress? Progress { get; init; }

    /// <summary>
    /// Gets how the run stands, in the one word a list needs.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored so it cannot fall out of step with the counts beside it.
    /// </remarks>
    public RunHealth Health
    {
        get
        {
            if (IsWaitingAtBreakpoint)
                return RunHealth.Waiting;

            if (Progress is { } progress && !progress.IsValid)
                return RunHealth.Failed;

            if (IsLive)
                return RunHealth.Running;

            // A run that stopped without saying so is a killed test host. Calling that "passed"
            // because nothing reported a failure would be the most flattering reading, not the
            // truthful one.
            if (!IsFinished)
                return Progress is null ? RunHealth.Unknown : RunHealth.Aborted;

            if (Progress is not { } finished)
                return RunHealth.Unknown;

            return finished.HasAssertions ? RunHealth.Passed : RunHealth.Unproven;
        }
    }

    /// <summary>Gets how long the run took, when both ends are known.</summary>
    public TimeSpan? Duration => FinishedAtUtc is { } finished ? finished - StartedAtUtc : null;
}

/// <summary>
/// A place in the code.
/// </summary>
/// <remarks>
/// The file and the line together rather than two fields on whatever holds them: a file with no line is
/// half an answer in an eight-hundred-line test class, and a line with no file is not an answer at all.
/// </remarks>
public sealed record SourceLocation
{
    /// <summary>Gets the file, as it was on the machine that ran the test.</summary>
    public required string FilePath { get; init; }

    /// <summary>Gets the one-based line, or zero when the producer did not report one.</summary>
    public int Line { get; init; }

    /// <summary>Gets the file's own name, for a label with no room for a path.</summary>
    public string FileName => FilePath[(FilePath.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>
    /// Reads a location out of what a run reported, or null when it reported nothing usable.
    /// </summary>
    /// <remarks>
    /// A line without a file is discarded rather than kept as a location pointing nowhere. The reverse is
    /// allowed: a file with no line opens at the top, which is still the right file.
    /// </remarks>
    public static SourceLocation? From(string? filePath, int line)
        => string.IsNullOrWhiteSpace(filePath)
            ? null
            : new SourceLocation { FilePath = filePath, Line = Math.Max(0, line) };
}

/// <summary>How a run stands, in the one word a list of runs needs.</summary>
public enum RunHealth
{
    /// <summary>The run is on disk but has not been read, so nothing is claimed about it.</summary>
    Unknown,

    /// <summary>The run is producing events.</summary>
    Running,

    /// <summary>A step is held at a breakpoint, waiting to be released.</summary>
    Waiting,

    /// <summary>The run stopped without reaching its finish, which is how a killed host looks.</summary>
    Aborted,

    /// <summary>Something failed: a step, an assertion, or both.</summary>
    Failed,

    /// <summary>Nothing failed, but the run asserted nothing, so it proved nothing.</summary>
    Unproven,

    /// <summary>Every step passed and every assertion held.</summary>
    Passed
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
