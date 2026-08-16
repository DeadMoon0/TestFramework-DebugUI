using System.Collections.Immutable;
using Axiom.State.Actions;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

/// <summary>
/// The actions that drive the run state.
/// </summary>
public static class RunActions
{
    /// <summary>
    /// Applies a batch of debug envelopes.
    /// </summary>
    /// <remarks>
    /// A batch rather than one action per envelope, because Axiom deep-clones the state on every
    /// dispatch. A log-heavy run emits thousands of events per second, and dispatching each
    /// separately would put the clone — and a notification to every binding — on each one.
    /// Coalescing turns that into one clone per window.
    /// </remarks>
    public static readonly StateAction<ImmutableList<DebugEnvelope>> IngestBatch =
        new(nameof(RunActions), nameof(IngestBatch));

    /// <summary>Renders a different session.</summary>
    public static readonly StateAction<string> SelectRun = new(nameof(RunActions), nameof(SelectRun));

    /// <summary>Appends an entry to the message feed.</summary>
    public static readonly StateAction<FeedEntry> AppendFeedEntry = new(nameof(RunActions), nameof(AppendFeedEntry));

    /// <summary>Marks the feed as read.</summary>
    public static readonly StateAction ClearUnreadFeed = new(nameof(RunActions), nameof(ClearUnreadFeed));

    /// <summary>Records the transport's current status.</summary>
    public static readonly StateAction<TransportStatus> SetTransportStatus =
        new(nameof(RunActions), nameof(SetTransportStatus));

    /// <summary>Shows one step's detail.</summary>
    public static readonly StateAction<StepSelection> SelectStep = new(nameof(RunActions), nameof(SelectStep));

    /// <summary>
    /// Adds runs recorded on disk to the picker.
    /// </summary>
    /// <remarks>
    /// Kept apart from ingesting: a listed run has been named but not opened, so it contributes a
    /// summary without any of the events behind it. Merging rather than replacing, because a live
    /// run and its own journal entry are the same run and must not appear twice.
    /// </remarks>
    public static readonly StateAction<ImmutableList<RunSummary>> AddRecordedRuns =
        new(nameof(RunActions), nameof(AddRecordedRuns));
}
