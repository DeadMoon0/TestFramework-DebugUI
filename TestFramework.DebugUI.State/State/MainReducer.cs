using System;
using System.Collections.Immutable;
using System.Linq;
using Axiom.State.Reducers;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Applies the transitions that touch more than one slice.
/// </summary>
/// <remarks>
/// The reducer decides <em>where</em> an event goes; <see cref="RunProjection"/> decides what it
/// means. Keeping those apart is what lets the projection rules be tested exhaustively without a
/// store, and what lets a replayed journal and a live stream share one implementation.
/// <para>
/// Only cross-slice work is here. Anything that writes a single slice belongs to that slice's own
/// reducer, beside it in the folder tree.
/// </para>
/// </remarks>
public sealed class MainReducer : Reducer<MainState>
{
    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public MainReducer()
    {
        On(MainActions.IngestBatch, (state, envelopes) => Ingest(state, envelopes));
        On(MainActions.SelectRun, (state, sessionId) => Select(state, sessionId));
        On(MainActions.RerunSelected, RecordRerunIntent);
    }

    /// <summary>
    /// Notes which test a re-run was asked for, so its run is shown when it arrives.
    /// </summary>
    /// <remarks>
    /// Reads the selection out of one slice and writes another, which is why it is here rather than
    /// in <see cref="ShellReducer"/>. A run that cannot be repeated records nothing: the intent would
    /// never be answered, and it would then steal the next unrelated run of that name.
    /// </remarks>
    private static MainState RecordRerunIntent(MainState state)
    {
        RunSummary? run = Selected(state);

        if (run is null || !RerunCommand.IsAvailableFor(run))
            return state;

        return state with { Shell = state.Shell with { AwaitedRerun = run.Test } };
    }

    private static MainState Ingest(MainState state, ImmutableList<DebugEnvelope> envelopes)
    {
        if (envelopes is null || envelopes.Count == 0)
            return state;

        foreach (DebugEnvelope envelope in envelopes)
            state = IngestOne(state, envelope);

        return state;
    }

    private static MainState IngestOne(MainState state, DebugEnvelope envelope)
    {
        IPipeSignal signal;
        try
        {
            signal = DebugEnvelopeCodec.Unwrap(envelope);
        }
        catch (Exception)
        {
            // A malformed payload is a transport or version problem, not a reason to lose the run
            // that is already on screen. The transport reports it to the feed.
            return state;
        }

        state = TrackSession(state, envelope, signal);

        // Every session is tracked, but only the selected one is projected in full. That is what
        // keeps twenty parallel runs from each dragging a graph through the per-dispatch clone.
        if (!string.Equals(state.Runs.SelectedSessionId, envelope.SessionId, StringComparison.Ordinal))
            return state;

        return state with
        {
            Board = state.Board with { ActiveRun = RunProjection.Apply(state.Board.ActiveRun, signal) }
        };
    }

    private static MainState TrackSession(MainState state, DebugEnvelope envelope, IPipeSignal signal)
    {
        int index = state.Runs.All.FindIndex(run => string.Equals(run.SessionId, envelope.SessionId, StringComparison.Ordinal));

        if (index < 0)
        {
            if (signal is not PipeInitTimelineRunSignal init)
                return state;

            RunSummary summary = new()
            {
                SessionId = envelope.SessionId,
                Name = init.Name,
                StartedAtUtc = envelope.AtUtc,
                IsLive = true,
                FullyQualifiedName = init.Identity?.FullyQualifiedName,
                ProjectPath = ProjectOf(init),
                ProjectFilePath = init.Identity?.ProjectFilePath,
                CanRerun = init.Identity?.CanRerun ?? false,
                Source = SourceLocation.From(init.Identity?.SourceFilePath, init.Identity?.SourceLineNumber ?? 0),

                // The run says how big it is up front, so the home page can show "3 of 14" from the
                // first step rather than a count that grows as it goes.
                Progress = RunProgress.Empty with { Steps = DeclaredSteps(init) }
            };

            // Newest first, so the picker's default order needs no sorting.
            state = state with { Runs = state.Runs with { All = state.Runs.All.Insert(0, summary) } };

            // Nothing selected yet means this is the first run the UI has seen; showing it beats
            // showing an empty board and making the user pick.
            if (state.Runs.SelectedSessionId is null)
                return state with { Runs = state.Runs with { SelectedSessionId = envelope.SessionId } };

            // The run someone asked for by pressing re-run. They are waiting to watch it, so it is
            // shown rather than filed behind the run already on screen — which is the one they just
            // decided was not enough.
            if (state.Shell.AwaitedRerun is { } awaited && string.Equals(awaited, summary.Test, StringComparison.Ordinal))
            {
                return state with
                {
                    Runs = state.Runs with { SelectedSessionId = envelope.SessionId },
                    Shell = state.Shell with { AwaitedRerun = null }
                };
            }

            return state;
        }

        RunSummary existing = state.Runs.All[index];
        RunSummary updated = signal switch
        {
            PipeTimelineRunFinishedSignal => existing with
            {
                IsFinished = true,
                IsLive = false,
                IsWaitingAtBreakpoint = false,
                FinishedAtUtc = envelope.AtUtc
            },
            PipeBreakpointHitRequestSignal => existing with { IsWaitingAtBreakpoint = true },

            // A run listed from disk gets its structure only when someone opens it and the journal
            // is replayed. Counting restarts here rather than adding to whatever a previous replay
            // left behind.
            PipeInitTimelineRunSignal replayed => existing with
            {
                Progress = RunProgress.Empty with { Steps = DeclaredSteps(replayed) },
                ProjectPath = existing.ProjectPath ?? ProjectOf(replayed),
                ProjectFilePath = existing.ProjectFilePath ?? replayed.Identity?.ProjectFilePath,
                CanRerun = existing.CanRerun || (replayed.Identity?.CanRerun ?? false)
            },

            // Any step transition means the run moved on, so it is no longer parked. Tracked on the
            // summary so the picker can badge which of several parallel runs wants attention.
            PipeEntityTransitionSignal { EntityKind: DebugEntityKind.Step } transition => existing with
            {
                IsWaitingAtBreakpoint = false,
                Progress = Advance(existing.Progress, transition)
            },
            PipeAssertionSignal assertion => existing with
            {
                Progress = (existing.Progress ?? RunProgress.Empty).WithAssertion(assertion.Entry.Succeeded)
            },
            _ => existing
        };

        return ReferenceEquals(existing, updated)
            ? state
            : state with { Runs = state.Runs with { All = state.Runs.All.SetItem(index, updated) } };
    }

    /// <summary>
    /// Folds one step transition into a run's running count.
    /// </summary>
    /// <remarks>
    /// Only settled states are recorded. A step on its way through <c>Running</c> or
    /// <c>WaitingForRetry</c> has not decided anything yet, and writing those down would make a
    /// retrying step look failed for as long as it took to succeed.
    /// </remarks>
    private static RunProgress Advance(RunProgress? progress, PipeEntityTransitionSignal transition)
    {
        RunProgress current = progress ?? RunProgress.Empty;

        if (transition.State is not (DebugLifecycleState.Complete
            or DebugLifecycleState.Error
            or DebugLifecycleState.Timeout
            or DebugLifecycleState.Skipped))
        {
            return current;
        }

        return current.WithStep(transition.Stage ?? string.Empty, transition.StepId ?? -1, transition.State);
    }

    /// <summary>
    /// Which project a run should be filed under.
    /// </summary>
    /// <remarks>
    /// The rule itself is Core's, so that grouping runs by project and naming the project in a log
    /// line cannot drift apart. All that is left here is the fallback for a run whose identity was
    /// never resolved.
    /// </remarks>
    private static string? ProjectOf(PipeInitTimelineRunSignal init)
        => init.Identity?.ProjectDisplayName ?? init.ProjectPath;

    /// <summary>
    /// Counts the steps a run announced.
    /// </summary>
    /// <remarks>
    /// Defensive about the structure being incomplete: an older producer, or one that could not
    /// describe itself, still gets listed with the steps it reports as it goes.
    /// </remarks>
    private static int DeclaredSteps(PipeInitTimelineRunSignal init)
    {
        DebugStageState[]? stages = init.RunStructure?.Stages;

        return stages is null ? 0 : stages.Sum(stage => stage.Steps?.Length ?? 0);
    }

    private static MainState Select(MainState state, string sessionId)
    {
        if (string.Equals(state.Runs.SelectedSessionId, sessionId, StringComparison.Ordinal))
            return state;

        // The newly selected run's graph is not held anywhere, so it is rebuilt by whoever owns the
        // source — replayed from its journal, or re-requested from the transport. Clearing here
        // keeps the board from briefly showing the previous run's contents under the new run's name.
        // The step selection goes with it: a stage and index mean nothing in a different run, and
        // keeping them would open the detail panel on whatever happened to sit at that index.
        // The comparison goes too. It is a statement about two named runs, so carrying it across a
        // selection change would badge the new run's values with the old run's verdict.
        //
        // A fresh BoardState rather than a field-by-field reset, so a field added to the board later
        // is cleared here by construction instead of being forgotten.
        return state with
        {
            Runs = state.Runs with { SelectedSessionId = sessionId },
            Board = new BoardState()
        };
    }

    /// <summary>The selected run, or null when nothing is selected or the selection is stale.</summary>
    private static RunSummary? Selected(MainState state)
        => state.Runs.SelectedSessionId is { } sessionId
            ? state.Runs.All.Find(run => string.Equals(run.SessionId, sessionId, StringComparison.Ordinal))
            : null;
}
