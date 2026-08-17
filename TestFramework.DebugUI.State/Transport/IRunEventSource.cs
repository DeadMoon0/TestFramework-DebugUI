using System;
using System.Collections.Generic;
using TestFramework.Core.Debugger;

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

    /// <summary>Gets the path of the journal holding the run's events.</summary>
    public required string JournalPath { get; init; }
}
