using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// What a run amounts to, counted up.
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the graph rather than state of its own: it is entirely derived, so storing it
/// would mean keeping it in step with every event, and getting that wrong shows up as a summary
/// that disagrees with the board beside it.
/// </para>
/// <para>
/// Computed in one place because two surfaces ask the same question. The summary page answers "is
/// this run healthy" as a page, and the verdict on the board answers it as a node; they must not be
/// able to give different answers.
/// </para>
/// </remarks>
public sealed record RunTally
{
    /// <summary>The tally of a run with nothing in it.</summary>
    public static RunTally Empty { get; } = new() { Failures = ImmutableList<StepFailure>.Empty };

    /// <summary>Gets the run's own lifecycle state.</summary>
    public DebugLifecycleState Lifecycle { get; init; } = DebugLifecycleState.Initialized;

    /// <summary>Gets a value indicating whether the run stopped producing events.</summary>
    public bool IsFinished { get; init; }

    /// <summary>Gets how many steps the run declared.</summary>
    public int Steps { get; init; }

    /// <summary>Gets how many steps completed.</summary>
    public int Complete { get; init; }

    /// <summary>Gets how many steps failed or timed out.</summary>
    public int Failed { get; init; }

    /// <summary>Gets how many steps were skipped.</summary>
    public int Skipped { get; init; }

    /// <summary>Gets how many steps have not finished.</summary>
    public int Outstanding => Math.Max(0, Steps - Complete - Failed - Skipped);

    /// <summary>Gets how many steps needed more than one attempt.</summary>
    public int Retried { get; init; }

    /// <summary>Gets how many assertions held.</summary>
    public int AssertionsPassed { get; init; }

    /// <summary>Gets how many assertions did not hold.</summary>
    public int AssertionsFailed { get; init; }

    /// <summary>Gets how many artifacts the run registered.</summary>
    public int Artifacts { get; init; }

    /// <summary>Gets how many variables the run reported.</summary>
    public int Variables { get; init; }

    /// <summary>Gets every step that failed, in run order.</summary>
    public required ImmutableList<StepFailure> Failures { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run can be trusted.
    /// </summary>
    /// <remarks>
    /// Both halves matter, and they are different questions: nothing threw, <em>and</em> everything
    /// asserted held. A run whose steps all pass while an assertion fails is not a passing run, and
    /// a run with no assertions at all has proved nothing — which the page says in words rather than
    /// dressing it up as a pass.
    /// </remarks>
    public bool IsValid => Failed == 0 && AssertionsFailed == 0;

    /// <summary>Gets a value indicating whether the run asserted anything at all.</summary>
    public bool HasAssertions => AssertionsPassed + AssertionsFailed > 0;

    /// <summary>
    /// Counts up a run.
    /// </summary>
    public static RunTally Of(RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        ImmutableList<StepFailure>.Builder failures = ImmutableList.CreateBuilder<StepFailure>();

        int steps = 0, complete = 0, failed = 0, skipped = 0, retried = 0;

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                steps++;

                if (step.Attempts.Count > 1)
                    retried++;

                switch (step.Lifecycle)
                {
                    case DebugLifecycleState.Complete:
                        complete++;
                        break;

                    case DebugLifecycleState.Error:
                    case DebugLifecycleState.Timeout:
                        failed++;
                        failures.Add(new StepFailure
                        {
                            StageName = stage.Name,
                            StepId = step.StepId,
                            DisplayName = step.DisplayName,

                            // The last attempt's failure, not the first: a step that failed, retried
                            // and then succeeded is not broken, and the surviving failure is the one
                            // that stopped it.
                            Detail = step.Attempts.LastOrDefault()?.Failure
                        });
                        break;

                    case DebugLifecycleState.Skipped:
                        skipped++;
                        break;
                }
            }
        }

        return new RunTally
        {
            Lifecycle = graph.Lifecycle,
            IsFinished = graph.IsFinished,
            Steps = steps,
            Complete = complete,
            Failed = failed,
            Skipped = skipped,
            Retried = retried,
            AssertionsPassed = graph.Assertions.Count(assertion => assertion.Succeeded),
            AssertionsFailed = graph.Assertions.Count(assertion => !assertion.Succeeded),
            Artifacts = graph.Artifacts.Count,
            Variables = graph.Variables.Count,
            Failures = failures.ToImmutable()
        };
    }

    /// <summary>Compares two tallies by their contents.</summary>
    /// <remarks>
    /// A record compares its collection member by reference, so two identical tallies would come out
    /// unequal and anything watching for a change would fire on every event.
    /// </remarks>
    public bool Equals(RunTally? other)
        => other is not null
           && Lifecycle == other.Lifecycle
           && IsFinished == other.IsFinished
           && Steps == other.Steps
           && Complete == other.Complete
           && Failed == other.Failed
           && Skipped == other.Skipped
           && Retried == other.Retried
           && AssertionsPassed == other.AssertionsPassed
           && AssertionsFailed == other.AssertionsFailed
           && Artifacts == other.Artifacts
           && Variables == other.Variables
           && Failures.SequenceEqual(other.Failures);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Lifecycle, IsFinished, Steps, Complete, Failed, AssertionsFailed, Failures.Count);
}

/// <summary>One step that did not pass, and why.</summary>
public sealed record StepFailure
{
    /// <summary>Gets the stage the step belongs to.</summary>
    public required string StageName { get; init; }

    /// <summary>Gets the step's index within its stage.</summary>
    public required int StepId { get; init; }

    /// <summary>Gets the step's display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the framework's account of the failure, when it gave one.</summary>
    public DebugFailureDetail? Detail { get; init; }
}
