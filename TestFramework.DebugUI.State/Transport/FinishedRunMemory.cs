using System;
using System.Collections.Generic;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// The runs this window saw finish, most recent first, up to a fixed number of them.
/// </summary>
/// <remarks>
/// <para>
/// A run can be added to after it has finished: a check made on the run a test got back is sent on a connection
/// of its own, after the one that carried the run has closed. That connection ends without a finish of its own,
/// which is exactly what a crashed test host looks like - so without remembering the run had already finished,
/// every such check was announced as a run that disconnected before it finished.
/// </para>
/// <para>
/// Bounded, because watch mode keeps a window open for days and thousands of runs. A late check arrives within
/// moments of its run finishing, so only the recent ones need remembering.
/// </para>
/// </remarks>
internal sealed class FinishedRunMemory
{
    private readonly int capacity;
    private readonly Queue<string> order = new();
    private readonly HashSet<string> finished = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Creates a memory that keeps the given number of runs.</summary>
    /// <param name="capacity">How many finished runs are remembered; the oldest is forgotten first.</param>
    public FinishedRunMemory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    /// <summary>Notes that a run finished.</summary>
    public void Remember(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        lock (gate)
        {
            if (!finished.Add(sessionId))
                return;

            order.Enqueue(sessionId);

            if (order.Count > capacity)
                finished.Remove(order.Dequeue());
        }
    }

    /// <summary>Whether a run is one this window saw finish.</summary>
    public bool Finished(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        lock (gate)
            return finished.Contains(sessionId);
    }
}
