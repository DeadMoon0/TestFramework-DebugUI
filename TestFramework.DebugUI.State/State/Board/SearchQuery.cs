using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State.Board;

/// <summary>What part of a run a result came from.</summary>
public enum SearchScope
{
    /// <summary>A step of the run.</summary>
    Steps,

    /// <summary>A line a step logged.</summary>
    Logs,

    /// <summary>A variable or artifact.</summary>
    Values,

    /// <summary>An assertion the run made.</summary>
    Checks
}

/// <summary>
/// A condition about a thing's standing rather than about its text.
/// </summary>
/// <remarks>
/// These are the point of searching a run rather than grepping a log file: the tool knows which steps failed,
/// which got slower than last time and which values differ from the last good run, and none of that is
/// findable by typing words.
/// </remarks>
public enum SearchCondition
{
    /// <summary>A step that errored or timed out, or a check that did not hold.</summary>
    Failed,

    /// <summary>A step that needed more than one attempt.</summary>
    Retried,

    /// <summary>A step materially slower than in the last run of this test that passed.</summary>
    Slower,

    /// <summary>A step materially quicker than in the last run of this test that passed.</summary>
    Faster,

    /// <summary>A value that differs from the last run of this test that passed.</summary>
    Changed,

    /// <summary>A step currently held at a breakpoint.</summary>
    Held
}

/// <summary>
/// A written search, read into the conditions it states.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately one query language rather than a filter box per panel. The reader has one question — "where
/// is the thing I am thinking of" — and the answer can be a step, a log line, a value or a check; making them
/// type it four times in four places would be the same mistake the copy buttons were.
/// </para>
/// <para>
/// A plain class, not a record: it holds <see cref="SearchPattern"/>s, which cannot usefully be compared.
/// </para>
/// </remarks>
public sealed class SearchQuery
{
    private SearchQuery()
    {
    }

    /// <summary>A search that has not been written yet.</summary>
    public static SearchQuery Empty { get; } = new();

    /// <summary>Gets the words to look for, all of which have to match something about a candidate.</summary>
    public ImmutableList<SearchPattern> Terms { get; private init; } = [];

    /// <summary>
    /// Gets which parts of the run to look in, or empty for all of them.
    /// </summary>
    /// <remarks>
    /// Written <c>in:logs</c>. Empty means everywhere, which is what someone typing a bare word wants — they
    /// are looking for a thing, not for a thing of a particular kind.
    /// </remarks>
    public ImmutableHashSet<SearchScope> Scopes { get; private init; } = [];

    /// <summary>Gets the conditions every result has to satisfy, written <c>is:failed</c>.</summary>
    public ImmutableHashSet<SearchCondition> Conditions { get; private init; } = [];

    /// <summary>Gets the stage results must be in, written <c>stage:Cleanup</c>.</summary>
    public SearchPattern? Stage { get; private init; }

    /// <summary>Gets the step results must belong to, written <c>step:Fetch</c>.</summary>
    public SearchPattern? Step { get; private init; }

    /// <summary>Gets the log event that emitted a line, written <c>event:StepResult</c>.</summary>
    public SearchPattern? Event { get; private init; }

    /// <summary>Gets the level a log line must be at, written <c>level:error</c>.</summary>
    public DebugLogLevel? Level { get; private init; }

    /// <summary>
    /// Gets what could not be understood, when something could not be.
    /// </summary>
    /// <remarks>
    /// Reported rather than thrown, and reported alongside whatever <em>was</em> understood, so a half-typed
    /// regular expression narrows the search instead of emptying it and blaming the reader.
    /// </remarks>
    public string? Problem { get; private init; }

    /// <summary>Whether there is anything here to search for.</summary>
    public bool IsEmpty
        => Terms.Count == 0
           && Scopes.Count == 0
           && Conditions.Count == 0
           && Stage is null
           && Step is null
           && Event is null
           && Level is null;

    /// <summary>Whether this query looks in a given part of the run.</summary>
    public bool Includes(SearchScope scope) => Scopes.Count == 0 || Scopes.Contains(scope);

    /// <summary>
    /// Reads what was typed.
    /// </summary>
    /// <remarks>
    /// Unknown prefixes are not treated as filters. <c>http://localhost:5000</c> contains a colon and is
    /// something a person would genuinely search a run for, so only the prefixes this understands are taken as
    /// filters and everything else stays a word to look for.
    /// </remarks>
    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        ImmutableList<SearchPattern>.Builder terms = ImmutableList.CreateBuilder<SearchPattern>();
        HashSet<SearchScope> scopes = [];
        HashSet<SearchCondition> conditions = [];

        SearchPattern? stage = null;
        SearchPattern? step = null;
        SearchPattern? emitter = null;
        DebugLogLevel? level = null;
        string? problem = null;

        foreach (string token in Tokenize(text))
        {
            int colon = token.IndexOf(':', StringComparison.Ordinal);
            string prefix = colon > 0 ? token[..colon].ToLowerInvariant() : string.Empty;
            string rest = colon > 0 ? token[(colon + 1)..] : string.Empty;

            switch (prefix)
            {
                // A prefix this understands with a value it does not is a typo, and it is said so. Falling
                // through to treat "in:stpes" as a word to search for would return nothing and look like the
                // run simply did not contain it.
                case "in":
                    if (TryReadScope(rest, out SearchScope scope))
                        scopes.Add(scope);
                    else
                        problem ??= $"\"{rest}\" is not something to search in. Try steps, logs, values or checks.";

                    continue;

                case "is":
                    if (TryReadCondition(rest, out SearchCondition condition))
                        conditions.Add(condition);
                    else
                        problem ??= $"\"{rest}\" is not a condition. Try failed, retried, slower, faster, changed or held.";

                    continue;

                case "level":
                    if (TryReadLevel(rest, out DebugLogLevel read))
                    {
                        level = read;

                        // Asking for a level is asking about log lines. Leaving the other kinds in would
                        // return every step in the run beside the three lines that were actually wanted.
                        scopes.Add(SearchScope.Logs);
                    }
                    else
                    {
                        problem ??= $"\"{rest}\" is not a log level. A run reports information, warning and error.";
                    }

                    continue;

                case "stage" when Read(rest, ref problem) is { } pattern:
                    stage = pattern;
                    continue;

                case "step" when Read(rest, ref problem) is { } pattern:
                    step = pattern;
                    continue;

                case "event" when Read(rest, ref problem) is { } pattern:
                    emitter = pattern;
                    scopes.Add(SearchScope.Logs);
                    continue;

                default:
                    if (Read(token, ref problem) is { } word)
                        terms.Add(word);

                    continue;
            }
        }

        return new SearchQuery
        {
            Terms = terms.ToImmutable(),
            Scopes = [.. scopes],
            Conditions = [.. conditions],
            Stage = stage,
            Step = step,
            Event = emitter,
            Level = level,
            Problem = problem
        };
    }

    /// <summary>Every filter prefix, for the help the bar shows when nothing has been typed.</summary>
    public static ImmutableList<string> Hints { get; } =
    [
        "in:steps · in:logs · in:values · in:checks",
        "is:failed · is:retried · is:slower · is:faster · is:changed · is:held",
        "stage:Cleanup · step:Fetch · level:error · event:StepResult",
        "Wait*Blob for a wildcard, /regular expression/ between slashes"
    ];

    private static SearchPattern? Read(string text, ref string? problem)
    {
        if (SearchPattern.TryParse(text, out SearchPattern? pattern, out string? reason))
            return pattern;

        problem ??= reason;
        return null;
    }

    private static bool TryReadScope(string text, out SearchScope scope)
    {
        switch (text.ToLowerInvariant())
        {
            case "step" or "steps":
                scope = SearchScope.Steps;
                return true;

            case "log" or "logs":
                scope = SearchScope.Logs;
                return true;

            case "value" or "values" or "variable" or "variables" or "artifact" or "artifacts":
                scope = SearchScope.Values;
                return true;

            case "check" or "checks" or "assertion" or "assertions":
                scope = SearchScope.Checks;
                return true;

            default:
                scope = default;
                return false;
        }
    }

    private static bool TryReadCondition(string text, out SearchCondition condition)
    {
        switch (text.ToLowerInvariant())
        {
            case "failed" or "failing" or "broken":
                condition = SearchCondition.Failed;
                return true;

            case "retried":
                condition = SearchCondition.Retried;
                return true;

            case "slow" or "slower":
                condition = SearchCondition.Slower;
                return true;

            case "fast" or "faster":
                condition = SearchCondition.Faster;
                return true;

            case "changed":
                condition = SearchCondition.Changed;
                return true;

            case "held" or "paused":
                condition = SearchCondition.Held;
                return true;

            default:
                condition = default;
                return false;
        }
    }

    private static bool TryReadLevel(string text, out DebugLogLevel level)
    {
        switch (text.ToLowerInvariant())
        {
            case "info" or "information":
                level = DebugLogLevel.Information;
                return true;

            case "warn" or "warning":
                level = DebugLogLevel.Warning;
                return true;

            case "error":
                level = DebugLogLevel.Error;
                return true;

            default:
                level = default;
                return false;
        }
    }

    /// <summary>
    /// Splits what was typed into terms, keeping quoted runs together.
    /// </summary>
    /// <remarks>
    /// Stage names and log messages contain spaces, so <c>stage:"Set up the database"</c> has to survive as
    /// one term. Quotes are dropped from the result: they are punctuation for this parser, not text anybody
    /// meant to search for.
    /// </remarks>
    private static IEnumerable<string> Tokenize(string text)
    {
        StringBuilder token = new();
        bool quoted = false;

        foreach (char character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
        }

        if (token.Length > 0)
            yield return token.ToString();
    }
}
