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

    /// <summary>
    /// Records that a re-run of one test was asked for, so its run is shown when it arrives.
    /// </summary>
    /// <remarks>
    /// Carried as the test's name: the session the new run announces itself under is decided by the
    /// test host, so there is nothing else to match it by until it exists.
    /// </remarks>
    public static readonly StateAction<string> AwaitRerun = new(nameof(RunActions), nameof(AwaitRerun));

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

    /// <summary>
    /// Records how the selected run's values compare with the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// Its own action because it arrives late and separately: the comparison needs an earlier run
    /// replayed off disk, so the board is on screen before this can be known. Dispatched with an
    /// unavailable reason rather than withheld when there is nothing to compare against, so the panel
    /// can say why instead of looking broken.
    /// </remarks>
    public static readonly StateAction<ValueDiff> SetValueDiff = new(nameof(RunActions), nameof(SetValueDiff));

    /// <summary>
    /// Records both comparisons against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// One action for the pair because they are computed together off one journal read, and dispatching them
    /// separately would put the board in a state where the values had been compared and the clock had not.
    /// </remarks>
    public static readonly StateAction<RunComparison> SetComparison = new(nameof(RunActions), nameof(SetComparison));
}
