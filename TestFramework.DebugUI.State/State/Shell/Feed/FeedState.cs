using System.Collections.Immutable;

namespace TestFramework.DebugUI.State.Shell.Feed;

/// <summary>
/// The message feed, and how much of it the user has not seen.
/// </summary>
/// <remarks>
/// A slice of its own because the count is only meaningful against the list it counts into: the
/// unread tail is the last <see cref="UnreadCount"/> of <see cref="Entries"/>, so one reducer has to
/// own both or the badge starts describing entries that are no longer there.
/// </remarks>
public record struct FeedState()
{
    /// <summary>
    /// Every entry, oldest first.
    /// </summary>
    /// <remarks>
    /// Bounded rather than unbounded: the previous UI appended to an ever-growing <c>DebugInfo</c>
    /// string for the life of the process.
    /// </remarks>
    public ImmutableList<FeedEntry> Entries = ImmutableList<FeedEntry>.Empty;

    /// <summary>Feed entries not yet seen by the user.</summary>
    public int UnreadCount = 0;
}
