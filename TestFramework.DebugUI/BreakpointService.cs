using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI;

/// <summary>
/// The steps the user has asked to stop at.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not in the store. Every step of every attached run asks whether to pause, from the
/// transport's reader thread, before it starts — so the answer has to be immediate and must not
/// wait on a dispatch, a clone, or the UI thread being free. A run held up because the window was
/// busy redrawing would be the tool interfering with the thing it is measuring.
/// </para>
/// <para>
/// Keyed by test, stage and step. The test is what makes the key an identity: a mark is meant to
/// survive running the same test again, and for several releases the key was the stage name and the
/// step's index alone — so a mark set on step 2 of <c>Act</c> in one test stopped step 2 of
/// <c>Act</c> in every other test that ran afterwards, saved to disk and across restarts. Stage
/// names are conventional, which is exactly why they collide.
/// </para>
/// <para>
/// Which test that is comes from the store, read here rather than pushed in from outside. It used to
/// be told — by the board, in its constructor — which meant marks worked only because that one
/// control happened to exist and happened to subscribe. Any second surface offering to set a mark
/// would have filed it against whatever test the board last saw, and the failure would have been
/// silent.
/// </para>
/// </remarks>
public sealed class BreakpointService : IDisposable
{
    private readonly ConcurrentDictionary<Key, bool> marks = new();
    private readonly IDisposable? watching;

    /// <summary>
    /// The run that has asked to be stopped at its next step, whichever step that turns out to be.
    /// </summary>
    /// <remarks>
    /// Held as a session rather than a flag so it cannot be spent by the wrong run: several runs can be
    /// attached at once, and a bare flag would be consumed by whichever of them happened to reach a step
    /// first. Cleared as it is read, which is what makes it a single step rather than a mode.
    /// </remarks>
    private string? stepping;

    /// <summary>
    /// The test the board is currently showing.
    /// </summary>
    /// <remarks>
    /// Marks are set and read against this rather than against a test threaded through every call site,
    /// because the board a mark is set on is always the selected run's — the two cannot disagree. It is the
    /// answering side, on the reader thread, that has to name its own test, and it does.
    /// </remarks>
    private string lookingAt = string.Empty;

    /// <summary>
    /// Creates a service that follows one store's selected run.
    /// </summary>
    /// <param name="store">
    /// Where the test in view is read from. Optional so the marks can be exercised on their own,
    /// with <see cref="NowLookingAt"/> standing in for the selection.
    /// </param>
    public BreakpointService(StateStore<MainState>? store = null)
    {
        watching = store?
            .Bind(RunsSelectors.SelectSelectedTest)
            .Subscribe(NowLookingAt);
    }

    /// <summary>Raised when a breakpoint is added or removed, or the test in view changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Gets or sets a value indicating whether a failing step should stop the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Armed for every attached run, not only the one on screen: the whole point is to catch the run that
    /// broke while you were looking at a different one.
    /// </para>
    /// <para>
    /// A run is only ever asked to pause <em>before</em> a step, so this stops it at the next step rather
    /// than at the moment of failure — and after a failure the next step is the first one of the following
    /// stage, which is normally teardown. That is the useful place to stop: the failed step has settled and
    /// can be read in full, and everything the test built is still standing because nothing has torn it
    /// down yet.
    /// </para>
    /// </remarks>
    public bool BreakOnFailure { get; set; }

    /// <summary>
    /// Says which test's marks the board is showing.
    /// </summary>
    /// <remarks>
    /// Followed from the store by default; public so a test can say it directly, and so a surface with a
    /// notion of "in view" that the store does not model could say it too.
    /// </remarks>
    public void NowLookingAt(string? test)
    {
        string next = test ?? string.Empty;

        if (string.Equals(lookingAt, next, StringComparison.Ordinal))
            return;

        lookingAt = next;
        Changed?.Invoke();
    }

    /// <summary>
    /// Reports whether a step should be held.
    /// </summary>
    /// <remarks>
    /// A pending single step wins over the marks, and is spent whether or not the step also had one. The
    /// alternative — leaving it pending because a real breakpoint answered first — would stop twice at the
    /// same place and read as the step-forward having done nothing.
    /// </remarks>
    public bool ShouldPause(BreakpointQuestion question)
    {
        if (question?.Request is null)
            return false;

        if (TakeStep(question.Request.SessionId))
            return true;

        // A run whose test could not be identified is not matched against marks at all. The alternative is
        // treating "unknown" as a name several runs share, which is the collision this key exists to end.
        return question.Test is { Length: > 0 } test
               && marks.ContainsKey(new Key(test, question.Request.Stage, question.Request.StepId));
    }

    /// <summary>
    /// Steps one run forward: arms a stop at its next step, then lets it go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves are here together because their order is the whole correctness of stepping. A run
    /// let go first can reach its next step and ask about it before this side has said anything, and it
    /// sails past — so arming comes first, and a release that does not happen has to be undone, or the
    /// run would stop unbidden at the next step it ever takes.
    /// </para>
    /// <para>
    /// Which is why there is no way to arm and release separately. A caller holding two methods has to
    /// remember the order and the undo; a caller holding this one cannot get either wrong.
    /// </para>
    /// </remarks>
    /// <param name="sessionId">The run to step.</param>
    /// <param name="release">How to let the run go, reporting whether it was still there to release.</param>
    /// <returns>Whether the run was released.</returns>
    public async Task<bool> StepThroughAsync(string sessionId, Func<Task<bool>> release)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        ArmNextStep(sessionId);

        bool released;
        try
        {
            released = await release();
        }
        catch (Exception)
        {
            CancelStep(sessionId);
            throw;
        }

        if (!released)
            CancelStep(sessionId);

        return released;
    }

    /// <summary>
    /// Asks a run to stop at its next step, without letting it go.
    /// </summary>
    /// <remarks>
    /// For the run that is going to reach its next step on its own — a failure has just been reported and
    /// nothing is holding it. Stepping forward from a breakpoint is <see cref="StepThroughAsync"/>, which
    /// is a different act and owns both halves of it.
    /// </remarks>
    public void ArmNextStep(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            Interlocked.Exchange(ref stepping, sessionId);
    }

    /// <summary>Whether a run is waiting to take a single step.</summary>
    public bool IsStepping(string sessionId)
        => string.Equals(Volatile.Read(ref stepping), sessionId, StringComparison.Ordinal);

    /// <summary>Reports whether the test in view has a breakpoint on a step.</summary>
    public bool IsSet(string stageName, int stepId)
        => lookingAt.Length > 0 && marks.ContainsKey(new Key(lookingAt, stageName, stepId));

    /// <summary>
    /// Adds or removes a breakpoint on the test in view, reporting whether it is now set.
    /// </summary>
    /// <remarks>
    /// A run with no identity cannot be marked. Rather than filing the mark under an empty name — where it
    /// would apply to every other unidentified run — nothing happens and the marker stays unlit, which is
    /// the truthful outcome for a run this window cannot name.
    /// </remarks>
    public bool Toggle(string stageName, int stepId)
    {
        if (lookingAt.Length == 0)
            return false;

        Key key = new(lookingAt, stageName, stepId);

        // One operation rather than a contains-then-remove: the marks are read from the transport's
        // reader thread while the UI thread writes them, and the pair could interleave into a mark that
        // is neither set nor cleared.
        bool nowSet = !marks.TryRemove(key, out _) && marks.TryAdd(key, true);

        Changed?.Invoke();
        return nowSet;
    }

    /// <summary>Removes every breakpoint, and any pending single step with them.</summary>
    public void Clear()
    {
        marks.Clear();
        Interlocked.Exchange(ref stepping, null);
        Changed?.Invoke();
    }

    /// <summary>The breakpoints currently set, in a form that can be written to disk.</summary>
    public ImmutableList<BreakpointMark> Snapshot()
        => [.. marks.Keys
            .OrderBy(key => key.Test, StringComparer.Ordinal)
            .ThenBy(key => key.StageName, StringComparer.Ordinal)
            .ThenBy(key => key.StepId)
            .Select(key => new BreakpointMark { Test = key.Test, Stage = key.StageName, StepId = key.StepId })];

    /// <summary>
    /// Replaces the set with what was saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mark naming no test is discarded. Those are the ones written before the test became part of the
    /// key, and there is no way to work out which test each belonged to — keeping them would mean either
    /// applying them to every test, which is the bug this key fixed, or applying them to none while still
    /// counting them in the settings page. They are one click each to set again.
    /// </para>
    /// <para>
    /// Raises <see cref="Changed"/> once at the end rather than per breakpoint: the board redraws on
    /// that event, and a saved set of twenty would otherwise redraw it twenty times before the window
    /// had even been shown.
    /// </para>
    /// </remarks>
    public void Restore(IEnumerable<BreakpointMark>? marks)
    {
        this.marks.Clear();
        Interlocked.Exchange(ref stepping, null);

        if (marks is not null)
        {
            foreach (BreakpointMark mark in marks)
            {
                if (!string.IsNullOrWhiteSpace(mark?.Stage) && !string.IsNullOrWhiteSpace(mark?.Test))
                    this.marks[new Key(mark.Test, mark.Stage, mark.StepId)] = true;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Stops following the store.</summary>
    public void Dispose() => watching?.Dispose();

    /// <summary>
    /// Withdraws a pending single step.
    /// </summary>
    /// <remarks>
    /// Private because the only caller that needs it is the release that did not happen, and that lives
    /// in <see cref="StepThroughAsync"/> — which is the point of it being one method.
    /// </remarks>
    private void CancelStep(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            Interlocked.CompareExchange(ref stepping, null, sessionId);
    }

    /// <summary>Consumes a pending single step for one run, reporting whether there was one.</summary>
    private bool TakeStep(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        // Compared and cleared in one operation: this is read from the transport's reader thread, and two
        // runs reaching a step together must not both be told to stop.
        return string.Equals(Interlocked.CompareExchange(ref stepping, null, sessionId), sessionId, StringComparison.Ordinal);
    }

    private readonly record struct Key(string Test, string StageName, int StepId);
}
