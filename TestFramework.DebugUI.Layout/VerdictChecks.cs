using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Layout;

/// <summary>One check the verdict lists because no pipe can show it.</summary>
/// <param name="Kind">What kind of thing was checked.</param>
/// <param name="Target">What was checked: a bare name, or the text of an expression or a set of steps.</param>
/// <param name="Name">The check, as the test wrote it.</param>
/// <param name="Held">Whether it held every time it was made.</param>
public sealed record VerdictCheck(DebugAssertionTargetKind Kind, string Target, string Name, bool Held)
{
    /// <summary>What was checked, as a line names it: a name in quotes, an expression as written.</summary>
    public string Subject => Kind is DebugAssertionTargetKind.Variable or DebugAssertionTargetKind.Artifact or DebugAssertionTargetKind.Step
        ? $"'{Target}'"
        : Target;
}

/// <summary>
/// Which of a run's checks the verdict shows as pipes, and which it lists.
/// </summary>
/// <remarks>
/// <para>
/// A check on a value a step produced is drawn as a pipe from that value into the verdict. Most checks are not
/// about such a value - a step that completed, a set of steps, an expression - and the verdict used to be drawn
/// only for the first kind, so a run validated entirely by the second showed no verdict at all. Those are listed
/// inside the verdict instead.
/// </para>
/// <para>
/// One function for both the layout, which makes room for the list, and the painter, which fills it - so the room
/// and what fills it cannot disagree.
/// </para>
/// </remarks>
public static class VerdictChecks
{
    /// <summary>How many checks the verdict lists before it says how many more there are.</summary>
    public const int Listed = 8;

    /// <summary>Whether a check is drawn as a pipe from a value some step produced.</summary>
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

    /// <summary>The keys of every value a step produced.</summary>
    public static IReadOnlySet<string> Produced(RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return graph.Stages
            .SelectMany(stage => stage.Steps)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The checks no pipe shows, once each, in the order they were first made.</summary>
    /// <remarks>
    /// A check made more than once - a poll, a loop - is one line, and it held only if it held every time.
    /// </remarks>
    public static ImmutableList<VerdictCheck> Unwired(RunGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        IReadOnlySet<string> produced = Produced(graph);
        List<VerdictCheck> checks = [];
        Dictionary<(DebugAssertionTargetKind, string, string), int> seen = [];

        foreach (AssertionNode assertion in graph.Assertions.Where(assertion => !IsWired(assertion, produced)))
        {
            (DebugAssertionTargetKind, string, string) key = (assertion.TargetKind, assertion.Target, assertion.AssertionName);

            if (seen.TryGetValue(key, out int at))
            {
                checks[at] = checks[at] with { Held = checks[at].Held && assertion.Succeeded };
                continue;
            }

            seen[key] = checks.Count;
            checks.Add(new VerdictCheck(assertion.TargetKind, assertion.Target, assertion.AssertionName, assertion.Succeeded));
        }

        return [.. checks];
    }

    /// <summary>How many lines the verdict's list takes: the checks it shows, and one for the rest.</summary>
    public static int Lines(int checks)
        => Math.Min(checks, Listed) + (checks > Listed ? 1 : 0);
}
