using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.Core.Debugger;

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
public sealed class ShellController : IDisposable
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
    private readonly TestRerunner rerunner;
    private readonly BaselineResolver baselines;

    private bool disposed;

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
        this.runsDirectory = runsDirectory ?? SafeRunsDirectory();

        ingest = new RunIngestService(store, ingestWindow);
        pipe = new PipeRunEventSource(pipeName);

        pipe.EnvelopeReceived += OnLiveEnvelope;
        pipe.Notice += Report;
        pipe.ConnectionsChanged += OnConnectionsChanged;
        pipe.PauseAtBreakpoint = ShouldPause;

        rerunner = new TestRerunner(Report);
        baselines = new BaselineResolver(this.runsDirectory);
    }

    /// <summary>Gets the live transport, for the window to drive breakpoints and cancellation.</summary>
    public PipeRunEventSource Pipe => pipe;

    /// <summary>
    /// Gets or sets the test of whether a step should be held at its breakpoint.
    /// </summary>
    /// <remarks>
    /// Every step asks before it runs, so with nothing set here no step is ever held. That default
    /// is the safe one: a consumer that paused by default would stall each step of every attached
    /// run until the producer gave up waiting.
    /// </remarks>
    public Func<PipeBreakpointHitRequestSignal, bool>? PauseAtBreakpoint { get; set; }

    /// <summary>
    /// Starts listening for live runs and lists the ones already recorded.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        pipe.Start();
        store.Dispatch(RunActions.SetTransportStatus, TransportStatus.Listening);

        RefreshRecordedRuns();
    }

    /// <summary>
    /// Re-reads the recorded runs and adds any that are new to the picker.
    /// </summary>
    public void RefreshRecordedRuns()
    {
        if (runsDirectory is null)
            return;

        try
        {
            ImmutableList<RunSummary> recorded =
            [
                .. JournalRunEventSource.ListRuns(runsDirectory).Select(run => new RunSummary
                {
                    SessionId = run.SessionId,
                    Name = run.Name,
                    StartedAtUtc = run.StartedAtUtc,
                    IsLive = false,

                    // A recorded run that never reached its finish signal is one whose host died.
                    // It is listed rather than hidden: a run that stopped unexpectedly is usually
                    // the one worth looking at.
                    IsFinished = run.IsFinished,
                    FinishedAtUtc = run.FinishedAtUtc,
                    FullyQualifiedName = run.FullyQualifiedName,
                    ProjectPath = run.ProjectPath,

                    // Carried through from the sidecar so a run can be repeated straight from the
                    // list. Waiting for someone to open it first made the button's availability
                    // depend on what they had happened to click.
                    ProjectFilePath = run.ProjectFilePath,
                    CanRerun = run.CanRerun
                })
            ];

            store.Dispatch(RunActions.AddRecordedRuns, recorded);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Report(Notice(FeedSeverity.Error, "Recorded runs could not be listed.", e.Message, FeedSource.Journal));
        }
    }

    /// <summary>
    /// Shows a run, rebuilding its board from whatever record of it exists.
    /// </summary>
    /// <remarks>
    /// Only the selected run is projected, so switching means replaying: from the events kept in
    /// memory for a live run, and from the journal for one that was recorded. A run with neither is
    /// listed but has nothing to show, which is reported rather than left as an empty board.
    /// </remarks>
    public void SelectRun(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ObjectDisposedException.ThrowIf(disposed, this);

        store.Dispatch(RunActions.SelectRun, sessionId);

        if (histories.TryGetValue(sessionId, out RunHistory? history))
        {
            foreach (DebugEnvelope envelope in history.Snapshot())
                ingest.Accept(envelope);

            ingest.Flush();

            RefreshDiff(sessionId);

            if (!history.Complete)
            {
                Report(Notice(
                    FeedSeverity.Warning,
                    "Only the recent part of this run could be shown.",
                    $"It produced more than {RetainedEventsPerRun:N0} events while it was not selected. Once it finishes, opening it again replays the whole run from its journal.",
                    FeedSource.Run,
                    sessionId));
            }

            return;
        }

        ReplayRecorded(sessionId);
    }

    /// <summary>
    /// Shows one step's detail.
    /// </summary>
    public void SelectStep(string stageName, int stepId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        store.Dispatch(RunActions.SelectStep, new StepSelection { StageName = stageName, StepId = stepId });
    }

    /// <summary>
    /// Releases the selected run from its breakpoint.
    /// </summary>
    public async Task<bool> ContinueSelectedRunAsync()
    {
        string? sessionId = store.GetValue(state => state.SelectedSessionId);
        if (sessionId is null)
            return false;

        bool released = await pipe.ContinueAsync(sessionId);

        if (!released)
        {
            Report(Notice(
                FeedSeverity.Warning,
                "The run could not be released.",
                "It is not attached, or it is no longer waiting at a breakpoint.",
                FeedSource.Transport,
                sessionId));
        }

        return released;
    }

    /// <summary>
    /// Asks the selected run to stop.
    /// </summary>
    /// <remarks>
    /// The run stops itself and unwinds through its cleanup stage. Nothing here kills a process:
    /// that would strand the containers, temp files and database rows cleanup exists to remove.
    /// </remarks>
    public async Task<bool> CancelSelectedRunAsync(string? reason = null)
    {
        string? sessionId = store.GetValue(state => state.SelectedSessionId);
        if (sessionId is null)
            return false;

        bool asked = await pipe.CancelAsync(sessionId, reason ?? "Stopped from the debugger.");

        Report(asked
            ? Notice(FeedSeverity.Info, "The run was asked to stop.", "It will unwind through its cleanup stage.", FeedSource.Transport, sessionId)
            : Notice(FeedSeverity.Warning, "The run could not be asked to stop.", "It is no longer attached.", FeedSource.Transport, sessionId));

        return asked;
    }

    /// <summary>
    /// Runs the selected run's test again.
    /// </summary>
    /// <remarks>
    /// The new run arrives over the pipe like any other, so nothing here waits for it or tracks it.
    /// This only starts the process and reports the cases the pipe cannot: that it could not be
    /// started, or that the run never carried enough identity to be repeated.
    /// </remarks>
    public Task<bool> RerunSelectedAsync()
    {
        string? sessionId = store.GetValue(state => state.SelectedSessionId);

        RunSummary? run = sessionId is null
            ? null
            : store.GetValue(state => state.Runs.Find(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)));

        if (run is null)
            return Task.FromResult(false);

        // Said before the process starts, so the run cannot arrive before the UI knows to show it.
        // A fast test finishes in a couple of hundred milliseconds; claiming the intent afterwards
        // would be a race the user loses on exactly the runs that are quickest to read.
        if (RerunCommand.IsAvailableFor(run))
            store.Dispatch(RunActions.AwaitRerun, run.Test);

        return rerunner.RerunAsync(run);
    }

    /// <summary>Adds an entry to the message feed.</summary>
    public void Report(FeedEntry entry)
    {
        if (entry is null || disposed)
            return;

        store.Dispatch(RunActions.AppendFeedEntry, entry);
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

        pipe.Dispose();
        ingest.Dispose();
    }

    /// <summary>
    /// Keeps the reported status level with how many runs are actually connected.
    /// </summary>
    /// <remarks>
    /// Only ever moves between listening and attached. A transport that has faulted stays faulted —
    /// that is a fact about the tool rather than about how busy it happens to be, and quietly
    /// downgrading it to "listening" because nothing is connected would hide it.
    /// </remarks>
    private void OnConnectionsChanged()
    {
        if (disposed)
            return;

        TransportStatus current = store.GetValue(state => state.Shell.Transport);

        if (current == TransportStatus.Faulted || current == TransportStatus.Idle)
            return;

        TransportStatus next = pipe.AttachedRunCount > 0 ? TransportStatus.Attached : TransportStatus.Listening;

        if (next != current)
            store.Dispatch(RunActions.SetTransportStatus, next);
    }

    private void OnLiveEnvelope(DebugEnvelope envelope)
    {
        // Kept for every session, not just the selected one. An unselected run is only a name in the
        // picker until someone opens it, and these events are what the board is rebuilt from.
        histories.GetOrAdd(envelope.SessionId, _ => new RunHistory()).Add(envelope);

        if (store.GetValue(state => state.Shell.Transport) != TransportStatus.Attached)
            store.Dispatch(RunActions.SetTransportStatus, TransportStatus.Attached);

        ingest.Accept(envelope);

        // A live run's values are still arriving, so comparing it mid-flight would badge values that
        // have not been assigned yet as removed. Its finish is the moment they settle, and the moment
        // the comparison is worth making.
        if (envelope.Kind == PipeSignalKind.TimelineRunFinished
            && string.Equals(store.GetValue(state => state.SelectedSessionId), envelope.SessionId, StringComparison.Ordinal))
        {
            ingest.Flush();
            RefreshDiff(envelope.SessionId);
        }
    }

    private void ReplayRecorded(string sessionId)
    {
        if (runsDirectory is null)
            return;

        AvailableRun? recorded = JournalRunEventSource
            .ListRuns(runsDirectory)
            .FirstOrDefault(run => string.Equals(run.SessionId, sessionId, StringComparison.Ordinal));

        if (recorded is null)
        {
            Report(Notice(
                FeedSeverity.Warning,
                "This run has nothing to show.",
                "It was not recorded, and the events it sent while it was attached are no longer held.",
                FeedSource.Run,
                sessionId));

            return;
        }

        JournalRunEventSource source = new(recorded.JournalPath);
        source.EnvelopeReceived += ingest.Accept;
        source.Notice += Report;

        try
        {
            source.Start();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Report(Notice(FeedSeverity.Error, "The recorded run could not be replayed.", e.Message, FeedSource.Journal, sessionId));
        }
        finally
        {
            source.EnvelopeReceived -= ingest.Accept;
            source.Notice -= Report;
            ingest.Flush();
        }

        RefreshDiff(sessionId);
    }

    /// <summary>
    /// Works out how the selected run's values compare with the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off the calling thread, because it reads journals: a comparison is worth waiting for but never
    /// worth freezing the window for. The board is already on screen by the time this starts, so the
    /// diff arrives as a later state change and the rail badges itself when it does.
    /// </para>
    /// <para>
    /// The result is only dispatched if the run is still the selected one. Someone clicking through
    /// several runs quickly would otherwise have an earlier run's comparison land on a later run's
    /// values, which is the one outcome worse than showing no comparison at all.
    /// </para>
    /// </remarks>
    public void RefreshDiff(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (disposed)
            return;

        RunSummary? run = store.GetValue(state =>
            state.Runs.Find(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)));

        if (run is null)
            return;

        RunGraph graph = store.GetValue(state => state.ActiveRun);
        ImmutableList<RunSummary> runs = store.GetValue(state => state.Runs);

        _ = Task.Run(() =>
        {
            ValueDiff diff;

            try
            {
                diff = baselines.Resolve(run, graph, runs);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                diff = RunBaselineSelector.Unavailable("The comparison against an earlier run could not be computed.");
            }

            if (disposed)
                return;

            if (!string.Equals(store.GetValue(state => state.SelectedSessionId), sessionId, StringComparison.Ordinal))
                return;

            store.Dispatch(RunActions.SetValueDiff, diff);
        });
    }

    private bool ShouldPause(PipeBreakpointHitRequestSignal request)
    {
        Func<PipeBreakpointHitRequestSignal, bool>? test = PauseAtBreakpoint;
        return test is not null && test(request);
    }

    /// <summary>
    /// Resolves where recorded runs live, tolerating not being able to.
    /// </summary>
    /// <remarks>
    /// A machine with no journal root is the ordinary case before the tool has been installed. The
    /// window still opens and still watches live runs; there is simply nothing recorded to list.
    /// </remarks>
    private static string? SafeRunsDirectory()
    {
        try
        {
            return DebugJournal.RunsDirectory;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
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

    /// <summary>What one session has sent, kept so its board can be rebuilt when it is selected.</summary>
    private sealed class RunHistory
    {
        private readonly object gate = new();
        private readonly Queue<DebugEnvelope> envelopes = new();

        /// <summary>Whether every event this run sent is still held.</summary>
        internal bool Complete { get; private set; } = true;

        internal void Add(DebugEnvelope envelope)
        {
            lock (gate)
            {
                envelopes.Enqueue(envelope);

                while (envelopes.Count > RetainedEventsPerRun)
                {
                    envelopes.Dequeue();
                    Complete = false;
                }
            }
        }

        internal ImmutableList<DebugEnvelope> Snapshot()
        {
            lock (gate)
                return [.. envelopes];
        }
    }
}
