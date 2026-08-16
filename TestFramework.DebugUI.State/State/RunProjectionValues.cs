using System;
using System.Collections.Immutable;
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
        DisplayText = signal.Envelope.DisplayText,
        TypeName = signal.Envelope.TypeName,
        SchemaKey = signal.Envelope.SchemaKey
    };

    private static ArtifactNode ProjectArtifact(RunGraph graph, PipeValueUpdateSignal signal)
    {
        ArtifactNode existing = graph.Artifacts.TryGetValue(signal.Name, out ArtifactNode? found)
            ? found
            : new ArtifactNode { Key = signal.Name };

        JObject? core = signal.Envelope.Core as JObject;

        return existing with
        {
            DisplayText = signal.Envelope.DisplayText,
            SchemaKey = signal.Envelope.SchemaKey,

            // Lifecycle comes from the payload rather than a transition: an artifact moves through
            // NotSetup -> Setup -> Cleaned by being mutated in place, which Core reports as a value
            // update rather than an entity transition.
            State = ReadString(core, "state") ?? existing.State,

            // The whole history, every time. Core sends the complete version list on each update, so
            // a consumer that attached late — or replayed a journal missing the earlier events —
            // still shows v1 -> v2 -> v3 rather than only what it happened to witness.
            Versions = ReadVersions(core, existing.Versions)
        };
    }

    private static string? ReadString(JObject? core, string property)
        => core?[property]?.Type == JTokenType.String ? core[property]!.Value<string>() : null;

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
    private static ImmutableList<string> ReadVersions(JObject? core, ImmutableList<string> existing)
    {
        if (core?["versions"] is not JArray versions)
            return existing;

        if (versions.Count == existing.Count)
        {
            bool same = true;
            for (int index = 0; index < versions.Count && same; index++)
                same = string.Equals(versions[index].Value<string>(), existing[index], StringComparison.Ordinal);

            if (same)
                return existing;
        }

        ImmutableList<string>.Builder builder = ImmutableList.CreateBuilder<string>();
        foreach (JToken version in versions)
            builder.Add(version.Value<string>() ?? string.Empty);

        return builder.ToImmutable();
    }

}
