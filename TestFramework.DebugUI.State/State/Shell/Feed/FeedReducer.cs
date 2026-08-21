using System.Collections.Immutable;
using Axiom.State.Reducers;

namespace TestFramework.DebugUI.State.Shell.Feed;

/// <summary>
/// Applies the transitions that change the message feed.
/// </summary>
public sealed class FeedReducer : Reducer<MainState>
{
    /// <summary>Bounds the feed so a long session cannot grow it without limit.</summary>
    private const int MaxEntries = 500;

    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public FeedReducer()
    {
        Scope(FeedSelectors.SelectFeed)
            .On(FeedActions.AppendEntry, Append)
            .On(FeedActions.ClearUnread, state => state with { UnreadCount = 0 });
    }

    /// <summary>
    /// Adds one entry and counts it as unread.
    /// </summary>
    /// <remarks>
    /// The bound is applied by dropping from the front, so the unread count keeps describing the tail
    /// it is meant to: trimming the other end would leave the badge pointing at entries that had just
    /// been thrown away.
    /// </remarks>
    private static FeedState Append(FeedState state, FeedEntry entry)
    {
        ImmutableList<FeedEntry> entries = state.Entries.Add(entry);

        if (entries.Count > MaxEntries)
            entries = entries.RemoveRange(0, entries.Count - MaxEntries);

        return state with
        {
            Entries = entries,
            UnreadCount = state.UnreadCount + 1
        };
    }
}
