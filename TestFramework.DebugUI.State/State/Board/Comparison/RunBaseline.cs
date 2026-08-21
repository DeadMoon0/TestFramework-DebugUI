using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// The values an earlier run produced, held as the thing a later run is compared against.
/// </summary>
public sealed record RunBaseline
{
    /// <summary>The run these values came from.</summary>
    public required string SessionId { get; init; }

    /// <summary>When that run started, which is how a reader recognises which run it was.</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    public ImmutableDictionary<string, ValueDescription> Variables { get; init; }
        = ImmutableDictionary<string, ValueDescription>.Empty;

    public ImmutableDictionary<string, ValueDescription> Artifacts { get; init; }
        = ImmutableDictionary<string, ValueDescription>.Empty;

    /// <summary>
    /// How long each of that run's steps took, keyed by <see cref="TimingComparison.KeyOf"/>.
    /// </summary>
    /// <remarks>
    /// Taken from the same replay as the values, because the expensive part of a baseline is reading and
    /// projecting the journal and there is no reason to do it twice to answer two questions about one run.
    /// </remarks>
    public ImmutableDictionary<string, StepDuration> Steps { get; init; }
        = ImmutableDictionary<string, StepDuration>.Empty;

    /// <summary>
    /// Gets how long that run took end to end, when both of its ends were recorded.
    /// </summary>
    /// <remarks>
    /// The run's own wall clock rather than the sum of its steps: steps run in parallel layers, so adding
    /// them up reports a number the run never took.
    /// </remarks>
    public TimeSpan? RanFor { get; init; }
}

/// <summary>
/// A run's values set beside the last run of the same test that passed.
/// </summary>
public sealed record ValueDiff
{
    /// <summary>Nothing compared yet.</summary>
    public static ValueDiff None { get; } = new();

    /// <summary>Which run the comparison is against, when there is one.</summary>
    public RunBaseline? Baseline { get; init; }

    /// <summary>Why there is no comparison, when there is none.</summary>
    /// <remarks>
    /// Always populated when <see cref="Baseline"/> is null and a comparison was attempted. "No
    /// baseline" and "no baseline because this test has only ever run once" are different answers, and
    /// the second is the one that stops a reader wondering whether the feature is broken.
    /// </remarks>
    public string? Unavailable { get; init; }

    public ImmutableList<ValueChange> Variables { get; init; } = ImmutableList<ValueChange>.Empty;

    public ImmutableList<ValueChange> Artifacts { get; init; } = ImmutableList<ValueChange>.Empty;

    /// <summary>Whether a comparison was made at all.</summary>
    public bool HasBaseline => Baseline is not null;

    /// <summary>How many values differ, which is the number a header shows.</summary>
    public int ChangedCount
        => Variables.Count(change => change.IsInteresting) + Artifacts.Count(change => change.IsInteresting);

    /// <summary>The standing of one variable, for the rail to badge it.</summary>
    public ValueChangeKind? ForVariable(string key) => Find(Variables, key);

    /// <summary>The standing of one artifact, for the rail to badge it.</summary>
    public ValueChangeKind? ForArtifact(string key) => Find(Artifacts, key);

    /// <summary>The full comparison of one variable, for the inspector to diff it.</summary>
    public ValueChange? ChangeForVariable(string key) => Locate(Variables, key);

    /// <summary>The full comparison of one artifact, for the inspector to diff it.</summary>
    public ValueChange? ChangeForArtifact(string key) => Locate(Artifacts, key);

    private static ValueChangeKind? Find(ImmutableList<ValueChange> changes, string key)
        => Locate(changes, key)?.Change;

    private static ValueChange? Locate(ImmutableList<ValueChange> changes, string key)
        => changes.FirstOrDefault(change => string.Equals(change.Key, key, StringComparison.Ordinal));
}

/// <summary>
/// Everything one run has to say about itself next to an earlier one.
/// </summary>
/// <remarks>
/// The two questions travel together because the expensive half of answering either is the same: find the
/// last run of this test that passed, read its journal, project it. Splitting them into two calls would read
/// the same file twice to compare the same pair of runs.
/// </remarks>
public sealed record RunComparison
{
    /// <summary>Nothing compared yet.</summary>
    public static RunComparison None { get; } = new();

    /// <summary>What the run's values did.</summary>
    public ValueDiff Values { get; init; } = ValueDiff.None;

    /// <summary>What the run's clock did.</summary>
    public TimingDiff Timing { get; init; } = TimingDiff.None;

    /// <summary>States why neither comparison could be made.</summary>
    public static RunComparison Unavailable(string reason) => new()
    {
        Values = RunBaselineSelector.Unavailable(reason),
        Timing = TimingComparison.Unavailable(reason)
    };
}

/// <summary>
/// Chooses which earlier run a run should be compared against, and folds it into a baseline.
/// </summary>
/// <remarks>
/// <para>
/// The baseline is the most recent earlier run of the <em>same test</em> that <em>passed</em>. Both
/// halves are deliberate. Comparing across tests would diff unrelated values; comparing against a run
/// that failed answers "what changed since it was last broken", when the question a reader actually
/// has is "what changed since it worked".
/// </para>
/// <para>
/// Everything here is pure. Reading a journal from disk is the caller's job, because that is the part
/// that can be slow, can fail, and has to be cancellable — and keeping it out means the selection
/// rules can be tested without writing a single file.
/// </para>
/// </remarks>
public static class RunBaselineSelector
{
    /// <summary>
    /// How many earlier runs are worth replaying before giving up.
    /// </summary>
    /// <remarks>
    /// A bound rather than the whole history: replaying a run costs a file read and a full projection,
    /// and a test whose last dozen runs all failed is a test whose baseline is not interesting enough
    /// to read a hundred journals for. The cap is reported when it is hit rather than looking like
    /// "no baseline exists".
    /// </remarks>
    public const int MaximumCandidates = 8;

    /// <summary>
    /// The earlier runs of the same test, newest first.
    /// </summary>
    public static ImmutableList<RunSummary> CandidatesFor(IEnumerable<RunSummary> runs, RunSummary current)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(current);

        if (string.IsNullOrWhiteSpace(current.FullyQualifiedName))
            return [];

        return
        [
            .. runs
                .Where(run => !string.Equals(run.SessionId, current.SessionId, StringComparison.Ordinal))

                // Same test, or there is nothing meaningful to compare.
                .Where(run => string.Equals(run.FullyQualifiedName, current.FullyQualifiedName, StringComparison.Ordinal))

                // Strictly earlier. A run still producing events has no settled values to be a
                // baseline, and one that started later is not what "previously" means.
                .Where(run => !run.IsLive && run.StartedAtUtc < current.StartedAtUtc)
                .OrderByDescending(run => run.StartedAtUtc)

                // Session id as a tiebreak so two runs stamped the same instant still order the same
                // way every time. A baseline that changes between refreshes is worse than none.
                .ThenBy(run => run.SessionId, StringComparer.Ordinal)
                .Take(MaximumCandidates)
        ];
    }

    /// <summary>
    /// Whether a replayed run is fit to be compared against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It reached its finish, and nothing in it failed — no errored step, no failed assertion. That is
    /// the strongest statement the debugger can make about a run, and it is the bar.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> the same as <see cref="RunHealth.Passed"/>, which also demands that
    /// the run asserted something. That extra condition is right for the run list, where claiming a run
    /// passed when it proved nothing would flatter it. It is wrong here, because <b>assertions
    /// frequently live outside the timeline</b>: a test whose checks are <c>Assert.Equal</c> calls in
    /// the test method is invisible to the framework, and every one of this repository's own value
    /// samples is written that way. Requiring assertions would treat the absence of information as
    /// evidence of failure and deny a baseline to a large class of tests that pass perfectly well.
    /// </para>
    /// <para>
    /// The cost of being wrong runs the other way too, and is smaller: a baseline from a run that
    /// silently produced bad values shows differences that are real but uninteresting, which a reader
    /// can see and dismiss. Refusing to compare at all gives them nothing to look at.
    /// </para>
    /// </remarks>
    public static bool IsUsableBaseline(RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        RunTally tally = RunTally.Of(graph);

        return tally.IsFinished && tally.IsValid;
    }

    /// <summary>Folds a recorded run's signals into a graph, without touching any store.</summary>
    public static RunGraph Replay(IEnumerable<DebugEnvelope> envelopes)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        RunGraph graph = RunGraph.Empty;

        foreach (DebugEnvelope envelope in envelopes)
        {
            IPipeSignal signal;

            try
            {
                signal = DebugEnvelopeCodec.Unwrap(envelope);
            }
            catch (Exception)
            {
                // A frame this build cannot read is skipped, not fatal. A baseline built from most of
                // a run is more use than no baseline, and the alternative is that one unknown signal
                // kind in an old journal silently disables the whole feature.
                continue;
            }

            graph = RunProjection.Apply(graph, signal);
        }

        return graph;
    }

    /// <summary>Takes the values out of a replayed run.</summary>
    public static RunBaseline BaselineFrom(RunSummary run, RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(graph);

        return new RunBaseline
        {
            SessionId = run.SessionId,
            StartedAtUtc = run.StartedAtUtc,
            Variables = graph.Variables.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Description, StringComparer.Ordinal),
            Artifacts = graph.Artifacts.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Description, StringComparer.Ordinal),

            // What the run cost, taken from the same replay. The picker's own duration is used for the run
            // total because it comes from the sidecar and is there even for a run nobody has opened.
            Steps = TimingComparison.DurationsOf(graph),
            RanFor = run.Duration
        };
    }

    /// <summary>Compares a run against a baseline.</summary>
    public static ValueDiff Compare(RunGraph current, RunBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);

        return new ValueDiff
        {
            Baseline = baseline,
            Variables = ValueComparison.Compare(
                baseline.Variables,
                current.Variables.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Description, StringComparer.Ordinal),
                DebugValueKindTag.Variable),
            Artifacts = ValueComparison.Compare(
                baseline.Artifacts,
                current.Artifacts.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Description, StringComparer.Ordinal),
                DebugValueKindTag.Artifact)
        };
    }

    /// <summary>States why no comparison could be made.</summary>
    public static ValueDiff Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ValueDiff { Unavailable = reason };
    }
}
