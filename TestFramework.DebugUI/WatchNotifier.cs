using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Disposables;
using Axiom.State;
using TestFramework.DebugUI.State;

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
/// </remarks>
public sealed class WatchNotifier : IDisposable
{
    private readonly CompositeDisposable subscriptions = [];
    private readonly HashSet<string> announced = new(StringComparer.Ordinal);

    private bool primed;

    /// <summary>Starts watching.</summary>
    public WatchNotifier()
    {
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs)
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
            if (!announced.Add(run.SessionId))
                continue;

            if (!ShouldAnnounce(run))
                continue;

            Finished?.Invoke(new RunFinishedNotice
            {
                SessionId = run.SessionId,
                Test = run.ShortName,
                Health = run.Health
            });
        }
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
