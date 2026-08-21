using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Axiom.State;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the comparison chain end to end: a request goes in as one action and the answer comes back
/// as another.
/// </summary>
/// <remarks>
/// This is the wiring the effect replaced a bare <c>Task.Run</c> with, and the part that a reducer
/// test cannot reach: whether the effect is registered at all, whether it runs off the dispatch
/// thread, and whether what it dispatches is something the reducer accepts. A store built without a
/// resolver leaves the effect out entirely, so nothing else in the suite exercises this.
/// </remarks>
public class ComparisonEffectTests : IDisposable
{
    private readonly List<IDisposable> stores = [];

    public void Dispose()
    {
        foreach (IDisposable store in stores) store.Dispose();
        GC.SuppressFinalize(this);
    }

    private StateStore<MainState> CreateStore(string? runsDirectory)
    {
        StateStore<MainState> store = MainStore.Create(new BaselineResolver(runsDirectory)).Build();
        stores.Add(store);
        return store;
    }

    [Fact]
    public void AskingForAComparisonAnswersWithOne()
    {
        // No journal directory, so the resolver has a definite answer with no file to read: the
        // comparison is unavailable and says why. What is under test is that the answer arrives at
        // all — the effect fired, computed, dispatched, and the reducer accepted it for this run.
        StateStore<MainState> store = CreateStore(runsDirectory: null);

        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1")]);
        store.Dispatch(MainActions.SelectRun, "s1");
        store.Dispatch(ComparisonActions.Refresh, "s1");

        string? reason = WaitFor(store, state => state.Board.Comparison.Values.Unavailable);

        Assert.Equal("No journal directory is configured, so earlier runs cannot be read.", reason);
    }

    [Fact]
    public void AnAnswerForARunThatIsNoLongerSelectedNeverLands()
    {
        // The request is made and the selection then moves before the answer can arrive. The guard
        // lives in the reducer, so this holds however the two happen to interleave.
        StateStore<MainState> store = CreateStore(runsDirectory: null);

        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1"), Recorded("s2")]);
        store.Dispatch(MainActions.SelectRun, "s1");
        store.Dispatch(ComparisonActions.Refresh, "s1");
        store.Dispatch(MainActions.SelectRun, "s2");

        // Long enough for the effect to have finished and been dropped. There is nothing to wait for
        // here — the assertion is that nothing arrives — so this is a settle, not a poll.
        Thread.Sleep(300);

        Assert.Null(store.GetValue(state => state.Board.Comparison.Values.Unavailable));
    }

    [Fact]
    public void ARequestForAnUnknownRunIsIgnored()
    {
        StateStore<MainState> store = CreateStore(runsDirectory: null);

        store.Dispatch(ComparisonActions.Refresh, "never-existed");
        Thread.Sleep(300);

        Assert.Null(store.GetValue(state => state.Board.Comparison.Values.Unavailable));
    }

    /// <summary>
    /// Polls the store until the read answers something, or gives up.
    /// </summary>
    /// <remarks>
    /// The effect is deliberately off the dispatch thread, so the dispatch that starts it returns
    /// before the answer exists. A generous ceiling with an early exit: the wait is normally over in
    /// a millisecond, and the ceiling only decides how a genuine break is reported.
    /// </remarks>
    private static T? WaitFor<T>(StateStore<MainState> store, Func<MainState, T?> read)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            T? value = store.GetValue(read);

            if (value is not null)
                return value;

            Thread.Sleep(5);
        }

        return default;
    }

    private static RunSummary Recorded(string sessionId) => new()
    {
        SessionId = sessionId,
        Name = sessionId,
        StartedAtUtc = DateTimeOffset.UnixEpoch,
        IsFinished = true
    };
}
