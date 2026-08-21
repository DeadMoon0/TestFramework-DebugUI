using System;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Runs;

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
