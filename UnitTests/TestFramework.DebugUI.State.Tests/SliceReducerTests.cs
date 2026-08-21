using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Axiom.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the state tree's slices: that each one's reducer is registered, and the two transitions
/// whose behaviour changed when the shell controller's chains moved into effects.
/// </summary>
/// <remarks>
/// The registration tests look trivial and are not. Axiom resolves a reducer per action at dispatch
/// time and throws when it finds none, so a slice left out of <see cref="MainStore"/> is invisible
/// until something dispatches into it — which, for the feed or the transport, might first happen in
/// front of a user.
/// </remarks>
public class SliceReducerTests : IDisposable
{
    private readonly List<IDisposable> stores = [];

    public void Dispose()
    {
        foreach (IDisposable store in stores) store.Dispose();
        GC.SuppressFinalize(this);
    }

    private StateStore<MainState> CreateStore()
    {
        StateStore<MainState> store = MainStore.Create().Build();
        stores.Add(store);
        return store;
    }

    [Fact]
    public void EverySliceHasAReducerRegistered()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(ShellActions.SetTransportStatus, TransportStatus.Listening);
        store.Dispatch(ShellActions.AwaitRerun, "Some.Test");
        store.Dispatch(FeedActions.AppendEntry, Entry("Something happened."));
        store.Dispatch(BoardActions.SelectStep, new StepSelection { StageName = "Main", StepId = 0 });
        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1")]);

        Assert.Equal(TransportStatus.Listening, store.GetValue(state => state.Shell.Transport));
        Assert.Equal("Some.Test", store.GetValue(state => state.Shell.AwaitedRerun));
        Assert.Single(store.GetValue(state => state.Shell.Feed.Entries));
        Assert.Equal(1, store.GetValue(state => state.Shell.Feed.UnreadCount));
        Assert.Equal(0, store.GetValue(state => state.Board.SelectedStep!.StepId));
        Assert.Single(store.GetValue(state => state.Runs.All));
    }

    [Fact]
    public void ReadingTheFeedClearsTheUnreadCountWithoutDroppingEntries()
    {
        StateStore<MainState> store = CreateStore();

        store.Dispatch(FeedActions.AppendEntry, Entry("One."));
        store.Dispatch(FeedActions.AppendEntry, Entry("Two."));
        store.Dispatch(FeedActions.ClearUnread);

        Assert.Equal(0, store.GetValue(state => state.Shell.Feed.UnreadCount));
        Assert.Equal(2, store.GetValue(state => state.Shell.Feed.Entries.Count));
    }

    [Fact]
    public void AComparisonIsAppliedToTheRunItWasComputedFor()
    {
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1")]);
        store.Dispatch(MainActions.SelectRun, "s1");

        store.Dispatch(ComparisonActions.SetComparison, "s1", RunComparison.Unavailable("No earlier run."));

        Assert.Equal("No earlier run.", store.GetValue(state => state.Board.Comparison.Values.Unavailable));
    }

    [Fact]
    public void AComparisonForARunThatIsNoLongerSelectedIsDropped()
    {
        // The race the old code guarded before dispatching, and this now guards inside the
        // transition: reading a journal takes long enough to click through several runs, so a result
        // can arrive after the selection has moved. Badging the new run's values with the old run's
        // verdict is worse than showing no comparison at all.
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1"), Recorded("s2")]);
        store.Dispatch(MainActions.SelectRun, "s2");

        store.Dispatch(ComparisonActions.SetComparison, "s1", RunComparison.Unavailable("Belongs to the other run."));

        Assert.Null(store.GetValue(state => state.Board.Comparison.Values.Unavailable));
    }

    [Fact]
    public void ChangingTheSelectionClearsTheComparisonWithTheBoard()
    {
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1"), Recorded("s2")]);
        store.Dispatch(MainActions.SelectRun, "s1");
        store.Dispatch(ComparisonActions.SetComparison, "s1", RunComparison.Unavailable("Stale."));

        store.Dispatch(MainActions.SelectRun, "s2");

        Assert.Null(store.GetValue(state => state.Board.Comparison.Values.Unavailable));
        Assert.Null(store.GetValue(state => state.Board.SelectedStep));
        Assert.Empty(store.GetValue(state => state.Board.ActiveRun.Stages));
    }

    [Fact]
    public void AskingForARerunRecordsWhichTestIsBeingWaitedFor()
    {
        // Recorded in the reducer rather than beside the process start, so the intent is on the state
        // before the effect that starts the run is even queued. A fast test can finish in a couple of
        // hundred milliseconds, and its run would otherwise be filed behind the one on screen.
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunsActions.AddRecorded, [Repeatable("s1", "Suite.TheTest")]);
        store.Dispatch(MainActions.SelectRun, "s1");

        store.Dispatch(MainActions.RerunSelected);

        Assert.Equal("Suite.TheTest", store.GetValue(state => state.Shell.AwaitedRerun));
    }

    [Fact]
    public void ARunThatCannotBeRepeatedRecordsNoIntent()
    {
        // An intent that will never be answered would be spent on the next unrelated run of that name.
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1")]);
        store.Dispatch(MainActions.SelectRun, "s1");

        store.Dispatch(MainActions.RerunSelected);

        Assert.Null(store.GetValue(state => state.Shell.AwaitedRerun));
    }

    [Fact]
    public void ADerivedLaneStillReadsTheSameAnswerAsTheStore()
    {
        // The lane and the plain getter behind it are one implementation on purpose: Bind takes a
        // selector and GetValue takes a function, and two spellings of "the selected run" would be
        // free to disagree about a selection naming a run the catalogue no longer holds.
        StateStore<MainState> store = CreateStore();
        store.Dispatch(RunsActions.AddRecorded, [Recorded("s1")]);
        store.Dispatch(MainActions.SelectRun, "s1");

        Assert.Equal("s1", store.GetValue(RunsSelectors.SelectedRunOf)?.SessionId);
        Assert.Equal("s1", store.GetValue(state => state.Runs.SelectedSessionId));

        store.Dispatch(MainActions.SelectRun, "gone");

        Assert.Null(store.GetValue(RunsSelectors.SelectedRunOf));
        Assert.Equal(string.Empty, store.GetValue(RunsSelectors.SelectedTestOf));
    }

    private static FeedEntry Entry(string title) => new()
    {
        AtUtc = DateTimeOffset.UnixEpoch,
        Severity = FeedSeverity.Info,
        Source = FeedSource.App,
        Title = title
    };

    private static RunSummary Recorded(string sessionId) => new()
    {
        SessionId = sessionId,
        Name = sessionId,
        StartedAtUtc = DateTimeOffset.UnixEpoch,
        IsFinished = true
    };

    private static RunSummary Repeatable(string sessionId, string test) => Recorded(sessionId) with
    {
        FullyQualifiedName = test,
        ProjectFilePath = "Suite.csproj",
        CanRerun = true
    };
}
