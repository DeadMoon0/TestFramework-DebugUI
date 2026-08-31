using System;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

public static partial class RunProjection
{
    /// <summary>
    /// Records a piece of evidence the run produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept at run scope in the order they arrived rather than hung off the step, so the same list
    /// answers both questions worth asking of it: what this step produced, and what the run produced.
    /// A step's own view is a filter over it.
    /// </para>
    /// <para>
    /// Appending is not idempotent on its own and a replayed journal redelivers everything, so an
    /// widget already held is not added twice. Unlike an assertion, a widget cannot legitimately
    /// repeat: the same file, from the same step and attempt, at the same instant, is the same
    /// widget — so the whole list is checked rather than only the last entry.
    /// </para>
    /// </remarks>
    public static RunGraph ApplyWidget(RunGraph graph, PipeWidgetSignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        DebugWidgetEntry entry = signal.Entry;

        WidgetNode node = new()
        {
            Stage = entry.Stage ?? string.Empty,
            StepId = entry.StepId,
            Attempt = entry.Attempt,
            Component = entry.Component ?? string.Empty,
            Kind = entry.Kind,
            Name = entry.Name,
            OccurredAtUtc = entry.OccurredAtUtc,
            Description = ValueDescription.From(entry.Description)
        };

        return graph.Widgets.Contains(node)
            ? graph
            : graph with { Widgets = graph.Widgets.Add(node) };
    }
}
