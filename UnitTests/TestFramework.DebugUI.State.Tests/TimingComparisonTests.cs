using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers which steps of a run count as having got slower.
/// </summary>
/// <remarks>
/// The rules are the whole feature. A comparison that flags everything is a comparison nobody reads, and one
/// that flags nothing is a feature that appears not to work — so the two thresholds and what they exclude are
/// what these tests are about, rather than the arithmetic of subtracting two timestamps.
/// </remarks>
public class TimingComparisonTests
{
    [Fact]
    public void AStepThatTookMuchLongerIsSlower()
    {
        StepTiming step = Compare(then: TimeSpan.FromSeconds(1), now: TimeSpan.FromSeconds(4));

        Assert.Equal(StepTimingChange.Slower, step.Change);
        Assert.Equal(TimeSpan.FromSeconds(3), step.Delta);
        Assert.Equal(4, step.Ratio);
        Assert.True(step.IsInteresting);
    }

    [Fact]
    public void AStepThatTookMuchLessTimeIsFaster()
    {
        StepTiming step = Compare(then: TimeSpan.FromSeconds(4), now: TimeSpan.FromSeconds(1));

        Assert.Equal(StepTimingChange.Faster, step.Change);
        Assert.Equal(TimeSpan.FromSeconds(-3), step.Delta);
        Assert.True(step.IsInteresting);
    }

    [Fact]
    public void ATinyStepThatTripledIsNotWorthMentioning()
    {
        // Three times slower and four milliseconds dearer. Without the floor every step in the run is a
        // headline, and the answer to "why was this run slow" stops being about the run being slow.
        StepTiming step = Compare(then: TimeSpan.FromMilliseconds(2), now: TimeSpan.FromMilliseconds(6));

        Assert.Equal(StepTimingChange.Unchanged, step.Change);
        Assert.False(step.IsInteresting);
    }

    [Fact]
    public void ALongStepThatDriftedALittleIsNotWorthMentioning()
    {
        // A tenth of a second clears the floor, so without the proportional test this would flag a minute-long
        // step for ordinary noise.
        StepTiming step = Compare(then: TimeSpan.FromSeconds(60), now: TimeSpan.FromSeconds(60.2));

        Assert.Equal(StepTimingChange.Unchanged, step.Change);
    }

    [Fact]
    public void AStepThatUsedToBeInstantAndNowIsNotCountsOnTheFloorAlone()
    {
        // Nothing to take a proportion of, so the absolute movement is the whole test — and half a second
        // appearing where there was nothing is exactly the case worth catching.
        StepTiming step = Compare(then: TimeSpan.Zero, now: TimeSpan.FromMilliseconds(500));

        Assert.Equal(StepTimingChange.Slower, step.Change);

        // Stated rather than reported as infinity: "it is now infinitely slower" says nothing about anything.
        Assert.Null(step.Ratio);
    }

    [Fact]
    public void BothConditionsHaveToHold()
    {
        // Exactly at the floor but only a tenth proportionally, and exactly at the tolerance but well under
        // the floor. Each condition alone would let one of these through.
        Assert.False(TimingComparison.IsMaterial(
            TimingComparison.Floor,
            TimeSpan.FromSeconds(1.1),
            TimeSpan.FromSeconds(1)));

        Assert.False(TimingComparison.IsMaterial(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void AStepThatIsStillRunningIsNotJudged()
    {
        // Its duration is null until it settles. Comparing a partial time would make a step read as faster
        // right up to the moment it turns out to be slower.
        RunGraph current = Graph(("Main", 0, "Fetch", null));
        TimingDiff diff = TimingComparison.Compare(current, Baseline(("Main", 0, "Fetch", TimeSpan.FromSeconds(1))), null, null);

        Assert.Equal(StepTimingChange.Unchanged, Assert.Single(diff.Steps).Change);
    }

    [Fact]
    public void AStepTheEarlierRunDidNotHaveIsAdded()
    {
        TimingDiff diff = TimingComparison.Compare(
            Graph(("Main", 0, "Fetch", TimeSpan.FromSeconds(1))),
            Baseline(),
            null,
            null);

        StepTiming step = Assert.Single(diff.Steps);

        Assert.Equal(StepTimingChange.Added, step.Change);
        Assert.Null(step.Then);
        Assert.False(step.IsInteresting);
    }

    [Fact]
    public void AStepTheEarlierRunHadAndThisOneDoesNotIsRemoved()
    {
        // Listed rather than dropped: a run that got quicker by not doing something is a plan change, which
        // is a different finding from a step that sped up.
        TimingDiff diff = TimingComparison.Compare(
            Graph(),
            Baseline(("Main", 0, "Fetch", TimeSpan.FromSeconds(1))),
            null,
            null);

        Assert.Equal(StepTimingChange.Removed, Assert.Single(diff.Steps).Change);
    }

    [Fact]
    public void ADifferentStepAtTheSameIndexIsNotComparedAgainstIt()
    {
        // Insert one step into a plan and every index after it names something else. Comparing those would
        // report the whole tail of the run as having changed speed, which is worse than saying nothing.
        TimingDiff diff = TimingComparison.Compare(
            Graph(("Main", 0, "WaitForBlob", TimeSpan.FromSeconds(9))),
            Baseline(("Main", 0, "Fetch", TimeSpan.FromMilliseconds(50))),
            null,
            null);

        StepTiming step = Assert.Single(diff.Steps, candidate => candidate.DisplayName == "WaitForBlob");

        Assert.Equal(StepTimingChange.Added, step.Change);
        Assert.Null(step.Then);
    }

    [Fact]
    public void TwoStagesWithConfusableNamesAreNotMixedUp()
    {
        // Keys are joined with a separator rather than concatenated: stage "A" step 12 and stage "A1" step 2
        // would otherwise be the same key.
        Assert.NotEqual(TimingComparison.KeyOf("A", 12), TimingComparison.KeyOf("A1", 2));
    }

    [Fact]
    public void TheBiggestMoversAreTheOnesThatCostTheMostTimeNotTheWorstRatios()
    {
        // A step that went from four seconds to twelve is where a slow run went. One that went from two
        // hundred milliseconds to a second is five times worse and costs the run almost nothing.
        RunGraph current = Graph(
            ("Main", 0, "Small", TimeSpan.FromSeconds(1)),
            ("Main", 1, "Large", TimeSpan.FromSeconds(12)));

        TimingDiff diff = TimingComparison.Compare(
            current,
            Baseline(
                ("Main", 0, "Small", TimeSpan.FromMilliseconds(200)),
                ("Main", 1, "Large", TimeSpan.FromSeconds(4))),
            null,
            null);

        Assert.Equal(5, diff.Steps.Single(step => step.DisplayName == "Small").Ratio);
        Assert.Equal("Large", diff.BiggestMovers(2)[0].DisplayName);
    }

    [Fact]
    public void TheRunsOwnClockIsComparedRatherThanTheSumOfItsSteps()
    {
        // Steps run in parallel layers, so adding them up reports a duration the run never took.
        TimingDiff diff = TimingComparison.Compare(
            Graph(("Main", 0, "A", TimeSpan.FromSeconds(5)), ("Main", 1, "B", TimeSpan.FromSeconds(5))),
            Baseline(("Main", 0, "A", TimeSpan.FromSeconds(5)), ("Main", 1, "B", TimeSpan.FromSeconds(5))),
            ranFor: TimeSpan.FromSeconds(5),
            baselineRanFor: TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), diff.Now);
        Assert.False(diff.RunMovedMaterially);
    }

    [Fact]
    public void ARunThatGotSlowerOverallSaysSo()
    {
        TimingDiff diff = TimingComparison.Compare(
            Graph(),
            Baseline(),
            ranFor: TimeSpan.FromSeconds(12),
            baselineRanFor: TimeSpan.FromSeconds(4));

        Assert.True(diff.RunMovedMaterially);
        Assert.Equal(3, diff.Ratio);
        Assert.Equal(TimeSpan.FromSeconds(8), diff.Delta);
    }

    [Fact]
    public void NothingIsClaimedWhenThereIsNoBaseline()
    {
        TimingDiff diff = TimingComparison.Unavailable("No earlier run of this test has been recorded yet.");

        Assert.False(diff.HasBaseline);
        Assert.Empty(diff.Steps);
        Assert.Equal("No earlier run of this test has been recorded yet.", diff.Unavailable);
        Assert.False(diff.RunMovedMaterially);
    }

    [Fact]
    public void AStepIsFoundByItsStageAndIndex()
    {
        TimingDiff diff = TimingComparison.Compare(
            Graph(("Main", 3, "Fetch", TimeSpan.FromSeconds(1))),
            Baseline(("Main", 3, "Fetch", TimeSpan.FromSeconds(4))),
            null,
            null);

        Assert.Equal(StepTimingChange.Faster, diff.ForStep("Main", 3)!.Change);
        Assert.Null(diff.ForStep("Main", 4));
        Assert.Null(diff.ForStep("Cleanup", 3));
    }

    private static StepTiming Compare(TimeSpan then, TimeSpan now)
    {
        TimingDiff diff = TimingComparison.Compare(
            Graph(("Main", 0, "Fetch", now)),
            Baseline(("Main", 0, "Fetch", then)),
            null,
            null);

        return Assert.Single(diff.Steps);
    }

    private static RunBaseline Baseline(params (string Stage, int StepId, string Name, TimeSpan? Took)[] steps)
        => new()
        {
            SessionId = "earlier",
            StartedAtUtc = DateTimeOffset.UnixEpoch,
            Steps = steps.ToImmutableDictionary(
                step => TimingComparison.KeyOf(step.Stage, step.StepId),
                step => new StepDuration
                {
                    StageName = step.Stage,
                    StepId = step.StepId,
                    DisplayName = step.Name,
                    Took = step.Took
                },
                StringComparer.Ordinal)
        };

    /// <summary>
    /// A graph carrying nothing but the timings, built by hand.
    /// </summary>
    /// <remarks>
    /// A step's duration is derived from the two timestamps it reported, so a fabricated finish is what gives
    /// it a duration here — the alternative is running two real timelines and hoping they differ by the right
    /// amount, which would test the clock rather than the rules.
    /// </remarks>
    private static RunGraph Graph(params (string Stage, int StepId, string Name, TimeSpan? Took)[] steps)
        => new()
        {
            Stages =
            [
                .. steps
                    .GroupBy(step => step.Stage, StringComparer.Ordinal)
                    .Select(stage => new StageNode
                    {
                        Name = stage.Key,
                        Steps =
                        [
                            .. stage.Select(step => new StepNode
                            {
                                StepId = step.StepId,
                                Name = step.Name,
                                StartedAtUtc = DateTimeOffset.UnixEpoch,
                                FinishedAtUtc = step.Took is { } took ? DateTimeOffset.UnixEpoch + took : null
                            })
                        ]
                    })
            ]
        };
}
