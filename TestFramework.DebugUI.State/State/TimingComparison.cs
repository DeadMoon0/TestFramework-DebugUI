using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State;

/// <summary>How one step's time stands against the same step in an earlier run.</summary>
public enum StepTimingChange
{
    /// <summary>Near enough the same to be worth nothing.</summary>
    Unchanged,

    /// <summary>Took materially longer than it did.</summary>
    Slower,

    /// <summary>Took materially less time than it did.</summary>
    Faster,

    /// <summary>Not in the earlier run at all, so there is nothing to compare it to.</summary>
    Added,

    /// <summary>In the earlier run and not in this one.</summary>
    Removed
}

/// <summary>
/// How long one step took, beside how long it used to take.
/// </summary>
public sealed record StepTiming
{
    /// <summary>Gets the stage the step belongs to.</summary>
    public required string StageName { get; init; }

    /// <summary>Gets the step's index within its stage.</summary>
    public required int StepId { get; init; }

    /// <summary>Gets the step's name, as a reader recognises it.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets what it took this time, or null when it has not finished.</summary>
    public TimeSpan? Now { get; init; }

    /// <summary>Gets what it took in the baseline run, or null when it was not in it.</summary>
    public TimeSpan? Then { get; init; }

    /// <summary>Gets how it stands.</summary>
    public StepTimingChange Change { get; init; }

    /// <summary>Gets how much longer it took, which is negative when it was quicker.</summary>
    public TimeSpan Delta => (Now ?? TimeSpan.Zero) - (Then ?? TimeSpan.Zero);

    /// <summary>
    /// Gets how many times as long it took, when both times are known and the old one was not zero.
    /// </summary>
    /// <remarks>
    /// Null rather than infinity for a step that used to take no measurable time. "Three milliseconds became
    /// four hundred" is a fact worth stating; "it is now infinitely slower" is not a statement about anything.
    /// </remarks>
    public double? Ratio => Now is { } now && Then is { TotalMilliseconds: > 0 } then
        ? now.TotalMilliseconds / then.TotalMilliseconds
        : null;

    /// <summary>Gets whether this is worth putting in front of a reader.</summary>
    public bool IsInteresting => Change is StepTimingChange.Slower or StepTimingChange.Faster;
}

/// <summary>
/// A run's step timings set beside the last run of the same test that passed.
/// </summary>
/// <remarks>
/// A separate answer from <see cref="ValueDiff"/> and computed from the same baseline in the same pass, so
/// the journal behind it is read once. What changed and what got slower are two different questions about
/// the same two runs, and a reader asks them at different moments.
/// </remarks>
public sealed record TimingDiff
{
    /// <summary>Nothing compared yet.</summary>
    public static TimingDiff None { get; } = new();

    /// <summary>Which run the comparison is against, when there is one.</summary>
    public RunBaseline? Baseline { get; init; }

    /// <summary>Why there is no comparison, when there is none.</summary>
    public string? Unavailable { get; init; }

    /// <summary>Every step, in run order.</summary>
    public ImmutableList<StepTiming> Steps { get; init; } = ImmutableList<StepTiming>.Empty;

    /// <summary>Gets how long this run took end to end, when it has finished.</summary>
    public TimeSpan? Now { get; init; }

    /// <summary>Gets how long the baseline run took end to end.</summary>
    public TimeSpan? Then { get; init; }

    /// <summary>Whether a comparison was made at all.</summary>
    public bool HasBaseline => Baseline is not null;

    /// <summary>How much longer the whole run took, which is negative when it was quicker.</summary>
    public TimeSpan Delta => (Now ?? TimeSpan.Zero) - (Then ?? TimeSpan.Zero);

    /// <summary>How many times as long the whole run took, when both ends are known.</summary>
    public double? Ratio => Now is { } now && Then is { TotalMilliseconds: > 0 } then
        ? now.TotalMilliseconds / then.TotalMilliseconds
        : null;

    /// <summary>How many steps took materially longer.</summary>
    public int SlowerCount => Steps.Count(step => step.Change == StepTimingChange.Slower);

    /// <summary>How many steps took materially less time.</summary>
    public int FasterCount => Steps.Count(step => step.Change == StepTimingChange.Faster);

    /// <summary>Whether the run as a whole moved enough to be worth remarking on.</summary>
    public bool RunMovedMaterially
        => Now is { } now && Then is { } then && TimingComparison.IsMaterial(now - then, now, then);

    /// <summary>How one step stands, for the board to colour its time.</summary>
    public StepTiming? ForStep(string stageName, int stepId)
        => Steps.FirstOrDefault(step =>
            step.StepId == stepId && string.Equals(step.StageName, stageName, StringComparison.Ordinal));

    /// <summary>
    /// The steps that moved most, largest first, for a page with room for a few of them.
    /// </summary>
    /// <remarks>
    /// Ordered by how much time actually moved rather than by ratio. A step that went from four seconds to
    /// twelve is where a slow run went; one that went from two milliseconds to eight is six times worse and
    /// costs nobody anything.
    /// </remarks>
    public ImmutableList<StepTiming> BiggestMovers(int count)
        =>
        [
            .. Steps
                .Where(step => step.IsInteresting)
                .OrderByDescending(step => Math.Abs(step.Delta.Ticks))
                .Take(count)
        ];
}

/// <summary>
/// Decides which steps of a run actually got slower, and by how much.
/// </summary>
/// <remarks>
/// Pure and separate from anything that reads a journal, so the rules can be argued with in tests rather
/// than by running two timelines and hoping.
/// </remarks>
public static class TimingComparison
{
    /// <summary>
    /// How much clock time has to move before it is worth mentioning.
    /// </summary>
    /// <remarks>
    /// Without a floor, every step in the run is a headline: a step that went from two milliseconds to six
    /// is three times slower and has cost the run four milliseconds. The floor is what keeps the answer to
    /// "why was this run slow" about the run being slow.
    /// </remarks>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How far a step may drift proportionally before it counts as a change.
    /// </summary>
    /// <remarks>
    /// Without a proportional test, the floor alone flags a sixty-second step for moving a tenth of a
    /// second. Both conditions have to hold, because each one on its own is wrong in the other's direction —
    /// and integration tests talk to real things, so a quarter is ordinary noise rather than a regression.
    /// </remarks>
    public const double Tolerance = 0.25;

    /// <summary>
    /// Whether a movement is big enough, both absolutely and proportionally, to report.
    /// </summary>
    public static bool IsMaterial(TimeSpan delta, TimeSpan now, TimeSpan then)
    {
        if (delta.Duration() < Floor)
            return false;

        // A step that previously took no measurable time has no proportion to drift by, so the floor is the
        // whole test: it used to be instant and now it is not.
        if (then.TotalMilliseconds <= 0)
            return true;

        return Math.Abs((now.TotalMilliseconds / then.TotalMilliseconds) - 1) >= Tolerance;
    }

    /// <summary>
    /// Takes every step's duration out of a run, keyed so it can be matched up again.
    /// </summary>
    /// <remarks>
    /// The name travels with the key. A stage and an index identify a position in a plan, not a step: insert
    /// one step and every index after it now names something else, and comparing those would report the
    /// whole tail of the run as having changed speed.
    /// </remarks>
    public static ImmutableDictionary<string, StepDuration> DurationsOf(RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        ImmutableDictionary<string, StepDuration>.Builder durations =
            ImmutableDictionary.CreateBuilder<string, StepDuration>(StringComparer.Ordinal);

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                durations[KeyOf(stage.Name, step.StepId)] = new StepDuration
                {
                    StageName = stage.Name,
                    StepId = step.StepId,
                    DisplayName = step.DisplayName,
                    Took = step.Duration
                };
            }
        }

        return durations.ToImmutable();
    }

    /// <summary>
    /// The key a step is matched on across two runs.
    /// </summary>
    /// <remarks>
    /// Joined with a separator that cannot appear in a stage name. Concatenated, stage "A" step 12 and stage
    /// "A1" step 2 would be the same key, and the two steps would be timed against each other.
    /// </remarks>
    public static string KeyOf(string stageName, int stepId)
        => stageName + "\u001F" + stepId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Compares a run's step timings against a baseline's.
    /// </summary>
    /// <param name="current">The run on screen.</param>
    /// <param name="baseline">The run it is being compared against.</param>
    /// <param name="ranFor">How long the current run took end to end, when it is over.</param>
    /// <param name="baselineRanFor">How long the baseline run took end to end.</param>
    public static TimingDiff Compare(
        RunGraph current,
        RunBaseline baseline,
        TimeSpan? ranFor,
        TimeSpan? baselineRanFor)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);

        ImmutableDictionary<string, StepDuration> now = DurationsOf(current);

        ImmutableList<StepTiming>.Builder steps = ImmutableList.CreateBuilder<StepTiming>();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, StepDuration> pair in now)
        {
            seen.Add(pair.Key);

            StepDuration step = pair.Value;
            baseline.Steps.TryGetValue(pair.Key, out StepDuration? before);

            // Matched only when the name agrees as well as the position. A different name at the same index
            // is a different step, and calling it the same one would report a plan change as a slowdown.
            bool matched = before is not null
                           && string.Equals(before.DisplayName, step.DisplayName, StringComparison.Ordinal);

            steps.Add(new StepTiming
            {
                StageName = step.StageName,
                StepId = step.StepId,
                DisplayName = step.DisplayName,
                Now = step.Took,
                Then = matched ? before!.Took : null,
                Change = Classify(step.Took, matched ? before!.Took : null)
            });
        }

        // Steps the baseline had and this run does not. Worth listing rather than dropping: a run that got
        // faster by not doing something is a run whose plan changed, which is a different finding.
        foreach (KeyValuePair<string, StepDuration> pair in baseline.Steps)
        {
            if (seen.Contains(pair.Key))
                continue;

            steps.Add(new StepTiming
            {
                StageName = pair.Value.StageName,
                StepId = pair.Value.StepId,
                DisplayName = pair.Value.DisplayName,
                Then = pair.Value.Took,
                Change = StepTimingChange.Removed
            });
        }

        return new TimingDiff
        {
            Baseline = baseline,
            Now = ranFor,
            Then = baselineRanFor,
            Steps =
            [
                .. steps
                    .OrderBy(step => step.StageName, StringComparer.Ordinal)
                    .ThenBy(step => step.StepId)
            ]
        };
    }

    /// <summary>States why no comparison could be made.</summary>
    public static TimingDiff Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new TimingDiff { Unavailable = reason };
    }

    /// <summary>
    /// How a step's two durations stand.
    /// </summary>
    /// <remarks>
    /// A step still running, or one that never finished, is <see cref="StepTimingChange.Unchanged"/> rather
    /// than anything more definite. There is no duration to compare, and guessing from a partial one would
    /// mean a step reads as faster right up to the moment it turns out to be slower.
    /// </remarks>
    private static StepTimingChange Classify(TimeSpan? now, TimeSpan? then)
    {
        // Unchanged rather than Removed. This step is in the current run — that is how it got here — it just
        // has no time yet, and calling it removed would report every step of a live run as having vanished.
        // A step genuinely absent from this run is classified where the missing keys are walked.
        if (now is null)
            return StepTimingChange.Unchanged;

        if (then is null)
            return StepTimingChange.Added;

        TimeSpan delta = now.Value - then.Value;

        if (!IsMaterial(delta, now.Value, then.Value))
            return StepTimingChange.Unchanged;

        return delta > TimeSpan.Zero ? StepTimingChange.Slower : StepTimingChange.Faster;
    }
}

/// <summary>What one step of a run cost, with enough about it to be matched in another run.</summary>
public sealed record StepDuration
{
    /// <summary>Gets the stage the step belongs to.</summary>
    public required string StageName { get; init; }

    /// <summary>Gets the step's index within its stage.</summary>
    public required int StepId { get; init; }

    /// <summary>Gets the step's name, which is half of what makes the match trustworthy.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets how long it took, or null when it did not finish.</summary>
    public TimeSpan? Took { get; init; }
}
