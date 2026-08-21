using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// How far a run has got, counted from its events rather than from its board.
/// </summary>
/// <remarks>
/// <para>
/// The cheap sibling of <see cref="RunTally"/>. A tally is a function of the graph, and only the
/// selected run has one; this is kept for <em>every</em> session so the home page can say what each
/// run is doing without projecting twenty boards nobody is looking at.
/// </para>
/// <para>
/// Two paths counting the same events could drift apart, which would show as a home card disagreeing
/// with the board it opens. They are pinned together by a test that runs a real timeline through the
/// store and asserts the two agree.
/// </para>
/// </remarks>
public sealed record RunProgress
{
    /// <summary>A run that has not reported anything yet.</summary>
    public static RunProgress Empty { get; } = new();

    /// <summary>Gets how many steps the run declared when it announced itself.</summary>
    public int Steps { get; init; }

    /// <summary>
    /// Gets the last state each step was seen in.
    /// </summary>
    /// <remarks>
    /// A map rather than a set of counters because a step is not done moving when it first fails: a
    /// retrying step passes through <see cref="DebugLifecycleState.Error"/> on its way to completing,
    /// and counters incremented per transition would report it as both failed and complete.
    /// </remarks>
    public ImmutableDictionary<StepKey, DebugLifecycleState> StepStates { get; init; }
        = ImmutableDictionary<StepKey, DebugLifecycleState>.Empty;

    /// <summary>Gets how many assertions held.</summary>
    public int AssertionsPassed { get; init; }

    /// <summary>Gets how many assertions did not hold.</summary>
    public int AssertionsFailed { get; init; }

    /// <summary>Gets how many steps completed.</summary>
    public int Complete => StepStates.Count(state => state.Value == DebugLifecycleState.Complete);

    /// <summary>Gets how many steps failed or timed out.</summary>
    public int Failed => StepStates.Count(state =>
        state.Value is DebugLifecycleState.Error or DebugLifecycleState.Timeout);

    /// <summary>Gets how many steps were skipped.</summary>
    public int Skipped => StepStates.Count(state => state.Value == DebugLifecycleState.Skipped);

    /// <summary>Gets how many steps have not finished.</summary>
    public int Outstanding => Math.Max(0, Steps - Complete - Failed - Skipped);

    /// <summary>Gets a value indicating whether the run asserted anything at all.</summary>
    public bool HasAssertions => AssertionsPassed + AssertionsFailed > 0;

    /// <summary>
    /// Gets a value indicating whether nothing has gone wrong so far.
    /// </summary>
    /// <remarks>
    /// Both halves matter: a run whose steps all pass while an assertion fails is not a passing run.
    /// Whether it also <em>proved</em> anything is a separate question, which <see cref="HasAssertions"/>
    /// answers and the home card says out loud.
    /// </remarks>
    public bool IsValid => Failed == 0 && AssertionsFailed == 0;

    /// <summary>Gets a value indicating whether the run has reported anything worth counting.</summary>
    public bool IsKnown => Steps > 0 || StepStates.Count > 0;

    /// <summary>
    /// Records the state a step was last seen in.
    /// </summary>
    public RunProgress WithStep(string stage, int stepId, DebugLifecycleState state)
    {
        StepKey key = new(stage ?? string.Empty, stepId);

        return StepStates.TryGetValue(key, out DebugLifecycleState known) && known == state
            ? this
            : this with { StepStates = StepStates.SetItem(key, state) };
    }

    /// <summary>Records an assertion result.</summary>
    public RunProgress WithAssertion(bool succeeded)
        => succeeded
            ? this with { AssertionsPassed = AssertionsPassed + 1 }
            : this with { AssertionsFailed = AssertionsFailed + 1 };
}

/// <summary>Which step a progress entry belongs to.</summary>
/// <remarks>
/// The stage is part of the key because a step id is an index within its stage, so ids repeat.
/// </remarks>
public readonly record struct StepKey(string Stage, int StepId);
