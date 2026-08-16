using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Layout.Tests;

/// <summary>
/// Builds the runs the layout tests lay out.
/// </summary>
/// <remarks>
/// Hand-built rather than driven from a real timeline, because the engine's subject is the shape of
/// a graph — layers, and who consumes whom — and constructing those directly is the only way to
/// cover the cases a real timeline makes awkward to produce on demand, such as a value consumed six
/// rows later. That the field mapping from a real run is right is settled elsewhere, by the
/// projection tests that replay one.
/// </remarks>
internal static class GraphBuilder
{
    internal static RunGraph Run(params StageNode[] stages)
        => new() { Stages = [.. stages] };

    /// <summary>The same run, with assertions recorded against the named values.</summary>
    internal static RunGraph Asserting(RunGraph run, params (string Target, bool Succeeded)[] assertions)
        => run with
        {
            Assertions =
            [
                .. assertions.Select(assertion => new AssertionNode
                {
                    Target = assertion.Target,
                    Succeeded = assertion.Succeeded
                })
            ]
        };

    internal static StageNode Stage(string name, params StepNode[] steps)
        => new() { Name = name, Steps = [.. steps] };

    internal static StepNode Step(int stepId, int layer, string? label = null, string[]? inputs = null, string[]? outputs = null, string[]? artifacts = null)
        => new()
        {
            StepId = stepId,
            Name = "step",
            Label = label ?? "step-" + stepId,
            LayerIndex = layer,
            Inputs = Declare(inputs, artifacts),
            Outputs = Declare(outputs, artifacts)
        };

    /// <summary>
    /// Turns key names into declared contract entries, marking the ones named as artifacts.
    /// </summary>
    private static ImmutableList<StepIO> Declare(string[]? keys, string[]? artifacts)
    {
        if (keys is null)
            return ImmutableList<StepIO>.Empty;

        return
        [
            .. keys.Select(key => new StepIO
            {
                Key = key,
                Kind = artifacts is not null && artifacts.Contains(key) ? DebugValueKind.Artifact : DebugValueKind.Variable
            })
        ];
    }

    /// <summary>A stage of steps that each wait for the one before, one per layer.</summary>
    internal static StageNode Chain(string name, int count)
    {
        StepNode[] steps = new StepNode[count];

        for (int index = 0; index < count; index++)
            steps[index] = Step(index, index);

        return Stage(name, steps);
    }
}
