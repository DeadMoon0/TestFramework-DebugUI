using Axiom.State.Actions;

namespace TestFramework.DebugUI.State.Shell.Feed;

/// <summary>
/// The actions that change the message feed.
/// </summary>
public static class FeedActions
{
    /// <summary>Appends an entry to the message feed.</summary>
    public static readonly StateAction<FeedEntry> AppendEntry = new(nameof(FeedActions), nameof(AppendEntry));

    /// <summary>Marks the feed as read.</summary>
    public static readonly StateAction ClearUnread = new(nameof(FeedActions), nameof(ClearUnread));
}
