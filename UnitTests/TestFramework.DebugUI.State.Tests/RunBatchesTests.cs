using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers gathering runs into the bursts they arrived in.
/// </summary>
public class RunBatchesTests
{
    [Fact]
    public void OneSuiteBecomesOneBatch()
    {
        // The case the whole thing exists for: fifty runs inside a second are one thing that happened,
        // not fifty. Listed separately they spend a column on a timestamp that is the same for all of
        // them.
        ImmutableList<RunBatch> batches = RunBatches.Of(
        [
            Run("a", seconds: 0),
            Run("b", seconds: 1),
            Run("c", seconds: 2)
        ]);

        Assert.Equal(3, Assert.Single(batches).Runs.Count);
    }

    [Fact]
    public void ASilenceStartsANewBatch()
    {
        ImmutableList<RunBatch> batches = RunBatches.Of(
        [
            Run("morning", seconds: 0),
            Run("afternoon", seconds: 3600)
        ]);

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void TheNewestBatchAndTheNewestRunComeFirst()
    {
        ImmutableList<RunBatch> batches = RunBatches.Of(
        [
            Run("old", seconds: 0),
            Run("new-first", seconds: 3600),
            Run("new-second", seconds: 3601)
        ]);

        Assert.Equal(["new-second", "new-first"], batches[0].Runs.Select(run => run.SessionId));
        Assert.Equal(["old"], batches[1].Runs.Select(run => run.SessionId));
    }

    [Fact]
    public void AGapIsMeasuredAgainstTheRunBeforeItRatherThanTheBatchStart()
    {
        // A stream of runs a minute apart is one session however long it goes on. Measured against the
        // batch's start instead, an hour of steady work would be cut into thirty arbitrary pieces.
        ImmutableList<RunBatch> batches = RunBatches.Of(
        [
            Run("a", seconds: 0),
            Run("b", seconds: 60),
            Run("c", seconds: 120),
            Run("d", seconds: 180),
            Run("e", seconds: 240)
        ]);

        Assert.Equal(5, Assert.Single(batches).Runs.Count);
    }

    [Fact]
    public void ABatchCountsTheRunsNobodyHasOpened()
    {
        // Said once on the heading instead of on every row underneath it.
        ImmutableList<RunBatch> batches = RunBatches.Of([Opened("seen", 0), Run("unseen", 1), Run("also", 2)]);

        RunBatch batch = Assert.Single(batches);

        Assert.Equal(3, batch.Runs.Count);
        Assert.Equal(2, batch.NotOpened);
    }

    [Fact]
    public void ABatchReportsItsWorstRun()
    {
        ImmutableList<RunBatch> batches = RunBatches.Of([Opened("passed", 0), Failed("broke", 1)]);

        Assert.Equal(RunHealth.Failed, Assert.Single(batches).Health);
    }

    [Fact]
    public void ABatchSpansFromItsFirstRunToItsLast()
    {
        RunBatch batch = Assert.Single(RunBatches.Of([Run("a", seconds: 0), Run("b", seconds: 30)]));

        Assert.Equal(DateTimeOffset.UnixEpoch, batch.StartedAtUtc);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(30), batch.LastStartedAtUtc);
    }

    [Fact]
    public void NoRunsMakeNoBatches()
    {
        Assert.Empty(RunBatches.Of([]));
    }

    [Fact]
    public void RunsWithNoRecordedTimeGatherTogether()
    {
        // A run recorded before the time was carried reads as the epoch, which is nowhere near anything
        // real. They form their own batch rather than being scattered through the list.
        ImmutableList<RunBatch> batches = RunBatches.Of(
        [
            Run("timed", seconds: 0) with { StartedAtUtc = DateTimeOffset.UtcNow },
            Run("untimed-a") with { StartedAtUtc = default },
            Run("untimed-b") with { StartedAtUtc = default }
        ]);

        Assert.Equal(2, batches.Count);
        Assert.Equal(2, batches[1].Runs.Count);
        Assert.Equal(default, batches[1].StartedAtUtc);
    }

    private static RunSummary Run(string sessionId, int seconds = 0)
        => new()
        {
            SessionId = sessionId,
            Name = sessionId,
            FullyQualifiedName = "Suite.LoginTests.Works",
            StartedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(seconds)
        };

    private static RunSummary Opened(string sessionId, int seconds)
        => Run(sessionId, seconds) with
        {
            IsFinished = true,
            Progress = RunProgress.Empty with { Steps = 1, AssertionsPassed = 1 }
        };

    private static RunSummary Failed(string sessionId, int seconds)
        => Run(sessionId, seconds) with
        {
            IsFinished = true,
            Progress = RunProgress.Empty with { Steps = 1, AssertionsFailed = 1 }
        };
}
