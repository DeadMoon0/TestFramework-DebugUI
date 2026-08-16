using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Turns debugger protocol messages into the run graph the UI renders.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions from (previous graph, signal) to a new graph, deliberately separate from the
/// Axiom reducers that call them. Two reasons: the projection rules are the part worth testing
/// exhaustively and they can be tested without a store, and the same rules must serve both a live
/// pipe stream and a replayed journal — which is only true if they depend on nothing but the
/// signal.
/// </para>
/// <para>
/// Every rule is idempotent. Replaying a journal, or a transport redelivering after a reconnect,
/// must converge on the same graph rather than doubling entries.
/// </para>
/// </remarks>
public static partial class RunProjection
{
    /// <summary>
    /// Builds the initial graph from a run's structure snapshot.
    /// </summary>
    /// <remarks>
    /// Core sends the whole structure up front, so the board can be laid out complete — every stage
    /// and step present and pending — before anything executes, rather than growing a row at a time
    /// as events arrive.
    /// </remarks>
    public static RunGraph ApplyInit(PipeInitTimelineRunSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        ImmutableList<StageNode>.Builder stages = ImmutableList.CreateBuilder<StageNode>();

        foreach (DebugStageState stage in signal.RunStructure.Stages)
            stages.Add(ProjectStage(stage));

        return new RunGraph
        {
            Stages = stages.ToImmutable(),
            Variables = ProjectVariables(signal.RunStructure.Variables),
            Artifacts = ProjectArtifacts(signal.RunStructure.Artifacts)
        };
    }

    private static StageNode ProjectStage(DebugStageState stage)
    {
        ImmutableList<StepNode>.Builder steps = ImmutableList.CreateBuilder<StepNode>();

        // A step's identity is its index within the stage, which is what every later signal
        // addresses it by. Nothing else about a step is unique — names repeat freely.
        for (int index = 0; index < stage.Steps.Length; index++)
            steps.Add(ProjectStep(index, stage.Steps[index]));

        return new StageNode
        {
            Name = stage.Name,
            Description = stage.Description,
            Steps = steps.ToImmutable()
        };
    }

    private static StepNode ProjectStep(int stepId, DebugStepState step)
    {
        return new StepNode
        {
            StepId = stepId,
            Name = step.Name,
            Label = step.LabelOptions?.Label,
            Description = step.Description,

            // Taken from the run rather than worked out here: the planner's answer depends on things
            // this side cannot see, and a layout drawn from a guess would misreport what ran at once.
            LayerIndex = step.LayerIndex,

            // The declared contract, not observed values: this is what lets the board draw edges
            // before a run produces anything, and what answers "why did this run in this order".
            Inputs = ProjectContract(step.IOContract?.Inputs),
            Outputs = ProjectContract(step.IOContract?.Outputs)
        };
    }

    private static ImmutableList<StepIO> ProjectContract(IEnumerable<StepIOEntry>? entries)
    {
        if (entries is null)
            return ImmutableList<StepIO>.Empty;

        ImmutableList<StepIO>.Builder declared = ImmutableList.CreateBuilder<StepIO>();

        foreach (StepIOEntry entry in entries)
        {
            declared.Add(new StepIO
            {
                Key = entry.Key,

                // Carried rather than dropped: a board colours a variable and an artifact
                // differently, and the contract is the only place that distinction is stated.
                Kind = entry.Kind == StepIOKind.Artifact ? DebugValueKind.Artifact : DebugValueKind.Variable
            });
        }

        return declared.ToImmutable();
    }

    private static ImmutableDictionary<string, ValueNode> ProjectVariables(IReadOnlyDictionary<VariableIdentifier, DebugValue> variables)
    {
        ImmutableDictionary<string, ValueNode>.Builder projected = ImmutableDictionary.CreateBuilder<string, ValueNode>(StringComparer.Ordinal);

        foreach (KeyValuePair<VariableIdentifier, DebugValue> entry in variables)
        {
            projected[entry.Key.Identifier] = new ValueNode
            {
                Key = entry.Key.Identifier,
                DisplayText = entry.Value.Envelope.DisplayText,
                TypeName = entry.Value.Envelope.TypeName,
                SchemaKey = entry.Value.Envelope.SchemaKey,
                Description = ValueDescription.From(entry.Value.Envelope.Description)
            };
        }

        return projected.ToImmutable();
    }

    private static ImmutableDictionary<string, ArtifactNode> ProjectArtifacts(IReadOnlyDictionary<ArtifactIdentifier, DebugValue> artifacts)
    {
        ImmutableDictionary<string, ArtifactNode>.Builder projected = ImmutableDictionary.CreateBuilder<string, ArtifactNode>(StringComparer.Ordinal);

        foreach (KeyValuePair<ArtifactIdentifier, DebugValue> entry in artifacts)
        {
            projected[entry.Key.Identifier] = new ArtifactNode
            {
                Key = entry.Key.Identifier,
                DisplayText = entry.Value.Envelope.DisplayText,
                SchemaKey = entry.Value.Envelope.SchemaKey,
                Description = ValueDescription.From(entry.Value.Envelope.Description)
            };
        }

        return projected.ToImmutable();
    }
}
