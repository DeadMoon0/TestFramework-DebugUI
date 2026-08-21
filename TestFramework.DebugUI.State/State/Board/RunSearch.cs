using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// One thing a search found, and how to get to it.
/// </summary>
/// <remarks>
/// Every hit carries the address of what it found rather than the thing itself, for the same reason
/// <see cref="StepSelection"/> does: the graph is replaced as events arrive, so a held node would be a stale
/// copy and clicking the result would open something that no longer looks like that.
/// </remarks>
public sealed record SearchHit
{
    /// <summary>Gets which part of the run this came from.</summary>
    public required SearchScope Scope { get; init; }

    /// <summary>Gets the line a reader identifies the hit by.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the context under it — where it is, or what it said.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Gets the stage to navigate to, when the hit belongs to one.</summary>
    public string? StageName { get; init; }

    /// <summary>Gets the step to navigate to, when the hit belongs to one.</summary>
    public int? StepId { get; init; }

    /// <summary>Gets the value to open, when the hit is one.</summary>
    public string? ValueKey { get; init; }

    /// <summary>Gets whether that value is an artifact rather than a variable.</summary>
    public bool IsArtifact { get; init; }
}

/// <summary>What a search found.</summary>
public sealed record SearchResults
{
    /// <summary>Nothing searched for yet.</summary>
    public static SearchResults None { get; } = new();

    /// <summary>Gets the hits, in run order within each part.</summary>
    public ImmutableList<SearchHit> Hits { get; init; } = ImmutableList<SearchHit>.Empty;

    /// <summary>Gets whether the search stopped early because there were too many.</summary>
    public bool Truncated { get; init; }

    /// <summary>Gets what could not be understood about the query, when something could not be.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets how many hits came from one part of the run.</summary>
    public int CountIn(SearchScope scope) => Hits.Count(hit => hit.Scope == scope);
}

/// <summary>
/// Runs a query over the run on screen.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and over the projected graph rather than the journal: the selected run is already in memory whole,
/// which is what makes searching it a function call rather than a file read. That also fixes the scope — this
/// searches the run being looked at, not every run ever recorded, which would be a different feature needing
/// a different answer for how to show it.
/// </para>
/// <para>
/// Both comparisons are taken as arguments so <c>is:slower</c> and <c>is:changed</c> can be answered. They are
/// conditions about two runs, which the graph on its own cannot state.
/// </para>
/// </remarks>
public static class RunSearch
{
    /// <summary>
    /// How many hits are collected before the search gives up.
    /// </summary>
    /// <remarks>
    /// A single letter typed into the box matches most of a large run. The cap is what stops that building a
    /// list of forty thousand rows nobody scrolls, and it is reported rather than hidden so the reader knows
    /// to be more specific instead of concluding their thing is not there.
    /// </remarks>
    public const int MaximumHits = 200;

    /// <summary>
    /// Finds everything in a run that answers a query.
    /// </summary>
    public static SearchResults Find(SearchQuery query, RunGraph graph, ValueDiff values, TimingDiff timing)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(timing);

        if (query.IsEmpty)
            return SearchResults.None with { Problem = query.Problem };

        ImmutableList<SearchHit>.Builder hits = ImmutableList.CreateBuilder<SearchHit>();
        bool truncated = false;

        foreach (SearchHit hit in Search(query, graph, values, timing))
        {
            if (hits.Count >= MaximumHits)
            {
                truncated = true;
                break;
            }

            hits.Add(hit);
        }

        return new SearchResults
        {
            Hits = hits.ToImmutable(),
            Truncated = truncated,
            Problem = query.Problem
        };
    }

    /// <summary>
    /// Every hit, lazily, so the cap actually stops the work rather than trimming the answer afterwards.
    /// </summary>
    private static IEnumerable<SearchHit> Search(SearchQuery query, RunGraph graph, ValueDiff values, TimingDiff timing)
    {
        if (query.Includes(SearchScope.Steps))
        {
            foreach (SearchHit hit in Steps(query, graph, timing))
                yield return hit;
        }

        if (query.Includes(SearchScope.Logs))
        {
            foreach (SearchHit hit in Logs(query, graph))
                yield return hit;
        }

        if (query.Includes(SearchScope.Values))
        {
            foreach (SearchHit hit in Values(query, graph, values))
                yield return hit;
        }

        if (query.Includes(SearchScope.Checks))
        {
            foreach (SearchHit hit in Checks(query, graph))
                yield return hit;
        }
    }

    private static IEnumerable<SearchHit> Steps(SearchQuery query, RunGraph graph, TimingDiff timing)
    {
        // A condition no step can satisfy excludes steps entirely, rather than being ignored. Someone who
        // typed is:changed is asking about values, and returning every step beside them would bury the answer.
        if (query.Conditions.Contains(SearchCondition.Changed))
            yield break;

        foreach (StageNode stage in graph.Stages)
        {
            if (query.Stage is { } stagePattern && !stagePattern.Matches(stage.Name))
                continue;

            foreach (StepNode step in stage.Steps)
            {
                if (query.Step is { } stepPattern && !stepPattern.MatchesAny(step.DisplayName, step.Name))
                    continue;

                if (!SatisfiesStepConditions(query, step, timing.ForStep(stage.Name, step.StepId)))
                    continue;

                // The words are matched against everything about the step a reader might have in mind: what
                // it is called, what it says it is, and the keys it reads and writes.
                if (!query.Terms.All(term => term.MatchesAny(
                        step.DisplayName,
                        step.Name,
                        step.Description,
                        stage.Name,
                        Keys(step))))
                {
                    continue;
                }

                yield return new SearchHit
                {
                    Scope = SearchScope.Steps,
                    Title = step.DisplayName,
                    Detail = DescribeStep(stage, step, timing.ForStep(stage.Name, step.StepId)),
                    StageName = stage.Name,
                    StepId = step.StepId
                };
            }
        }
    }

    private static IEnumerable<SearchHit> Logs(SearchQuery query, RunGraph graph)
    {
        // Log lines have no standing of their own — a line is not slow, retried or changed. The one condition
        // that carries is failed, which is answered by the level.
        if (query.Conditions.Any(condition => condition != SearchCondition.Failed))
            yield break;

        foreach (StageNode stage in graph.Stages)
        {
            if (query.Stage is { } stagePattern && !stagePattern.Matches(stage.Name))
                continue;

            foreach (StepNode step in stage.Steps)
            {
                if (query.Step is { } stepPattern && !stepPattern.MatchesAny(step.DisplayName, step.Name))
                    continue;

                foreach (AttemptNode attempt in step.Attempts)
                {
                    foreach (LogNode entry in attempt.Logs)
                    {
                        if (query.Level is { } level && entry.Level != level)
                            continue;

                        if (query.Conditions.Contains(SearchCondition.Failed) && entry.Level != DebugLogLevel.Error)
                            continue;

                        if (query.Event is { } emitter && !emitter.Matches(entry.EventName))
                            continue;

                        string line = entry.Render();

                        // The rendered line and the template both: someone searching for the sentence they
                        // saw wants the first, and someone looking for every line of one shape — the template
                        // with its holes unfilled — wants the second.
                        if (!query.Terms.All(term => term.MatchesAny(line, entry.Template, entry.EventName)))
                            continue;

                        yield return new SearchHit
                        {
                            Scope = SearchScope.Logs,
                            Title = line,
                            Detail = $"{entry.Level.ToString().ToLowerInvariant()} · {step.DisplayName} · {stage.Name}",
                            StageName = stage.Name,
                            StepId = step.StepId
                        };
                    }
                }
            }
        }
    }

    private static IEnumerable<SearchHit> Values(SearchQuery query, RunGraph graph, ValueDiff values)
    {
        // A value is not slow, not retried and not held. Only "changed" says anything about one.
        if (query.Conditions.Any(condition => condition != SearchCondition.Changed))
            yield break;

        // Values are reported at run scope, not per step, so a query narrowed to a stage or a step is not
        // asking about them.
        if (query.Stage is not null || query.Step is not null || query.Level is not null || query.Event is not null)
            yield break;

        foreach (KeyValuePair<string, ValueNode> pair in graph.Variables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (query.Conditions.Contains(SearchCondition.Changed) && values.ForVariable(pair.Key) is null or ValueChangeKind.Unchanged)
                continue;

            if (!query.Terms.All(term => term.MatchesAny(pair.Key, pair.Value.TypeName, pair.Value.Description.Summary)))
                continue;

            yield return new SearchHit
            {
                Scope = SearchScope.Values,
                Title = pair.Key,
                Detail = $"variable · {pair.Value.Description.Summary}",
                ValueKey = pair.Key
            };
        }

        foreach (KeyValuePair<string, ArtifactNode> pair in graph.Artifacts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (query.Conditions.Contains(SearchCondition.Changed) && values.ForArtifact(pair.Key) is null or ValueChangeKind.Unchanged)
                continue;

            if (!query.Terms.All(term => term.MatchesAny(pair.Key, pair.Value.SchemaKey, pair.Value.Description.Summary)))
                continue;

            yield return new SearchHit
            {
                Scope = SearchScope.Values,
                Title = pair.Key,
                Detail = $"artifact · {pair.Value.Description.Summary}",
                ValueKey = pair.Key,
                IsArtifact = true
            };
        }
    }

    private static IEnumerable<SearchHit> Checks(SearchQuery query, RunGraph graph)
    {
        // Of the conditions, only "failed" means anything about a check.
        if (query.Conditions.Any(condition => condition != SearchCondition.Failed))
            yield break;

        if (query.Level is not null || query.Event is not null)
            yield break;

        foreach (AssertionNode assertion in graph.Assertions)
        {
            if (query.Conditions.Contains(SearchCondition.Failed) && assertion.Succeeded)
                continue;

            if (!query.Terms.All(term => term.MatchesAny(
                    assertion.Target,
                    assertion.AssertionName,
                    assertion.Render(),
                    assertion.Actual.Summary,
                    assertion.Scope)))
            {
                continue;
            }

            yield return new SearchHit
            {
                Scope = SearchScope.Checks,
                Title = $"{assertion.Subject} · {assertion.Render()}",
                Detail = assertion.Succeeded
                    ? $"held · was {assertion.Actual.Summary}"
                    : $"did not hold · was {assertion.Actual.Summary}"
            };
        }
    }

    private static bool SatisfiesStepConditions(SearchQuery query, StepNode step, StepTiming? timing)
    {
        foreach (SearchCondition condition in query.Conditions)
        {
            bool holds = condition switch
            {
                SearchCondition.Failed => step.Lifecycle is DebugLifecycleState.Error or DebugLifecycleState.Timeout,
                SearchCondition.Retried => step.Attempts.Count > 1,
                SearchCondition.Slower => timing?.Change == StepTimingChange.Slower,
                SearchCondition.Faster => timing?.Change == StepTimingChange.Faster,
                SearchCondition.Held => step.IsWaitingAtBreakpoint,

                // Handled by the caller, which excludes steps outright rather than asking each one.
                _ => false
            };

            if (!holds)
                return false;
        }

        return true;
    }

    private static string DescribeStep(StageNode stage, StepNode step, StepTiming? timing)
    {
        List<string> parts =
        [
            stage.Name,
            step.Lifecycle.ToString().ToLowerInvariant()
        ];

        if (step.Duration is { } took)
            parts.Add(Took(took));

        if (timing?.Change == StepTimingChange.Slower)
            parts.Add("slower than last time");
        else if (timing?.Change == StepTimingChange.Faster)
            parts.Add("quicker than last time");

        if (step.Attempts.Count > 1)
            parts.Add($"{step.Attempts.Count} attempts");

        return string.Join(" · ", parts);
    }

    /// <summary>Every key a step declares, as one string to match words against.</summary>
    private static string Keys(StepNode step)
        => string.Join(" ", step.Inputs.Select(input => input.Key).Concat(step.Outputs.Select(output => output.Key)));

    private static string Took(TimeSpan took)
        => took < TimeSpan.FromSeconds(1)
            ? took.TotalMilliseconds.ToString("F0", CultureInfo.CurrentCulture) + " ms"
            : took < TimeSpan.FromMinutes(1)
                ? took.TotalSeconds.ToString("F1", CultureInfo.CurrentCulture) + " s"
                : ((int)took.TotalMinutes).ToString(CultureInfo.CurrentCulture) + "m " + took.Seconds.ToString("00", CultureInfo.CurrentCulture) + "s";
}
