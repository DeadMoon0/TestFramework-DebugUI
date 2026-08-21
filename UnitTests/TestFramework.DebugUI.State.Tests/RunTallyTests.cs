using System.Collections.Immutable;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers counting up what a run amounts to.
/// </summary>
/// <remarks>
/// Two surfaces ask this — the summary page and the verdict on the board — so it is worth being sure
/// they cannot disagree, and worth being sure the arithmetic that decides "valid" is the arithmetic
/// intended rather than whatever the first screenshot happened to show.
/// </remarks>
public sealed class RunTallyTests
{
    [Fact]
    public void AnEmptyRunCountsToNothing()
    {
        RunTally tally = RunTally.Of(RunGraph.Empty);

        Assert.Equal(0, tally.Steps);
        Assert.False(tally.HasAssertions);
    }

    [Fact]
    public void StepsAreCountedByOutcome()
    {
        RunTally tally = RunTally.Of(Run(
            Step(0, DebugLifecycleState.Complete),
            Step(1, DebugLifecycleState.Error),
            Step(2, DebugLifecycleState.Skipped),
            Step(3, DebugLifecycleState.Running)));

        Assert.Equal(4, tally.Steps);
        Assert.Equal(1, tally.Complete);
        Assert.Equal(1, tally.Failed);
        Assert.Equal(1, tally.Skipped);

        // What is left is still to come, which is different from having passed.
        Assert.Equal(1, tally.Outstanding);
    }

    [Fact]
    public void ATimeoutCountsAsAFailure()
    {
        Assert.Equal(1, RunTally.Of(Run(Step(0, DebugLifecycleState.Timeout))).Failed);
    }

    [Fact]
    public void AFailingAssertionInvalidatesARunWhoseStepsAllPassed()
    {
        // The case worth being explicit about: every step did what it was told and the run is still
        // wrong. Judging validity on step outcomes alone would call this a pass.
        RunGraph graph = Run(Step(0, DebugLifecycleState.Complete)) with
        {
            Assertions = [new AssertionNode { Target = "answer", Succeeded = false }]
        };

        RunTally tally = RunTally.Of(graph);

        Assert.Equal(0, tally.Failed);
        Assert.Equal(1, tally.AssertionsFailed);
        Assert.False(tally.IsValid);
    }

    [Fact]
    public void ARunThatAssertedNothingIsNotClaimedToBeProven()
    {
        RunTally tally = RunTally.Of(Run(Step(0, DebugLifecycleState.Complete)));

        Assert.True(tally.IsValid);

        // Nothing broke, but nothing was checked either — and the page has to be able to say so
        // rather than showing a pass that means less than it looks like.
        Assert.False(tally.HasAssertions);
    }

    [Fact]
    public void AFailureCarriesTheStepItBelongsToAndTheFrameworksAccountOfIt()
    {
        StepNode broken = Step(1, DebugLifecycleState.Error) with
        {
            Label = "writes the row",
            Attempts =
            [
                new AttemptNode { Attempt = 1, Failure = new DebugFailureDetail { ExceptionType = "FirstTry", Message = "no" } },
                new AttemptNode { Attempt = 2, Failure = new DebugFailureDetail { ExceptionType = "LastTry", Message = "still no" } }
            ]
        };

        StepFailure failure = Assert.Single(RunTally.Of(Run(broken)).Failures);

        Assert.Equal("writes the row", failure.DisplayName);
        Assert.Equal(1, failure.StepId);

        // The surviving failure, not the first one: an earlier attempt that was retried past is not
        // what stopped the step.
        Assert.Equal("LastTry", failure.Detail?.ExceptionType);
    }

    [Fact]
    public void ARetriedStepIsCountedAsRetried()
    {
        StepNode retried = Step(0, DebugLifecycleState.Complete) with
        {
            Attempts = [new AttemptNode { Attempt = 1 }, new AttemptNode { Attempt = 2 }]
        };

        Assert.Equal(1, RunTally.Of(Run(retried)).Retried);
    }

    [Fact]
    public void TwoTalliesOfTheSameRunAreEqual()
    {
        // Otherwise anything watching the tally for a change fires on every event, and the summary
        // page rebuilds itself twenty times a second.
        RunGraph graph = Run(Step(0, DebugLifecycleState.Error));

        Assert.Equal(RunTally.Of(graph), RunTally.Of(graph));
        Assert.NotEqual(RunTally.Of(graph), RunTally.Of(Run(Step(0, DebugLifecycleState.Complete))));
    }

    private static RunGraph Run(params StepNode[] steps)
        => new() { Stages = [new StageNode { Name = "Main", Steps = [.. steps] }] };

    private static StepNode Step(int stepId, DebugLifecycleState lifecycle)
        => new() { StepId = stepId, Name = "step", Lifecycle = lifecycle };
}
