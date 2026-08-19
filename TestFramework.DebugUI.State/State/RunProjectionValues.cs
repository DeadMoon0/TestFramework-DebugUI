using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Newtonsoft.Json.Linq;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

public static partial class RunProjection
{
    /// <summary>
    /// Applies a variable or artifact update.
    /// </summary>
    /// <remarks>
    /// Updates are keyed by identifier and replace rather than accumulate, so applying the same one
    /// twice — after a reconnect, or while replaying a journal — converges instead of duplicating.
    /// </remarks>
    public static RunGraph ApplyValueUpdate(RunGraph graph, PipeValueUpdateSignal signal)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(signal);

        return signal.ValueKind switch
        {
            DebugValueKind.Variable => graph with { Variables = graph.Variables.SetItem(signal.Name, ProjectValue(signal)) },
            DebugValueKind.Artifact => graph with { Artifacts = graph.Artifacts.SetItem(signal.Name, ProjectArtifact(graph, signal)) },
            _ => graph
        };
    }

    private static ValueNode ProjectValue(PipeValueUpdateSignal signal) => new()
    {
        Key = signal.Name,
        TypeName = signal.Envelope.TypeName,
        SchemaKey = signal.Envelope.SchemaKey,
        Description = ValueDescription.From(signal.Envelope.Description)
    };

    private static ArtifactNode ProjectArtifact(RunGraph graph, PipeValueUpdateSignal signal)
    {
        ArtifactNode existing = graph.Artifacts.TryGetValue(signal.Name, out ArtifactNode? found)
            ? found
            : new ArtifactNode { Key = signal.Name };

        return existing with
        {
            SchemaKey = signal.Envelope.SchemaKey,
            Description = ValueDescription.From(signal.Envelope.Description),

            // Lifecycle comes from the value rather than a transition: an artifact moves through
            // NotSetup -> Setup -> Cleaned by being mutated in place, which Core reports as a value
            // update rather than an entity transition.
            State = signal.Envelope.Lifecycle?.State ?? existing.State,

            // The whole history, every time. Core sends the complete version list on each update, so
            // a consumer that attached late — or replayed a journal missing the earlier events —
            // still shows v1 -> v2 -> v3 rather than only what it happened to witness.
            Versions = ReadVersions(signal.Envelope.Lifecycle, existing.Versions)
        };
    }

    /// <summary>
    /// Reads the version history, returning the existing list unchanged when the content matches.
    /// </summary>
    /// <remarks>
    /// Reusing the instance is not a micro-optimisation. <see cref="ImmutableList{T}"/> does not
    /// override equality, and Axiom's collection-aware comparer only compares one level
    /// structurally — so a freshly built list with identical content is treated as a change. Since
    /// Core resends the full history on <em>every</em> artifact update, rebuilding unconditionally
    /// would make every update look like a change and re-emit to every binding watching it.
    /// </remarks>
    private static ImmutableList<string> ReadVersions(DebugValueLifecycle? lifecycle, ImmutableList<string> existing)
    {
        if (lifecycle is null)
            return existing;

        return Same(lifecycle.Versions, existing) ? existing : [.. lifecycle.Versions];
    }

    private static bool Same(IReadOnlyList<string> read, ImmutableList<string> existing)
    {
        if (read.Count != existing.Count)
            return false;

        for (int index = 0; index < read.Count; index++)
        {
            if (!string.Equals(read[index], existing[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

}
