using System;
using TestFramework.Core.Debugger;
using System.Linq;

namespace TestFramework.DebugUI.State;

public static partial class RunProjection
{
    /// <summary>
    /// Records an assertion result at run scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assertions are kept apart from the log collections rather than folded into them. A run's
    /// assertions are the summary a reader wants first — what was checked, and what held — and
    /// burying them in the log stream means reconstructing that by reading prose. Keeping them
    /// structured also means expected and actual stay comparable values rather than pre-rendered
    /// text.
    /// </para>
    /// <para>
    /// Run scope rather than step scope because an assertion can be made about a variable or
    /// artifact outside any step, and because the useful view is the whole run's checks in order.
    /// </para>
    /// </remarks>
    public static RunGraph ApplyAssertion(RunGraph graph, PipeAssertionSignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        DebugAssertionEntry entry = signal.Entry;

        AssertionNode node = new()
        {
            OccurredAtUtc = entry.OccurredAtUtc,
            AssertionName = entry.AssertionName,
            Arguments = [.. entry.Arguments.Select(argument => new LogFact { Name = argument.Name, Value = argument.Value })],
            Target = entry.Target,
            TargetKind = entry.TargetKind,
            Succeeded = entry.Succeeded,
            Actual = ValueDescription.From(entry.Actual),
            Scope = entry.AssertionScope
        };

        // Appending is not idempotent on its own, and a replayed journal redelivers every assertion.
        // A run does legitimately assert the same thing more than once, though — a poll that checks
        // a value repeatedly, or the same assertion inside a loop — so only an exact redelivery of
        // the entry just recorded is suppressed.
        if (graph.Assertions.Count > 0 && graph.Assertions[^1] == node)
            return graph;

        return graph with { Assertions = graph.Assertions.Add(node) };
    }
}
