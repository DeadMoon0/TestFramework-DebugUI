using System;
using System.Collections.Immutable;
using System.Linq;
using Axiom.State.Selectors;

namespace TestFramework.DebugUI.State.Shell.Feed;

/// <summary>
/// The ways the UI asks about the message feed.
/// </summary>
public static class FeedSelectors
{
    /// <summary>The feed slice, for whoever needs the entries and the count together.</summary>
    public static readonly Selector<MainState, FeedState> SelectFeed =
        MainSelectors.SelectShell.Then(Selector.Property((ShellState shell) => shell.Feed));

    /// <summary>Every entry, oldest first.</summary>
    public static readonly Selector<MainState, ImmutableList<FeedEntry>> SelectEntries =
        SelectFeed.Then(Selector.Property((FeedState feed) => feed.Entries));

    /// <summary>How many entries the user has not seen.</summary>
    public static readonly Selector<MainState, int> SelectUnreadCount =
        SelectFeed.Then(Selector.Property((FeedState feed) => feed.UnreadCount));

    /// <summary>
    /// Every entry, newest first, which is the order a feed is read in.
    /// </summary>
    /// <remarks>
    /// A lane rather than a <c>Select</c> at the call site: the list is held oldest-first because
    /// that is the order it is appended in, and every reader wants the other one.
    /// </remarks>
    public static readonly Selector<MainState, ImmutableList<FeedEntry>> SelectNewestFirst =
        ReadOnlySelector.Of(nameof(SelectNewestFirst), static state => state.Shell.Feed.Entries.Reverse());

    /// <summary>
    /// The entries the user has not seen yet, oldest of those first.
    /// </summary>
    /// <remarks>
    /// The unread tail is the last <see cref="FeedState.UnreadCount"/> entries, and working that out
    /// from the two fields was open-coded in the window to decide how loudly to badge the feed.
    /// </remarks>
    public static readonly Selector<MainState, ImmutableList<FeedEntry>> SelectUnread =
        ReadOnlySelector.Of(nameof(SelectUnread), static state => UnreadOf(state.Shell.Feed));

    /// <summary>
    /// The entries not yet seen, out of a feed already in hand.
    /// </summary>
    /// <remarks>
    /// Takes the slice rather than the whole state so a reader that has already bound
    /// <see cref="SelectFeed"/> can ask this without going back to the store — the window does exactly
    /// that, deriving both the count and the worst severity from one binding.
    /// </remarks>
    public static ImmutableList<FeedEntry> UnreadOf(FeedState feed)
        => ImmutableList.CreateRange(feed.Entries.Skip(Math.Max(0, feed.Entries.Count - feed.UnreadCount)));
}
