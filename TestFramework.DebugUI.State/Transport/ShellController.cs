using System;
using System.Collections.Concurrent;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// Connects the sources of run events to the store.
/// </summary>
/// <remarks>
/// <para>
/// Everything the window does that is not drawing happens here: listening for live runs, listing the
/// ones already recorded, switching between them, releasing a breakpoint, asking a run to stop. It
/// holds no WPF type, so the whole behaviour of the application can be exercised without opening a
/// window — which is the only way most of it would ever get tested.
/// </para>
/// <para>
/// Every session is ingested; only the selected one is projected. That is what lets a suite run
/// twenty timelines at once without the board of the one being watched slowing down.
/// </para>
/// </remarks>
public sealed partial class ShellController : IShellCommands, IDisposable
{
    /// <summary>
    /// How many events of an unselected live run are kept so it can be shown if selected.
    /// </summary>
    /// <remarks>
    /// A run that is not on screen is still projected from scratch the moment someone picks it, and
    /// the events are the only record of what it did. Bounded because a log-heavy run would
    /// otherwise grow without limit in the background: past the bound the earliest events are
    /// dropped, and the board is rebuilt from the journal instead once the run has finished.
    /// </remarks>
    public const int RetainedEventsPerRun = 20_000;

    private readonly StateStore<MainState> store;
    private readonly RunIngestService ingest;
    private readonly PipeRunEventSource pipe;
    private readonly string? runsDirectory;
    private readonly ConcurrentDictionary<string, RunHistory> histories = new(StringComparer.Ordinal);

    /// <summary>
    /// What test each attached run is an execution of, as the run announced itself.
    /// </summary>
    /// <remarks>
    /// Kept here rather than read from the store because it is needed on the reader thread, synchronously,
    /// to answer a step that is holding a run open — and the store is a coalesced dispatch behind the pipe.
    /// </remarks>
    private readonly ConcurrentDictionary<string, string> tests = new(StringComparer.Ordinal);

    private bool disposed;

    /// <summary>
    /// Gets where recorded runs are read from, when there is somewhere.
    /// </summary>
    /// <remarks>
    /// Exposed so importing a bundle can put its runs where the listing already looks, rather than inventing a
    /// second place for runs to live and a second way to find them.
    /// </remarks>
    public string? RunsDirectory => runsDirectory;

    /// <summary>
    /// Creates a controller over a store.
    /// </summary>
    /// <param name="store">The store to drive.</param>
    /// <param name="runsDirectory">Where recorded runs live. Defaults to the framework's journal.</param>
    /// <param name="pipeName">The pipe to listen on. Defaults to the framework's well-known name.</param>
    /// <param name="ingestWindow">How long to coalesce events before dispatching.</param>
    public ShellController(StateStore<MainState> store, string? runsDirectory = null, string? pipeName = null, TimeSpan? ingestWindow = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.runsDirectory = runsDirectory ?? DefaultRunsDirectory();

        ingest = new RunIngestService(store, ingestWindow);
        pipe = new PipeRunEventSource(pipeName);

        pipe.EnvelopeReceived += OnLiveEnvelope;
        pipe.Notice += Report;
        pipe.ConnectionsChanged += OnConnectionsChanged;
        pipe.SessionEnded += OnSessionEnded;
        pipe.PauseAtBreakpoint = ShouldPause;
    }

    /// <summary>Gets the live transport, for the window to drive breakpoints and cancellation.</summary>
    public PipeRunEventSource Pipe => pipe;

    /// <summary>
    /// How many runs are still holding their events in memory.
    /// </summary>
    /// <remarks>
    /// Internal because it is a fact about the implementation rather than about any run, and no
    /// surface should draw it. It exists so that holding on to a session too long is a test failure
    /// rather than something noticed as memory growth after a week of watch mode.
    /// </remarks>
    internal int RetainedSessionCount => histories.Count;

    /// <summary>
    /// Gets or sets the test of whether a step should be held at its breakpoint.
    /// </summary>
    /// <remarks>
    /// Every step asks before it runs, so with nothing set here no step is ever held. That default
    /// is the safe one: a consumer that paused by default would stall each step of every attached
    /// run until the producer gave up waiting.
    /// </remarks>
    public Func<BreakpointQuestion, bool>? PauseAtBreakpoint { get; set; }

    /// <summary>
    /// Gets or sets what to do when a step of any attached run fails for the last time.
    /// </summary>
    /// <remarks>
    /// Raised from the reader thread as the transition passes, so a consumer can arm a pause before the run
    /// reaches its next step. Only for a failure with no retry behind it: a step that is about to be tried
    /// again has not failed yet, and stopping the run there would interrupt its own recovery.
    /// </remarks>
    public Action<StepFailureNotice>? StepEndedBadly { get; set; }

    /// <summary>
    /// Starts listening for live runs and lists the ones already recorded.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        pipe.Start();
        store.Dispatch(ShellActions.SetTransportStatus, TransportStatus.Listening);
        PublishTransportDetails();

        RefreshRecordedRuns();
        ReportRecordingsFromOtherBuilds();
    }

    /// <summary>Adds an entry to the message feed.</summary>
    public void Report(FeedEntry entry)
    {
        if (entry is null || disposed)
            return;

        store.Dispatch(FeedActions.AppendEntry, entry);
    }

    /// <summary>
    /// Stops listening and flushes anything already received.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        pipe.EnvelopeReceived -= OnLiveEnvelope;
        pipe.Notice -= Report;
        pipe.ConnectionsChanged -= OnConnectionsChanged;
        pipe.SessionEnded -= OnSessionEnded;

        pipe.Dispose();
        ingest.Dispose();
    }

    /// <summary>
    /// The folder runs are recorded in, or null when it cannot be determined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A machine with no journal root is the ordinary case before the tool has been installed. The
    /// window still opens and still watches live runs; there is simply nothing recorded to list.
    /// </para>
    /// <para>
    /// Public because the effects need the same answer: <see cref="ComparisonEffects"/> reads earlier
    /// runs out of this folder, and it is built when the store is, before any controller exists. Two
    /// separate defaults would mean the comparison reading a different folder than the one the runs
    /// were listed from.
    /// </para>
    /// </remarks>
    public static string? DefaultRunsDirectory()
    {
        try
        {
            return DebugJournal.RunsDirectory;
        }
        catch (Exception e)
        {
            Log.Write(e);
            return null;
        }
    }

    private static FeedEntry Notice(FeedSeverity severity, string title, string? detail, FeedSource source, string? sessionId = null)
        => new()
        {
            AtUtc = DateTimeOffset.UtcNow,
            Severity = severity,
            Source = source,
            Title = title,
            Detail = detail,
            SessionId = sessionId
        };
}
