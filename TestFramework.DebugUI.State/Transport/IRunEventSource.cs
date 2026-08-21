using System;
using System.Collections.Generic;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// A source of debug events, whether live or recorded.
/// </summary>
/// <remarks>
/// One abstraction over the pipe and the journal because they carry the same envelopes. A replayed
/// run therefore drives exactly the code a live run drives — there is no second projection to keep
/// in step, and no class of bug that only appears when reading from disk.
/// </remarks>
public interface IRunEventSource
{
    /// <summary>Raised for each envelope the source produces, in order.</summary>
    event Action<DebugEnvelope>? EnvelopeReceived;

    /// <summary>
    /// Raised when something happens to the transport itself that the user should know about.
    /// </summary>
    /// <remarks>
    /// Only ever the tool failing, never a test failing. A step throwing is the thing this
    /// application exists to display, and reporting it here as well would make the surface that
    /// means "the debugger is broken" fire on every run that does its job.
    /// </remarks>
    event Action<FeedEntry>? Notice;

    /// <summary>Begins producing events.</summary>
    void Start();
}

/// <summary>
/// A step asking whether it should be held, and enough about its run to answer.
/// </summary>
/// <remarks>
/// The request names a stage and a step index, which together do not identify anything: stage names are
/// conventional and an index is a number. The test is what makes the question answerable, and it is resolved
/// here — on the reader thread, from what the run announced — rather than read out of the store, which lags
/// behind the pipe by however long the last dispatch took.
/// </remarks>
public sealed record BreakpointQuestion
{
    /// <summary>Gets the step that is waiting for an answer.</summary>
    public required PipeBreakpointHitRequestSignal Request { get; init; }

    /// <summary>Gets the test the run is an execution of, or null when the run did not say.</summary>
    public string? Test { get; init; }
}

/// <summary>
/// A step of some attached run that has just ended badly, and is not going to be retried.
/// </summary>
/// <remarks>
/// Raised for every session rather than the selected one: catching the run that broke while you were
/// looking at another is the whole reason anyone arms break-on-failure.
/// </remarks>
public sealed record StepFailureNotice
{
    /// <summary>Gets the run the step belongs to.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the stage the step belongs to.</summary>
    public required string Stage { get; init; }

    /// <summary>Gets the step's index within its stage.</summary>
    public required int StepId { get; init; }
}

/// <summary>
/// A run the UI can open, as listed for the run picker.
/// </summary>
public sealed record AvailableRun
{
    /// <summary>Gets the run's session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the run's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run reached its finish signal.
    /// </summary>
    /// <remarks>
    /// False on a recorded run means the producer went away without closing it — a killed test host.
    /// The picker shows that as aborted rather than hiding it, which the previous UI did.
    /// </remarks>
    public bool IsFinished { get; init; }

    /// <summary>Gets when the run stopped, when the sidecar recorded it stopping.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>Gets the fully qualified test name, when the run could be identified.</summary>
    public string? FullyQualifiedName { get; init; }

    /// <summary>Gets the assembly or host path the run came from.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Gets the project file the test lives in, when the run recorded one.</summary>
    /// <remarks>
    /// Carried separately from <see cref="ProjectPath"/>, which is for reading and may be a bare
    /// assembly name. Repeating a run needs something that can be built.
    /// </remarks>
    public string? ProjectFilePath { get; init; }

    /// <summary>Gets whether the recorded identity is complete enough to run the test again.</summary>
    /// <remarks>
    /// Read from the sidecar rather than from the journal, which is the whole reason the sidecar
    /// carries the identity: the picker can offer re-run for a run nobody has opened.
    /// </remarks>
    public bool CanRerun { get; init; }

    /// <summary>Gets where in the code the run was started, when the sidecar recorded it.</summary>
    /// <remarks>
    /// From the sidecar for the same reason as <see cref="CanRerun"/>: opening the test in an editor is
    /// offered for a run in the list, before anyone has read its journal.
    /// </remarks>
    public SourceLocation? Source { get; init; }

    /// <summary>
    /// Gets how many events the recording holds, as the sidecar counted them.
    /// </summary>
    /// <remarks>
    /// Counted by the producer while it wrote, so it is free here: the whole point of the sidecar is that
    /// listing runs never parses one.
    /// </remarks>
    public long? EventCount { get; init; }

    /// <summary>Gets the path of the journal holding the run's events.</summary>
    public required string JournalPath { get; init; }
}
