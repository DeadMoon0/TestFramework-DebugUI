using System.Collections.Immutable;
using Axiom.State.Actions;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

/// <summary>
/// The actions that cross more than one slice.
/// </summary>
/// <remarks>
/// Axiom builds one reducer handler per action and throws when two reducers claim the same one, so an
/// action that writes to two slices cannot be split between their reducers — it has to be owned in
/// one place. These three are that case; everything else belongs to the slice it writes and lives
/// beside it in the folder tree.
/// </remarks>
public static class MainActions
{
    /// <summary>
    /// Applies a batch of debug envelopes.
    /// </summary>
    /// <remarks>
    /// A batch rather than one action per envelope, because Axiom deep-clones the state on every
    /// dispatch. A log-heavy run emits thousands of events per second, and dispatching each
    /// separately would put the clone — and a notification to every binding — on each one.
    /// Coalescing turns that into one clone per window.
    /// <para>
    /// Cross-slice: an envelope updates the run's summary in <see cref="RunsState"/> and, when it
    /// belongs to the selected run, the graph in <see cref="BoardState"/>.
    /// </para>
    /// </remarks>
    public static readonly StateAction<ImmutableList<DebugEnvelope>> IngestBatch =
        new(nameof(MainActions), nameof(IngestBatch));

    /// <summary>
    /// Renders a different session.
    /// </summary>
    /// <remarks>
    /// Cross-slice, and that is the whole point of it: it moves the selection in
    /// <see cref="RunsState"/> and clears the board and its comparison in the same transition, so
    /// there is no instant in which the new run's name sits above the previous run's contents.
    /// </remarks>
    public static readonly StateAction<string> SelectRun = new(nameof(MainActions), nameof(SelectRun));

    /// <summary>
    /// Runs the selected run's test again.
    /// </summary>
    /// <remarks>
    /// Cross-slice: it reads the selection out of <see cref="RunsState"/> and records the intent in
    /// <see cref="ShellState.AwaitedRerun"/>. Recording it in the reducer is what makes the ordering
    /// safe — a dispatch reduces before its effects are queued, so the UI knows to show the new run
    /// before the process that produces it is started. A fast test finishes in a couple of hundred
    /// milliseconds, and claiming the intent afterwards would be a race the user loses on exactly the
    /// runs that are quickest to read.
    /// </remarks>
    public static readonly StateAction RerunSelected = new(nameof(MainActions), nameof(RerunSelected));
}
