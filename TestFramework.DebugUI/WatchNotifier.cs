using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Axiom.State;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Settings;

namespace TestFramework.DebugUI;

/// <summary>
/// Watches the run list and speaks up when a run finishes.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the tray icon and from the window: this decides <em>whether</em> something is worth
/// saying, the icon decides how to say it, and the window decides where to go when the user acts on
/// it. Keeping the decision here is what lets it be reasoned about — a notification that fires at the
/// wrong moment is worse than none, because the user turns them all off.
/// </para>
/// <para>
/// Only transitions are announced. The list is re-read on every dispatch, so a run that was already
/// finished the last time this looked must not be announced again — otherwise selecting a run, or a
/// refresh finding forty recorded runs, would raise forty notifications.
/// </para>
/// <para>
/// A run is judged a moment after it finishes, not at the moment. The checks a test makes on the run it got back
/// arrive after the finish, so a run that proved everything was still "unproven" right when it finished - and
/// was announced as needing a look.
/// </para>
/// </remarks>
public sealed class WatchNotifier : IDisposable
{
    private readonly CompositeDisposable subscriptions = [];
    private readonly HashSet<string> announced = new(StringComparer.Ordinal);

    /// <summary>Runs that have finished and are waiting out the settle time, with how they look now.</summary>
    private readonly Dictionary<string, RunSummary> settling = new(StringComparer.Ordinal);

    private readonly IScheduler scheduler;

    private bool primed;

    /// <summary>How long after finishing a run is judged.</summary>
    /// <remarks>Late checks follow their run within milliseconds; a second leaves room for a slow machine.</remarks>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    /// <summary>Starts watching.</summary>
    /// <param name="store">The store whose run list is watched.</param>
    /// <param name="scheduler">Where the settle time is waited out; the window's dispatcher unless a test says otherwise.</param>
    public WatchNotifier(StateStore<MainState> store, IScheduler? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        // The window's own context, so the notice is raised on the thread the tray icon belongs to.
        this.scheduler = scheduler ?? new SynchronizationContextScheduler(
            System.Threading.SynchronizationContext.Current
            ?? throw new InvalidOperationException("Watching needs the window's thread, or a scheduler handed in."));

        subscriptions.Add(store
            .Bind(RunsSelectors.SelectAll)
            .Subscribe(OnRuns));
    }

    /// <summary>Raised when a finished run is worth telling the user about.</summary>
    public event Action<RunFinishedNotice>? Finished;

    /// <summary>Whether a notification should be raised at all, and for which runs.</summary>
    public WatchSettings Settings { get; set; } = new();

    /// <summary>Whether the window is out of the way, which is when notifications matter.</summary>
    /// <remarks>
    /// A notification for something already on screen is noise. When the window is up, the board is
    /// already showing the run finish as it happens.
    /// </remarks>
    public bool IsHidden { get; set; }

    public void Dispose() => subscriptions.Dispose();

    private void OnRuns(ImmutableList<RunSummary> runs)
    {
        if (runs is null)
            return;

        // The first look establishes what was already there. Announcing on it would mean every run in
        // the journal was "just finished" the moment the tool opened.
        if (!primed)
        {
            primed = true;

            foreach (RunSummary run in runs.Where(IsSettled))
                announced.Add(run.SessionId);

            return;
        }

        foreach (RunSummary run in runs.Where(IsSettled))
        {
            if (settling.ContainsKey(run.SessionId))
            {
                settling[run.SessionId] = run;
                continue;
            }

            if (!announced.Add(run.SessionId))
                continue;

            settling[run.SessionId] = run;
            subscriptions.Add(scheduler.Schedule(run.SessionId, SettleTime, (_, sessionId) =>
            {
                Judge(sessionId);
                return Disposable.Empty;
            }));
        }
    }

    /// <summary>Announces a run, if it is worth it, as it looks once the settle time is over.</summary>
    private void Judge(string sessionId)
    {
        if (!settling.Remove(sessionId, out RunSummary? run) || !ShouldAnnounce(run))
            return;

        Finished?.Invoke(new RunFinishedNotice
        {
            SessionId = run.SessionId,
            Test = run.ShortName,
            Health = run.Health
        });
    }

    /// <summary>
    /// Whether a run has stopped, one way or another.
    /// </summary>
    /// <remarks>
    /// A run that was abandoned counts. Its host died, which is a thing the person waiting on it very
    /// much wants to hear about, and it is the case a "finished successfully" check would miss.
    /// </remarks>
    private static bool IsSettled(RunSummary run)
        => !run.IsLive && run.Health != RunHealth.Unknown;

    private bool ShouldAnnounce(RunSummary run)
    {
        if (!Settings.NotifyOnFinish || !IsHidden)
            return false;

        return !Settings.NotifyOnlyOnFailure || run.Health != RunHealth.Passed;
    }
}

/// <summary>One run worth mentioning.</summary>
public sealed record RunFinishedNotice
{
    public required string SessionId { get; init; }

    /// <summary>The short label, not the fully qualified name: this goes in a notification.</summary>
    public required string Test { get; init; }

    public required RunHealth Health { get; init; }
}
