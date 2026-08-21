using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Newtonsoft.Json.Linq;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// The bulky half of a run's state, held behind a reference so cloning it costs one pointer.
/// </summary>
/// <remarks>
/// <para>
/// Axiom deep-clones the whole state on every dispatch, and its <c>Scope</c>/<c>Selector</c> narrow
/// what a reducer <em>writes</em>, never what gets copied. A run with a few hundred steps, each with
/// attempts and log entries, modelled as nested value types would put tens of thousands of nodes
/// through that clone on every single event.
/// </para>
/// <para>
/// So the split is deliberate: the small, frequently-addressed scalars stay in <c>record struct</c>
/// slices where Axiom's selectors work naturally, and everything that grows — steps, attempts, logs,
/// payloads — lives here in immutable classes that <c>ReferencePreserveStrategy</c> copies by
/// reference. Reducers replace these references; they must never mutate through them, or the
/// previous snapshot changes underneath anyone still holding it and the change may go unnoticed
/// because the reference did not move.
/// </para>
/// </remarks>
public sealed record RunGraph
{
    /// <summary>An empty graph, used before a run's structure has arrived.</summary>
    public static RunGraph Empty { get; } = new();

    /// <summary>Gets the run's own lifecycle state.</summary>
    public DebugLifecycleState Lifecycle { get; init; } = DebugLifecycleState.Initialized;

    /// <summary>
    /// Gets a value indicating whether the run has stopped producing events.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Lifecycle"/>. The run's outcome arrives as its own
    /// transition, so a graph can already read Complete or Error while more events are still in
    /// flight. This is the flag that says none are coming — and its absence on a graph whose
    /// producer has gone is exactly what marks a run as aborted rather than merely unfinished.
    /// </remarks>
    public bool IsFinished { get; init; }

    /// <summary>Gets the stages in declared execution order.</summary>
    public ImmutableList<StageNode> Stages { get; init; } = ImmutableList<StageNode>.Empty;

    /// <summary>Gets the variables observed so far, keyed by identifier.</summary>
    public ImmutableDictionary<string, ValueNode> Variables { get; init; } = ImmutableDictionary<string, ValueNode>.Empty;

    /// <summary>Gets the artifacts observed so far, keyed by identifier.</summary>
    public ImmutableDictionary<string, ArtifactNode> Artifacts { get; init; } = ImmutableDictionary<string, ArtifactNode>.Empty;

    /// <summary>Gets the assertions recorded across the run.</summary>
    public ImmutableList<AssertionNode> Assertions { get; init; } = ImmutableList<AssertionNode>.Empty;
}

/// <summary>One stage and the steps it contains.</summary>
public sealed record StageNode
{
    /// <summary>Gets the stage name, which is also its identity within a run.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the stage description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the stage's lifecycle state.</summary>
    public DebugLifecycleState Lifecycle { get; init; } = DebugLifecycleState.Initialized;

    /// <summary>Gets the steps in declared order.</summary>
    public ImmutableList<StepNode> Steps { get; init; } = ImmutableList<StepNode>.Empty;
}

/// <summary>One step, its attempts and the values it declared.</summary>
public sealed record StepNode
{
    /// <summary>Gets the step's index within its stage, which is its identity.</summary>
    public required int StepId { get; init; }

    /// <summary>Gets the step's own name, which comes from the step type.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name the test author gave this step with <c>.Name(...)</c>, when they gave one.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Name"/>, and the one a reader recognises: a timeline full of steps
    /// all called "set" or "http" is unreadable, and the label is what tells them apart. Showing
    /// <see cref="Name"/> alone would throw away the only naming the author actually chose.
    /// </remarks>
    public string? Label { get; init; }

    /// <summary>Gets the label if there is one, otherwise the step's own name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    /// <summary>Gets the step description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the policies the step runs under, as the run declared them.
    /// </summary>
    /// <remarks>
    /// New with protocol 4. The retry count and the timeout were previously unreachable — they travelled as an
    /// empty reference wrapper — so this window could not say what a step's policy was even though every run
    /// announced it.
    /// </remarks>
    public StepPolicy Policy { get; init; } = StepPolicy.None;

    /// <summary>
    /// Gets the execution layer the run's planner put this step in.
    /// </summary>
    /// <remarks>
    /// Steps sharing a layer ran concurrently. Reported by Core rather than derived here, because
    /// the plan depends on parallelization mode and shared artifact resources that never reach this
    /// side — so a board drawn from a local guess could show steps side by side that in fact waited
    /// for each other.
    /// </remarks>
    public int LayerIndex { get; init; }

    /// <summary>Gets the step's lifecycle state.</summary>
    public DebugLifecycleState Lifecycle { get; init; } = DebugLifecycleState.Initialized;

    /// <summary>Gets the outcome of the most recent finished attempt, when there is one.</summary>
    public DebugLifecycleState? Outcome { get; init; }

    /// <summary>Gets the declared inputs, used to draw the graph's incoming edges.</summary>
    public ImmutableList<StepIO> Inputs { get; init; } = ImmutableList<StepIO>.Empty;

    /// <summary>Gets the declared outputs, used to draw the graph's outgoing edges.</summary>
    public ImmutableList<StepIO> Outputs { get; init; } = ImmutableList<StepIO>.Empty;

    /// <summary>Gets the attempts made, oldest first.</summary>
    public ImmutableList<AttemptNode> Attempts { get; init; } = ImmutableList<AttemptNode>.Empty;

    /// <summary>Gets a value indicating whether the step is currently paused at a breakpoint.</summary>
    public bool IsWaitingAtBreakpoint { get; init; }

    /// <summary>Gets when the step first started running.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>Gets when the step settled, whether it passed, failed or was skipped.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }

    /// <summary>
    /// Gets how long the step took, once it is over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured across every attempt, from the first time it started to the moment it settled, so a
    /// step that was retried reports the time it actually cost the run rather than the time of
    /// whichever attempt happened to succeed.
    /// </para>
    /// <para>
    /// Absent while the step is still going: a duration that grows as you look at it invites
    /// comparing two numbers that were measured at different moments.
    /// </para>
    /// </remarks>
    public TimeSpan? Duration => StartedAtUtc is { } started && FinishedAtUtc is { } finished && finished >= started
        ? finished - started
        : null;
}

/// <summary>
/// One entry of a step's declared contract.
/// </summary>
/// <remarks>
/// The kind travels with the key because a board draws a variable and an artifact differently — they
/// behave differently, and a reader tracing a value needs to know which of the two they are looking
/// at without opening anything.
/// </remarks>
public sealed record StepIO
{
    /// <summary>Gets the variable or artifact identifier.</summary>
    public required string Key { get; init; }

    /// <summary>Gets whether this is a variable or an artifact.</summary>
    public DebugValueKind Kind { get; init; } = DebugValueKind.Variable;

    /// <summary>
    /// Gets the type the step declared for this entry, when it declared one.
    /// </summary>
    /// <remarks>
    /// A key on its own says what a step reads and not what it expects to find there, which is the half a
    /// reader needs when a value arrives and the step rejects it anyway.
    /// </remarks>
    public string? DeclaredType { get; init; }

    /// <summary>
    /// Gets whether the step insists on this entry being there.
    /// </summary>
    /// <remarks>
    /// Defaulted to true because that is the protocol's own default and the ordinary case: a declared input
    /// is normally one the step cannot run without. An optional entry is the interesting one, which is why
    /// the panel marks only those.
    /// </remarks>
    public bool Required { get; init; } = true;
}

/// <summary>One execution attempt of a step, owning the logs emitted during it.</summary>
public sealed record AttemptNode
{
    /// <summary>Gets the attempt number, counting from one.</summary>
    public required int Attempt { get; init; }

    /// <summary>Gets when the attempt started.</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Gets the log entries emitted during this attempt, in order.</summary>
    public ImmutableList<LogNode> Logs { get; init; } = ImmutableList<LogNode>.Empty;

    /// <summary>
    /// Gets why the attempt failed, when it did.
    /// </summary>
    /// <remarks>
    /// Carried straight from Core, so the step panel can show the framework's own recovery guidance
    /// rather than making the reader parse a stack trace.
    /// </remarks>
    public DebugFailureDetail? Failure { get; init; }
}

/// <summary>
/// What a step's declaration says about how it is allowed to run.
/// </summary>
/// <remarks>
/// Every member is optional because a step need not set any of them, and a policy a test pinned to a variable
/// is named rather than valued: at the moment the run announced its plan, that variable may not have been
/// written yet.
/// </remarks>
public sealed record StepPolicy
{
    /// <summary>A step that declared nothing beyond the defaults.</summary>
    public static StepPolicy None { get; } = new();

    /// <summary>Gets how many times the step may be retried.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Gets the variable the retry count is read from, when a test pinned it to one.</summary>
    public string? MaxRetriesVariable { get; init; }

    /// <summary>Gets how long the step may run for.</summary>
    public TimeSpan? TimeOut { get; init; }

    /// <summary>Gets the variable the timeout is read from, when a test pinned it to one.</summary>
    public string? TimeOutVariable { get; init; }

    /// <summary>Gets the exception types the step may throw without failing the run.</summary>
    public ImmutableList<string> IgnoredExceptions { get; init; } = [];

    /// <summary>Gets whether the step refuses to run beside its neighbours.</summary>
    public bool RunsAlone { get; init; }

    /// <summary>Whether there is anything here worth showing.</summary>
    public bool IsStated
        => MaxRetries is not null
           || MaxRetriesVariable is not null
           || TimeOut is not null
           || TimeOutVariable is not null
           || RunsAlone
           || IgnoredExceptions.Count > 0;

    /// <summary>Compares the ignored types by content, which the generated members would not.</summary>
    public bool Equals(StepPolicy? other)
        => other is not null
           && MaxRetries == other.MaxRetries
           && TimeOut == other.TimeOut
           && RunsAlone == other.RunsAlone
           && string.Equals(MaxRetriesVariable, other.MaxRetriesVariable, StringComparison.Ordinal)
           && string.Equals(TimeOutVariable, other.TimeOutVariable, StringComparison.Ordinal)
           && IgnoredExceptions.SequenceEqual(other.IgnoredExceptions, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MaxRetries, TimeOut, RunsAlone, IgnoredExceptions.Count);
}

/// <summary>
/// A single log entry, as the run stated it.
/// </summary>
/// <remarks>
/// The template and the values that fill it, not a sentence. Core stopped shipping the console's own output on
/// the transport, and keeping the facts here means this window can render them as a line, line them up as
/// columns, or one day group a hundred entries by the template they share.
/// </remarks>
public sealed record LogNode
{
    /// <summary>Gets when the entry was emitted.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the severity.</summary>
    public DebugLogLevel Level { get; init; }

    /// <summary>Gets the name of the log event that emitted this, which a user-defined event keeps.</summary>
    public string EventName { get; init; } = string.Empty;

    /// <summary>Gets the sentence, with its holes unfilled.</summary>
    public string Template { get; init; } = string.Empty;

    /// <summary>Gets the values that fill the holes, typed as they were logged.</summary>
    public ImmutableList<LogFact> Facts { get; init; } = [];

    /// <summary>The entry as one line, for a display with a row to put it in.</summary>
    /// <remarks>
    /// Rendered on demand rather than stored beside the facts. A rendering kept next to the data it came from
    /// is the thing that has to be kept in step with it, which is how the protocol ended up carrying both.
    /// </remarks>
    public string Render()
        => DebugLogTemplate.Render(Template, [.. Facts.Select(fact => new DebugLogField { Name = fact.Name, Value = fact.Value })]);
}

/// <summary>One named value behind a log entry.</summary>
public sealed record LogFact
{
    /// <summary>Gets the name, which is the hole it fills in the template.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the value, typed as it was logged.</summary>
    public required JToken Value { get; init; }

    /// <summary>Gets the value as text, for a display that only wants to print it.</summary>
    public string Text => DebugJson.Text(Value);

    /// <summary>
    /// Compares the value by content.
    /// </summary>
    /// <remarks>
    /// A <see cref="JToken"/> compares by reference, and two tokens deserialized from the same bytes are never
    /// the same instance — so the generated equality would report every redelivered entry as a new one and
    /// re-emit to every binding watching the run.
    /// </remarks>
    public bool Equals(LogFact? other)
        => other is not null
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && JToken.DeepEquals(Value, other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
}

/// <summary>A variable's most recently observed value.</summary>
public sealed record ValueNode
{
    /// <summary>Gets the variable identifier.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the runtime type name that produced the value.</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>Gets the renderer contract key.</summary>
    public string SchemaKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets what the value is, stated as facts rather than as a formatted line.
    /// </summary>
    /// <remarks>
    /// The whole account of the value: its shape, its named facts, its content, and where the rest of it was
    /// written when it did not fit. There used to be a one-line rendering beside this, produced by the
    /// framework and preferred by this window, which is how a panel with room for a table came to show a
    /// sentence.
    /// </remarks>
    public ValueDescription Description { get; init; } = ValueDescription.Empty;
}

/// <summary>An artifact and the versions captured of it.</summary>
public sealed record ArtifactNode
{
    /// <summary>Gets the artifact identifier.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the renderer contract key, such as <c>tf.artifact.sql.row</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on the schema rather than the CLR type name, so the rail can show a SQL
    /// row with its own icon whether it came from the EF-backed or the ADO-backed implementation.
    /// </remarks>
    public string SchemaKey { get; init; } = string.Empty;

    /// <summary>Gets the artifact's lifecycle state, such as Setup or Cleaned.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets what the artifact is, as its own kind described it.
    /// </summary>
    /// <remarks>
    /// An artifact kind that overrides <c>Describe</c> says what it actually is — a row's table and
    /// key, a blob's container and content type. What arrived before was a single line reading
    /// <c>ref=…; state=…; versions=…; latest=…</c>, shown verbatim, semicolons and all.
    /// </remarks>
    public ValueDescription Description { get; init; } = ValueDescription.Empty;

    /// <summary>
    /// Gets the captured version identifiers, oldest first.
    /// </summary>
    /// <remarks>
    /// Core reports the whole history on every update, so this survives a late attach or a replay
    /// that missed the earlier events.
    /// </remarks>
    public ImmutableList<string> Versions { get; init; } = ImmutableList<string>.Empty;
}

/// <summary>One assertion result.</summary>
public sealed record AssertionNode
{
    /// <summary>Gets when the assertion ran, which is also its ordering.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the check's name, such as <c>Be</c> or <c>NotExist</c>.</summary>
    public string AssertionName { get; init; } = string.Empty;

    /// <summary>Gets the check's own parameters, typed as they were passed.</summary>
    public ImmutableList<LogFact> Arguments { get; init; } = [];

    /// <summary>
    /// Gets what was asserted against — a variable or artifact identifier.
    /// </summary>
    /// <remarks>
    /// Without this an assertion list reads as a wall of expected/actual pairs with no indication of
    /// what each one was about.
    /// </remarks>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Gets what kind of thing was asserted against.
    /// </summary>
    /// <remarks>
    /// The name alone is ambiguous by design — a variable and an artifact can share one — and a check
    /// against a step or a bare value has no identifier at all, which reads as a check about nothing until
    /// the kind says otherwise.
    /// </remarks>
    public DebugAssertionTargetKind TargetKind { get; init; } = DebugAssertionTargetKind.Value;

    /// <summary>Gets whether the assertion held.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the value as it actually was, described.</summary>
    public ValueDescription Actual { get; init; } = ValueDescription.Empty;

    /// <summary>Gets the enclosing assertion scope, when the assertion ran inside one.</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>
    /// Compares the arguments by content.
    /// </summary>
    /// <remarks>
    /// The generated equality compares <see cref="ImmutableList{T}"/> by reference, and two lists built from the
    /// same redelivered assertion are never the same instance — so a replayed journal would append every check
    /// it already held. The suppression of an exact redelivery depends on this.
    /// </remarks>
    public bool Equals(AssertionNode? other)
        => other is not null
           && OccurredAtUtc == other.OccurredAtUtc
           && string.Equals(AssertionName, other.AssertionName, StringComparison.Ordinal)
           && string.Equals(Target, other.Target, StringComparison.Ordinal)
           && TargetKind == other.TargetKind
           && string.Equals(Scope, other.Scope, StringComparison.Ordinal)
           && Succeeded == other.Succeeded
           && Equals(Actual, other.Actual)
           && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OccurredAtUtc, AssertionName, Target, Succeeded);

    /// <summary>
    /// The check as it would be written in a test, for a row that has to show it as one line.
    /// </summary>
    /// <remarks>
    /// Rendered here rather than carried: the run states the name and the arguments, and what used to travel
    /// beside them was this sentence, a second copy of the expectation and a third describing the difference.
    /// </remarks>
    public string Render()
        => Arguments.Count == 0
            ? AssertionName
            : $"{AssertionName}({string.Join(", ", Arguments.Select(argument => argument.Text))})";

    /// <summary>
    /// What the check was about, for a row that puts the subject before the check.
    /// </summary>
    /// <remarks>
    /// The kind is said alongside the name because a variable and an artifact are allowed to share one. A
    /// check against a bare value or a step has no identifier at all, and for those the kind is the whole
    /// answer — the alternative, which is what this replaced, was a row that began with a separator and
    /// nothing in front of it.
    /// </remarks>
    public string Subject
    {
        get
        {
            string kind = TargetKind switch
            {
                DebugAssertionTargetKind.StepList => "steps",
                _ => TargetKind.ToString().ToLowerInvariant()
            };

            return string.IsNullOrWhiteSpace(Target) ? kind : $"{kind} {Target}";
        }
    }
}
