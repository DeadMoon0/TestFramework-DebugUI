using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout;

/// <summary>One check a run made, as the board draws it: a card of its own in the assertions stage.</summary>
/// <param name="Kind">What kind of thing was checked.</param>
/// <param name="Target">What was checked: a bare name, or the text of an expression or a set of steps.</param>
/// <param name="Name">The check, as the test wrote it.</param>
/// <param name="Held">Whether it held every time it was made.</param>
/// <param name="Value">The value a step produced that this check is about, which a pipe runs from; null when there is none.</param>
public sealed record RunCheck(DebugAssertionTargetKind Kind, string Target, string Name, bool Held, string? Value)
{
    /// <summary>What was checked, as a card names it: a name in quotes, an expression as written.</summary>
    public string Subject => Kind is DebugAssertionTargetKind.Variable or DebugAssertionTargetKind.Artifact or DebugAssertionTargetKind.Step
        ? $"'{Target}'"
        : Target;
}

/// <summary>
/// The checks a run made, as the cards of the stage the board draws them in.
/// </summary>
/// <remarks>
/// <para>
/// Every check is a card of its own, so a check on a value a step produced takes that value as its input and a
/// pipe runs to it from the step that made it - the same as a step consuming it. A check on anything else - a
/// step that completed, a set of steps, an expression - is a card with nothing to plug in.
/// </para>
/// <para>
/// One function for the layout, which places the cards and their pipes, and for the view that draws them.
/// </para>
/// </remarks>
public static class RunChecks
{
    /// <summary>The name the assertions stage is laid out under.</summary>
    /// <remarks>One no stage of a run can have, so it can never be confused with one the run executed.</remarks>
    public const string StageName = "$assertions";

    /// <summary>How many check cards sit side by side before the next ones start a row of their own.</summary>
    public const int PerRow = 3;

    /// <summary>Whether a check is about a value some step produced, and so is piped from it.</summary>
    /// <remarks>
    /// Never a check on a step or on a set of steps: it carries the step's name, and a variable that happened to
    /// share it would be drawn as the thing checked.
    /// </remarks>
    public static bool IsWired(AssertionNode assertion, IReadOnlySet<string> produced)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(produced);

        return assertion.TargetKind is not (DebugAssertionTargetKind.Step or DebugAssertionTargetKind.StepList)
               && produced.Contains(assertion.Target);
    }

    /// <summary>Every check the run made, once each, in the order each was first made.</summary>
    /// <remarks>
    /// A check made more than once - a poll, a loop - is one card, and it held only if it held every time.
    /// </remarks>
    public static ImmutableList<RunCheck> Of(RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        IReadOnlySet<string> produced = graph.Stages
            .SelectMany(stage => stage.Steps)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Key)
            .ToHashSet(StringComparer.Ordinal);

        List<RunCheck> checks = [];
        Dictionary<(DebugAssertionTargetKind, string, string), int> seen = [];

        foreach (AssertionNode assertion in graph.Assertions)
        {
            (DebugAssertionTargetKind, string, string) key = (assertion.TargetKind, assertion.Target, assertion.AssertionName);

            if (seen.TryGetValue(key, out int at))
            {
                checks[at] = checks[at] with { Held = checks[at].Held && assertion.Succeeded };
                continue;
            }

            seen[key] = checks.Count;
            checks.Add(new RunCheck(
                assertion.TargetKind,
                assertion.Target,
                assertion.AssertionName,
                assertion.Succeeded,
                IsWired(assertion, produced) ? assertion.Target : null));
        }

        return [.. checks];
    }
}
