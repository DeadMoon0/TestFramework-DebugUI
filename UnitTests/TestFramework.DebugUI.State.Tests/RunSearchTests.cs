using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers searching the run on screen.
/// </summary>
/// <remarks>
/// The query language is the feature, so these are mostly about what a written query means: which parts of the
/// run it looks in, what it excludes, and what happens to something that cannot be understood. A search that
/// silently ignores a term the reader typed is worse than one that says it did not understand.
/// </remarks>
public class RunSearchTests
{
    [Fact]
    public void APlainWordIsASubstringAndIgnoresCase()
    {
        SearchResults results = Find("blob");

        Assert.Contains(results.Hits, hit => hit.Title == "WaitForBlob");
    }

    [Fact]
    public void AWildcardStandsForAnyRunOfCharacters()
    {
        Assert.Contains(Find("Wait*Blob").Hits, hit => hit.Title == "WaitForBlob");
        Assert.Empty(Find("Wait*Order").Hits.Where(hit => hit.Scope == SearchScope.Steps));
    }

    [Fact]
    public void AQuestionMarkStandsForExactlyOneCharacter()
    {
        Assert.Contains(Find("Wait?orBlob").Hits, hit => hit.Title == "WaitForBlob");
        Assert.DoesNotContain(Find("Wait?Blob").Hits, hit => hit.Title == "WaitForBlob");
    }

    [Fact]
    public void AWildcardDoesNotHaveToDescribeTheWholeName()
    {
        // Unanchored, so nobody has to write a trailing star. A wildcard is how a substring search gets
        // narrowed, not a description of a whole string.
        Assert.Contains(Find("Wait*Bl").Hits, hit => hit.Title == "WaitForBlob");
    }

    [Fact]
    public void ADotInATermIsNotAWildcard()
    {
        // Everything that is not * or ? is escaped. Step names and value keys are full of dots, and leaving
        // them live would match text they have nothing to do with.
        Assert.True(SearchPattern.TryParse("a?c", out SearchPattern? pattern, out _));
        Assert.False(pattern!.Matches("abbc"));
        Assert.True(pattern.Matches("abc"));

        Assert.True(SearchPattern.TryParse("A.C", out SearchPattern? dotted, out _));
        Assert.False(dotted!.Matches("ABC"));
        Assert.True(dotted.Matches("A.C"));
    }

    [Fact]
    public void ARegularExpressionGoesBetweenSlashes()
    {
        SearchResults results = Find("/Wait(ing)?ForBlob/");

        Assert.Contains(results.Hits, hit => hit.Title == "WaitForBlob");
    }

    [Fact]
    public void AMalformedRegularExpressionIsExplainedRatherThanThrown()
    {
        // Every half-typed expression is malformed, so this is the ordinary case rather than the exceptional
        // one. Reported, and the query keeps whatever else it understood.
        SearchResults results = Find("/Wait(/");

        Assert.NotNull(results.Problem);
        Assert.Contains("not a valid regular expression", results.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWordHasToMatch()
    {
        // Words are combined with and, which is what narrowing means. Anything else and adding a word to a
        // search would widen it.
        Assert.NotEmpty(Find("wait blob").Hits);
        Assert.Empty(Find("wait order").Hits.Where(hit => hit.Scope == SearchScope.Steps));
    }

    [Fact]
    public void AQuotedTermSurvivesItsSpaces()
    {
        Assert.NotEmpty(Find("\"the blob\"").Hits);
    }

    [Fact]
    public void SearchingCanBeNarrowedToOnePartOfTheRun()
    {
        SearchResults results = Find("in:steps blob");

        Assert.All(results.Hits, hit => Assert.Equal(SearchScope.Steps, hit.Scope));
    }

    [Fact]
    public void LogLinesAreSearchedByWhatTheySaid()
    {
        SearchResults results = Find("in:logs timed");

        SearchHit hit = Assert.Single(results.Hits);

        Assert.Equal(SearchScope.Logs, hit.Scope);
        Assert.Contains("timed out", hit.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ALevelNarrowsToLogLinesOnItsOwn()
    {
        // Asking for a level is asking about log lines. Leaving the other kinds in would answer with every
        // step in the run beside the one line that was wanted.
        SearchResults results = Find("level:error");

        Assert.NotEmpty(results.Hits);
        Assert.All(results.Hits, hit => Assert.Equal(SearchScope.Logs, hit.Scope));
    }

    [Fact]
    public void LinesCanBeFoundByTheEventThatEmittedThem()
    {
        // The event name has been on the wire all along and was never shown or searchable. It is the grouping
        // key for a log: every line one kind of event produced, without knowing what any of them say.
        SearchResults results = Find("event:StepResult");

        Assert.NotEmpty(results.Hits);
        Assert.All(results.Hits, hit => Assert.Equal(SearchScope.Logs, hit.Scope));
    }

    [Fact]
    public void AStageNarrowsToWhatIsInIt()
    {
        Assert.Empty(Find("stage:Cleanup blob").Hits);
        Assert.NotEmpty(Find("stage:Main blob").Hits);
    }

    [Fact]
    public void FailedFindsTheStepsThatBroke()
    {
        SearchResults results = Find("is:failed");

        Assert.Contains(results.Hits, hit => hit.Scope == SearchScope.Steps && hit.Title == "WaitForBlob");
    }

    [Fact]
    public void RetriedFindsTheStepsThatNeededMoreThanOneAttempt()
    {
        SearchResults results = Find("is:retried");

        Assert.Equal("WaitForBlob", Assert.Single(results.Hits, hit => hit.Scope == SearchScope.Steps).Title);
    }

    [Fact]
    public void SlowerFindsWhatTheTimingComparisonFlagged()
    {
        // The whole reason to search a run rather than grep a log: this is knowledge the tool has and no
        // amount of typing words would find.
        SearchResults results = Find("is:slower");

        Assert.Equal("Fetch", Assert.Single(results.Hits).Title);
    }

    [Fact]
    public void ChangedFindsWhatTheValueComparisonFlagged()
    {
        SearchResults results = Find("is:changed");

        SearchHit hit = Assert.Single(results.Hits);

        Assert.Equal(SearchScope.Values, hit.Scope);
        Assert.Equal("orderId", hit.ValueKey);
    }

    [Fact]
    public void AConditionAKindCannotSatisfyExcludesThatKind()
    {
        // Someone who typed is:changed is asking about values. Returning every step in the run beside them
        // would bury the answer in the thing they did not ask about.
        Assert.DoesNotContain(Find("is:changed").Hits, hit => hit.Scope == SearchScope.Steps);
        Assert.DoesNotContain(Find("is:slower").Hits, hit => hit.Scope == SearchScope.Values);
        Assert.DoesNotContain(Find("is:retried").Hits, hit => hit.Scope == SearchScope.Logs);
    }

    [Fact]
    public void ChecksAreFoundByWhatTheyChecked()
    {
        SearchResults results = Find("in:checks orderId");

        Assert.Equal(SearchScope.Checks, Assert.Single(results.Hits).Scope);
    }

    [Fact]
    public void AHitCarriesTheAddressOfWhatItFound()
    {
        // Not the node. The graph is replaced as events arrive, so a held node would be a stale copy and
        // clicking the result would open something that no longer looks like that.
        SearchHit hit = Assert.Single(Find("in:steps WaitForBlob").Hits);

        Assert.Equal("Main", hit.StageName);
        Assert.Equal(1, hit.StepId);
    }

    [Fact]
    public void AMisspeltFilterIsSaidRatherThanSearchedFor()
    {
        // Treating "in:stpes" as a word would answer with nothing and read as the run not containing it.
        Assert.Contains("is not something to search in", Find("in:stpes").Problem!, StringComparison.Ordinal);
        Assert.Contains("is not a condition", Find("is:slwoer").Problem!, StringComparison.Ordinal);
        Assert.Contains("is not a log level", Find("level:trace").Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPrefixStaysAWordToSearchFor()
    {
        // A colon is ordinary in the things a run reports. A URL is exactly what somebody searches a run for,
        // and reading it as a filter would make it unsearchable.
        SearchQuery query = SearchQuery.Parse("http://localhost:5000");

        Assert.Null(query.Problem);
        Assert.Single(query.Terms);
        Assert.Equal("http://localhost:5000", query.Terms[0].Text);
    }

    [Fact]
    public void AnEmptyQueryFindsNothingRatherThanEverything()
    {
        Assert.True(SearchQuery.Parse("   ").IsEmpty);
        Assert.Empty(Find("   ").Hits);
    }

    [Fact]
    public void TooManyHitsAreCappedAndSaidToBe()
    {
        // A single letter matches most of a large run. Reported rather than hidden, so the reader knows to
        // narrow it instead of concluding their thing is not there.
        RunGraph graph = new()
        {
            Stages =
            [
                new StageNode
                {
                    Name = "Main",
                    Steps =
                    [
                        .. Enumerable.Range(0, RunSearch.MaximumHits + 40).Select(index => new StepNode
                        {
                            StepId = index,
                            Name = "step" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        })
                    ]
                }
            ]
        };

        SearchResults results = RunSearch.Find(SearchQuery.Parse("step"), graph, ValueDiff.None, TimingDiff.None);

        Assert.True(results.Truncated);
        Assert.Equal(RunSearch.MaximumHits, results.Hits.Count);
    }

    [Fact]
    public void EveryFilterIsListedInTheHelpTheBarShows()
    {
        // Read off the parser's own definition, so a filter that exists and is not mentioned anywhere cannot
        // happen. This is the test that fails when one is added and the hints are not.
        string hints = string.Join(" ", SearchQuery.Hints);

        foreach (SearchScope scope in Enum.GetValues<SearchScope>())
            Assert.Contains("in:" + scope.ToString().ToLowerInvariant(), hints, StringComparison.OrdinalIgnoreCase);

        foreach (SearchCondition condition in Enum.GetValues<SearchCondition>())
            Assert.Contains("is:" + condition.ToString().ToLowerInvariant(), hints, StringComparison.OrdinalIgnoreCase);
    }

    private static SearchResults Find(string query)
        => RunSearch.Find(SearchQuery.Parse(query), Graph(), Values(), Timing());

    /// <summary>
    /// A run with one of everything worth finding: a step that failed after retrying, a step that got slower,
    /// a changed value, a check that did not hold and a log line at each level.
    /// </summary>
    private static RunGraph Graph() => new()
    {
        Stages =
        [
            new StageNode
            {
                Name = "Main",
                Steps =
                [
                    new StepNode
                    {
                        StepId = 0,
                        Name = "Fetch",
                        Description = "Reads the order",
                        Lifecycle = DebugLifecycleState.Complete,
                        StartedAtUtc = DateTimeOffset.UnixEpoch,
                        FinishedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(9),
                        Outputs = [new StepIO { Key = "orderId" }],
                        Attempts =
                        [
                            new AttemptNode
                            {
                                Attempt = 1,
                                Logs =
                                [
                                    Line(DebugLogLevel.Information, "Fetched the order", "OrderFetchedLogEvent"),
                                    Line(DebugLogLevel.Warning, "Retrying", "StepResultLogEvent")
                                ]
                            }
                        ]
                    },
                    new StepNode
                    {
                        StepId = 1,
                        Name = "WaitForBlob",
                        Description = "Waits for the blob to appear",
                        Lifecycle = DebugLifecycleState.Timeout,
                        Attempts =
                        [
                            new AttemptNode { Attempt = 1 },
                            new AttemptNode
                            {
                                Attempt = 2,
                                Logs = [Line(DebugLogLevel.Error, "The step timed out", "StepResultLogEvent")]
                            }
                        ]
                    }
                ]
            },
            new StageNode { Name = "Cleanup", Steps = [new StepNode { StepId = 0, Name = "Deconstruct" }] }
        ],
        Variables = ImmutableDictionary<string, ValueNode>.Empty
            .Add("orderId", new ValueNode { Key = "orderId", TypeName = "Int32" }),
        Assertions =
        [
            new AssertionNode
            {
                AssertionName = "Be",
                Target = "orderId",
                TargetKind = DebugAssertionTargetKind.Variable,
                Succeeded = false
            }
        ]
    };

    private static LogNode Line(DebugLogLevel level, string template, string eventName) => new()
    {
        Level = level,
        Template = template,
        EventName = eventName
    };

    private static ValueDiff Values() => new()
    {
        Baseline = new RunBaseline { SessionId = "earlier", StartedAtUtc = DateTimeOffset.UnixEpoch },
        Variables =
        [
            new ValueChange { Key = "orderId", Kind = DebugValueKindTag.Variable, Change = ValueChangeKind.Changed }
        ]
    };

    private static TimingDiff Timing() => new()
    {
        Baseline = new RunBaseline { SessionId = "earlier", StartedAtUtc = DateTimeOffset.UnixEpoch },
        Steps =
        [
            new StepTiming
            {
                StageName = "Main",
                StepId = 0,
                DisplayName = "Fetch",
                Now = TimeSpan.FromSeconds(9),
                Then = TimeSpan.FromSeconds(1),
                Change = StepTimingChange.Slower
            }
        ]
    };
}
