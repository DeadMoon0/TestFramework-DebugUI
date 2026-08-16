using System;
using System.Collections.Concurrent;
using TestFramework.Core.Debugger;

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

    /// <summary>Raised when a breakpoint is added or removed.</summary>
    public static event Action? Changed;

    /// <summary>Reports whether a step should be held.</summary>
    public static bool ShouldPause(PipeBreakpointHitRequestSignal request)
        => request is not null && Set.ContainsKey(new Key(request.Stage, request.StepId));

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

    /// <summary>Removes every breakpoint.</summary>
    public static void Clear()
    {
        Set.Clear();
        Changed?.Invoke();
    }

    private readonly record struct Key(string StageName, int StepId);
}
