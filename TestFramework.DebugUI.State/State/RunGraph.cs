using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

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

/// <summary>A single log line.</summary>
public sealed record LogNode
{
    /// <summary>Gets when the entry was emitted.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the severity.</summary>
    public DebugLogLevel Level { get; init; }

    /// <summary>Gets the rendered message.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>A variable's most recently observed value.</summary>
public sealed record ValueNode
{
    /// <summary>Gets the variable identifier.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the text a consumer displays for the value.</summary>
    public string DisplayText { get; init; } = string.Empty;

    /// <summary>Gets the runtime type name that produced the value.</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>Gets the renderer contract key.</summary>
    public string SchemaKey { get; init; } = string.Empty;
}

/// <summary>An artifact and the versions captured of it.</summary>
public sealed record ArtifactNode
{
    /// <summary>Gets the artifact identifier.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the text a consumer displays for the artifact.</summary>
    public string DisplayText { get; init; } = string.Empty;

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

    /// <summary>Gets the assertion's display form, such as <c>Be("Ada")</c>.</summary>
    public string Display { get; init; } = string.Empty;

    /// <summary>
    /// Gets what was asserted against — a variable or artifact identifier.
    /// </summary>
    /// <remarks>
    /// Without this an assertion list reads as a wall of expected/actual pairs with no indication of
    /// what each one was about.
    /// </remarks>
    public string Target { get; init; } = string.Empty;

    /// <summary>Gets whether the assertion held.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the expected value, as text.</summary>
    public string Expected { get; init; } = string.Empty;

    /// <summary>Gets the actual value, as text.</summary>
    public string Actual { get; init; } = string.Empty;

    /// <summary>
    /// Gets the framework's explanation of the failure, when it failed.
    /// </summary>
    /// <remarks>
    /// Expected and actual say what differed; this says why that counts as a failure, which is not
    /// always obvious from the two values alone.
    /// </remarks>
    public string FailureReason { get; init; } = string.Empty;

    /// <summary>Gets the enclosing assertion scope, when the assertion ran inside one.</summary>
    public string Scope { get; init; } = string.Empty;
}
