using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

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
/// Keyed by stage and step rather than by session, so a breakpoint set on a test still applies when
/// that test is run again — which is the point of setting one.
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

    /// <summary>Raised when a breakpoint is added or removed.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Reports whether a step should be held.
    /// </summary>
    /// <remarks>
    /// A pending single step wins over the marks, and is spent whether or not the step also had one. The
    /// alternative — leaving it pending because a real breakpoint answered first — would stop twice at the
    /// same place and read as the step-forward having done nothing.
    /// </remarks>
    public static bool ShouldPause(PipeBreakpointHitRequestSignal request)
    {
        if (request is null)
            return false;

        if (TakeStep(request.SessionId))
            return true;

        return Set.ContainsKey(new Key(request.Stage, request.StepId));
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
    /// For when the release that was supposed to follow it did not happen. Arming has to come first — a
    /// run released before it is armed can reach its next step and sail past — so the failed case has to
    /// be undone rather than avoided, or the run would stop unbidden at the next step it ever takes.
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

    /// <summary>Reports whether a step has a breakpoint on it.</summary>
    public static bool IsSet(string stageName, int stepId) => Set.ContainsKey(new Key(stageName, stepId));

    /// <summary>Adds or removes a breakpoint, reporting whether it is now set.</summary>
    public static bool Toggle(string stageName, int stepId)
    {
        Key key = new(stageName, stepId);

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
            .OrderBy(key => key.StageName, StringComparer.Ordinal)
            .ThenBy(key => key.StepId)
            .Select(key => new BreakpointMark { Stage = key.StageName, StepId = key.StepId })];

    /// <summary>
    /// Replaces the set with what was saved.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="Changed"/> once at the end rather than per breakpoint: the board redraws on
    /// that event, and a saved set of twenty would otherwise redraw it twenty times before the window
    /// had even been shown.
    /// </remarks>
    public static void Restore(IEnumerable<BreakpointMark>? marks)
    {
        Set.Clear();
        Interlocked.Exchange(ref stepping, null);

        if (marks is not null)
        {
            foreach (BreakpointMark mark in marks)
            {
                if (!string.IsNullOrWhiteSpace(mark?.Stage))
                    Set[new Key(mark.Stage, mark.StepId)] = true;
            }
        }

        Changed?.Invoke();
    }

    private readonly record struct Key(string StageName, int StepId);
}
