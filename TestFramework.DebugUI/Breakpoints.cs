using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TestFramework.DebugUI.State;
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
/// </remarks>
public static class Breakpoints
{
    private static readonly ConcurrentDictionary<Key, bool> Set = new();

    /// <summary>
    /// The run that has asked to be stopped at its next step, whichever step that turns out to be.
    /// </summary>
    /// <remarks>
    /// Held as a session rather than a flag so it cannot be spent by the wrong run: several runs can be
    /// attached at once, and a bare flag would be consumed by whichever of them happened to reach a step
    /// first. Cleared as it is read, which is what makes it a single step rather than a mode.
    /// </remarks>
    private static string? stepping;

    /// <summary>
    /// The test the board is currently showing.
    /// </summary>
    /// <remarks>
    /// Marks are set and read against this rather than against a test threaded through every call site,
    /// because the board a mark is set on is always the selected run's — the two cannot disagree. It is the
    /// answering side, on the reader thread, that has to name its own test, and it does.
    /// </remarks>
    private static string lookingAt = string.Empty;

    /// <summary>Raised when a breakpoint is added or removed, or the test in view changes.</summary>
    public static event Action? Changed;

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
    public static bool BreakOnFailure { get; set; }

    /// <summary>
    /// Says which test's marks the board is showing.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="Changed"/> so the markers are redrawn: a different test has different marks, and
    /// leaving the old ones lit would show marks that belong to the run you just navigated away from.
    /// </remarks>
    public static void NowLookingAt(string? test)
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
    public static bool ShouldPause(BreakpointQuestion question)
    {
        if (question?.Request is null)
            return false;

        if (TakeStep(question.Request.SessionId))
            return true;

        // A run whose test could not be identified is not matched against marks at all. The alternative is
        // treating "unknown" as a name several runs share, which is the collision this key exists to end.
        return question.Test is { Length: > 0 } test
               && Set.ContainsKey(new Key(test, question.Request.Stage, question.Request.StepId));
    }

    /// <summary>
    /// Asks a run to stop at its next step, whether or not that step has a breakpoint.
    /// </summary>
    /// <remarks>
    /// Arming this does not release the run. The caller releases it afterwards, and the run then reaches
    /// its next step and asks — by which time this is waiting for it.
    /// </remarks>
    public static void StepOnce(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            Interlocked.Exchange(ref stepping, sessionId);
    }

    /// <summary>Whether a run is waiting to take a single step.</summary>
    public static bool IsStepping(string sessionId)
        => string.Equals(Volatile.Read(ref stepping), sessionId, StringComparison.Ordinal);

    /// <summary>
    /// Withdraws a pending single step.
    /// </summary>
    /// <remarks>
    /// For when the release that was supposed to follow it did not happen, and for a run that has finished
    /// without ever reaching another step. Arming has to come first — a run released before it is armed can
    /// reach its next step and sail past — so the failed case has to be undone rather than avoided, or the
    /// run would stop unbidden at the next step it ever takes.
    /// </remarks>
    public static void CancelStep(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            Interlocked.CompareExchange(ref stepping, null, sessionId);
    }

    /// <summary>Consumes a pending single step for one run, reporting whether there was one.</summary>
    private static bool TakeStep(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        // Compared and cleared in one operation: this is read from the transport's reader thread, and two
        // runs reaching a step together must not both be told to stop.
        return string.Equals(Interlocked.CompareExchange(ref stepping, null, sessionId), sessionId, StringComparison.Ordinal);
    }

    /// <summary>Reports whether the test in view has a breakpoint on a step.</summary>
    public static bool IsSet(string stageName, int stepId)
        => lookingAt.Length > 0 && Set.ContainsKey(new Key(lookingAt, stageName, stepId));

    /// <summary>
    /// Adds or removes a breakpoint on the test in view, reporting whether it is now set.
    /// </summary>
    /// <remarks>
    /// A run with no identity cannot be marked. Rather than filing the mark under an empty name — where it
    /// would apply to every other unidentified run — nothing happens and the marker stays unlit, which is
    /// the truthful outcome for a run this window cannot name.
    /// </remarks>
    public static bool Toggle(string stageName, int stepId)
    {
        if (lookingAt.Length == 0)
            return false;

        Key key = new(lookingAt, stageName, stepId);

        bool nowSet;
        if (Set.ContainsKey(key))
        {
            Set.TryRemove(key, out _);
            nowSet = false;
        }
        else
        {
            Set[key] = true;
            nowSet = true;
        }

        Changed?.Invoke();
        return nowSet;
    }

    /// <summary>Removes every breakpoint, and any pending single step with them.</summary>
    public static void Clear()
    {
        Set.Clear();
        Interlocked.Exchange(ref stepping, null);
        Changed?.Invoke();
    }

    /// <summary>The breakpoints currently set, in a form that can be written to disk.</summary>
    public static ImmutableList<BreakpointMark> Snapshot()
        => [.. Set.Keys
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
    public static void Restore(IEnumerable<BreakpointMark>? marks)
    {
        Set.Clear();
        Interlocked.Exchange(ref stepping, null);

        if (marks is not null)
        {
            foreach (BreakpointMark mark in marks)
            {
                if (!string.IsNullOrWhiteSpace(mark?.Stage) && !string.IsNullOrWhiteSpace(mark?.Test))
                    Set[new Key(mark.Test, mark.Stage, mark.StepId)] = true;
            }
        }

        Changed?.Invoke();
    }

    private readonly record struct Key(string Test, string StageName, int StepId);
}
