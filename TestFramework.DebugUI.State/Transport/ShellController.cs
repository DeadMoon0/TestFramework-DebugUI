using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Shell;
using TestFramework.DebugUI.State.Shell.Feed;

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
    /// Creates a controller over a store.
    /// </summary>
    /// <param name="store">The store to drive.</param>
    /// <param name="runsDirectory">Where recorded runs live. Defaults to the framework's journal.</param>
    /// <param name="pipeName">The pipe to listen on. Defaults to the framework's well-known name.</param>
    /// <param name="ingestWindow">How long to coalesce events before dispatching.</param>
    /// <summary>
    /// Gets where recorded runs are read from, when there is somewhere.
    /// </summary>
    /// <remarks>
    /// Exposed so importing a bundle can put its runs where the listing already looks, rather than inventing a
    /// second place for runs to live and a second way to find them.
    /// </remarks>
    public string? RunsDirectory => runsDirectory;

    public ShellController(StateStore<MainState> store, string? runsDirectory = null, string? pipeName = null, TimeSpan? ingestWindow = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.runsDirectory = runsDirectory ?? DefaultRunsDirectory();

        ingest = new RunIngestService(store, ingestWindow);
        pipe = new PipeRunEventSource(pipeName);

        pipe.EnvelopeReceived += OnLiveEnvelope;
        pipe.Notice += Report;
        pipe.ConnectionsChanged += OnConnectionsChanged;
        pipe.PauseAtBreakpoint = ShouldPause;
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

        RefreshRecordedRuns();
        ReportRecordingsFromOtherBuilds();
    }

    /// <summary>
    /// Says once, at startup, that some recordings on disk are not this build's to read.
    /// </summary>
    /// <remarks>
    /// The debug protocol is versioned and this build reads one version of it. A recording from another is
    /// left out of the picker rather than opened as an empty board — and saying so once is the difference
    /// between a deliberate omission and a tool that appears to have lost somebody's runs. The files are not
    /// touched.
    /// </remarks>
    private void ReportRecordingsFromOtherBuilds()
    {
        if (runsDirectory is null)
            return;

        try
        {
            int count = JournalRunEventSource.CountFromOtherBuilds(runsDirectory);

            if (count == 0)
                return;

            Report(Notice(
                FeedSeverity.Info,
                count == 1
                    ? "One recorded run was made by another build and is not listed."
                    : $"{count} recorded runs were made by another build and are not listed.",
                $"This build reads debug protocol v{DebugProtocol.Version}. The recordings are still in {runsDirectory}.",
                FeedSource.Journal));
        }
        catch (Exception e)
        {
            Report(Notice(FeedSeverity.Error, "Recorded runs could not be counted.", e.Message, FeedSource.Journal));
        }
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
                    CanRerun = run.CanRerun,
                    Source = run.Source,
                    EventCount = run.EventCount,
                    JournalPath = run.JournalPath
                })
            ];

            store.Dispatch(RunsActions.AddRecorded, Located(recorded));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Report(Notice(FeedSeverity.Error, "Recorded runs could not be listed.", e.Message, FeedSource.Journal));
        }
    }


    /// <summary>
    /// Points each run at a project that exists on this machine, where the recorded one does not.
    /// </summary>
    /// <remarks>
    /// A run that arrived in a bundle records the sender's paths, so re-run and the editor buttons would both be
    /// unavailable on it even though everything needed to find the project locally is present. Translated here,
    /// once, at the point the picker is built, so nothing downstream has to know a run came from elsewhere.
    /// The journal itself is untouched: this is the projection, not the record.
    /// </remarks>
    private static ImmutableList<RunSummary> Located(ImmutableList<RunSummary> runs)
    {
        ImmutableDictionary<string, string> resolved = ProjectResolution.ResolveAll(runs);

        if (resolved.Count == 0)
            return runs;

        return
        [
            .. runs.Select(run => resolved.TryGetValue(run.SessionId, out string? local)
                ? run with { ProjectFilePath = local }
                : run)
        ];
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

        store.Dispatch(MainActions.SelectRun, sessionId);

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
        store.Dispatch(BoardActions.SelectStep, new StepSelection { StageName = stageName, StepId = stepId });
    }

    /// <summary>
    /// Releases the selected run from its breakpoint.
    /// </summary>
    public async Task<bool> ContinueSelectedRunAsync()
    {
        string? sessionId = store.GetValue(state => state.Runs.SelectedSessionId);
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
        string? sessionId = store.GetValue(state => state.Runs.SelectedSessionId);
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
    /// One dispatch, and everything else follows from it: <see cref="MainReducer"/> records which
    /// test is being waited for, and <see cref="RunsEffects"/> starts the process. That ordering is
    /// guaranteed rather than arranged — a dispatch reduces before its effects are queued — which is
    /// what the old hand-ordered version here was trying to achieve by dispatching first and starting
    /// the process second.
    /// </remarks>
    public void RerunSelected()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        store.Dispatch(MainActions.RerunSelected);
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
            store.Dispatch(ShellActions.SetTransportStatus, next);
    }

    private void OnLiveEnvelope(DebugEnvelope envelope)
    {
        // Kept for every session, not just the selected one. An unselected run is only a name in the
        // picker until someone opens it, and these events are what the board is rebuilt from.
        histories.GetOrAdd(envelope.SessionId, _ => new RunHistory()).Add(envelope);

        // Before the events are handed on, because both things this reads have to be known by the time the
        // run's next step asks whether to stop — which is sooner than the store will have heard about it.
        WatchForBreakpointContext(envelope);

        if (store.GetValue(state => state.Shell.Transport) != TransportStatus.Attached)
            store.Dispatch(ShellActions.SetTransportStatus, TransportStatus.Attached);

        ingest.Accept(envelope);

        // A live run's values are still arriving, so comparing it mid-flight would badge values that
        // have not been assigned yet as removed. Its finish is the moment they settle, and the moment
        // the comparison is worth making.
        if (envelope.Kind == PipeSignalKind.TimelineRunFinished
            && string.Equals(store.GetValue(state => state.Runs.SelectedSessionId), envelope.SessionId, StringComparison.Ordinal))
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
    /// Asks for the selected run's comparison against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// The work itself is <see cref="ComparisonEffects"/>. What is left here is the timing: a
    /// comparison needs the run's graph, and the graph does not exist when the selection changes —
    /// it is replayed from the retained events or from the journal first. This is called once that
    /// replay has been flushed, which is the only point at which there is something to compare.
    /// </remarks>
    public void RefreshDiff(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (disposed)
            return;

        store.Dispatch(ComparisonActions.Refresh, sessionId);
    }

    /// <summary>
    /// Answers a waiting step, naming the test it belongs to.
    /// </summary>
    /// <remarks>
    /// The test comes from <see cref="tests"/> rather than from the store. Both are filled from the same
    /// announcement, but the store's copy arrives through a coalescing dispatch — so for the first step of a
    /// run the store may not have it yet, and a mark on that step would be missed exactly once per run.
    /// </remarks>
    private bool ShouldPause(PipeBreakpointHitRequestSignal request)
    {
        Func<BreakpointQuestion, bool>? test = PauseAtBreakpoint;

        if (test is null)
            return false;

        return test(new BreakpointQuestion
        {
            Request = request,
            Test = tests.TryGetValue(request.SessionId, out string? named) ? named : null
        });
    }

    /// <summary>
    /// Notes what test a run is an execution of, and tells anyone waiting when one of its steps breaks.
    /// </summary>
    /// <remarks>
    /// Read straight off the envelope stream, in order and on the reader thread, which is what makes both
    /// answers available before the next step of that run asks anything.
    /// </remarks>
    private void WatchForBreakpointContext(DebugEnvelope envelope)
    {
        try
        {
            switch (envelope.Kind)
            {
                case PipeSignalKind.InitTimelineRun:
                    if (DebugEnvelopeCodec.Unwrap(envelope) is PipeInitTimelineRunSignal init)
                    {
                        // The fully qualified name when the run could identify itself, and its display name
                        // otherwise — the same fallback the picker shows, so a mark is filed under the name
                        // the reader saw when they set it.
                        tests[envelope.SessionId] = RunSummary.TestNameOf(init.Identity?.FullyQualifiedName, init.Name);
                    }

                    break;

                case PipeSignalKind.TimelineRunFinished:
                    // The last message a session ever sends, so nothing will ask about this run again. Left
                    // in place it would accumulate for the life of the process, which under watch mode is
                    // measured in days and thousands of runs.
                    tests.TryRemove(envelope.SessionId, out _);
                    break;

                // A step about to be retried transitions to WaitingForRetry rather than to Error, so
                // matching on the state is what excludes a failure the run is going to recover from.
                case PipeSignalKind.EntityTransition when StepEndedBadly is not null:
                    if (DebugEnvelopeCodec.Unwrap(envelope) is PipeEntityTransitionSignal
                        {
                            EntityKind: DebugEntityKind.Step,
                            State: DebugLifecycleState.Error or DebugLifecycleState.Timeout,
                            Stage: { Length: > 0 } stage,
                            StepId: int stepId
                        })
                    {
                        StepEndedBadly(new StepFailureNotice
                        {
                            SessionId = envelope.SessionId,
                            Stage = stage,
                            StepId = stepId
                        });
                    }

                    break;

                default:
                    break;
            }
        }
        catch (Exception e)
        {
            // A frame this build cannot read must not take the transport down with it. Losing the test name
            // costs the run its breakpoints; throwing here would cost it the whole connection.
            Debug.WriteLine(e);
        }
    }

    /// <summary>
    /// Resolves where recorded runs live, tolerating not being able to.
    /// </summary>
    /// <remarks>
    /// A machine with no journal root is the ordinary case before the tool has been installed. The
    /// window still opens and still watches live runs; there is simply nothing recorded to list.
    /// </remarks>
    /// <summary>
    /// The folder runs are recorded in, or null when it cannot be determined.
    /// </summary>
    /// <remarks>
    /// Public because the effects need the same answer: <see cref="ComparisonEffects"/> reads earlier
    /// runs out of this folder, and it is built when the store is, before any controller exists. Two
    /// separate defaults would mean the comparison reading a different folder than the one the runs
    /// were listed from.
    /// </remarks>
    public static string? DefaultRunsDirectory()
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
