using System;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI;

/// <summary>
/// Where the panels are, for the window that draws them.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not in the store, for the same reason <see cref="Breakpoints"/> is not: this is not a fact
/// about a run. Nothing a reducer produces depends on it, nothing about a run changes it, and it would put
/// every rearrangement through a clone that every binding in the window is watching. A splitter drag would be
/// the worst of it — and that one is answered twice over, because a drag only commits here when the pointer is
/// released, exactly as the step panel's own width already does.
/// </para>
/// <para>
/// One arrangement per process rather than per window, so a floating window and the main one cannot disagree
/// about where a panel is. Static for the same reason the store and the shell controller are.
/// </para>
/// </remarks>
internal static class Arrangement
{
    private static DockLayout current = DockLayout.Default;

    /// <summary>Raised when the arrangement actually changed.</summary>
    public static event Action? Changed;

    /// <summary>Where every panel is now.</summary>
    public static DockLayout Current => current;

    /// <summary>
    /// Replaces the arrangement with a saved one.
    /// </summary>
    /// <remarks>
    /// Repaired on the way in rather than trusted. A file written by another build may place a panel this one
    /// does not have, or omit one it does, and <see cref="DockLayout.Normalise"/> is what makes either of those
    /// survivable instead of fatal.
    /// </remarks>
    public static void Restore(DockLayout? layout)
    {
        current = (layout ?? DockLayout.Default).Normalise();
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies a change, and says nothing when it changed nothing.
    /// </summary>
    /// <remarks>
    /// The equality check is load-bearing. Clicking the icon of a panel that already has the attention, or
    /// dropping a panel back where it came from, must not make the host tear down and rebuild every panel —
    /// which is what an unconditional event would cause, since the host redraws on it.
    /// </remarks>
    public static void Apply(Func<DockLayout, DockLayout> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        DockLayout next = change(current);

        if (next is null || next.Equals(current))
            return;

        current = next;
        Changed?.Invoke();
    }
}
