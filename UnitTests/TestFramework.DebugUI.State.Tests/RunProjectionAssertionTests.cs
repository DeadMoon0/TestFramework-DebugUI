using System;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers recording assertion results at run scope.
/// </summary>
public class RunProjectionAssertionTests
{
    [Fact]
    public void APassingAssertionIsRecordedWithWhatItChecked()
    {
        RunGraph graph = RunProjection.ApplyAssertion(new RunGraph(), Assertion("user", succeeded: true, expected: "Ada", actual: "Ada"));

        AssertionNode assertion = Assert.Single(graph.Assertions);
        Assert.True(assertion.Succeeded);
        Assert.Equal("user", assertion.Target);

        // The check and its argument, which is what "expected" was before it became a sentence.
        Assert.Equal("Be", assertion.AssertionName);
        Assert.Equal("Ada", Assert.Single(assertion.Arguments).Text);
        Assert.Equal("Ada", assertion.Actual.Summary);
        Assert.Equal("Be(Ada)", assertion.Render());
    }

    [Fact]
    public void AFailingAssertionKeepsWhatItExpectedAndWhatItFound()
    {
        // Which is what the framework's explanation was assembled from. Keeping the two apart means a reader
        // can compare them; the sentence about them could only be read.
        RunGraph graph = RunProjection.ApplyAssertion(
            new RunGraph(),
            Assertion("user", succeeded: false, expected: "Ada", actual: "Grace"));

        AssertionNode assertion = Assert.Single(graph.Assertions);
        Assert.False(assertion.Succeeded);
        Assert.Equal("Ada", Assert.Single(assertion.Arguments).Text);
        Assert.Equal("Grace", assertion.Actual.Summary);
    }

    [Fact]
    public void AssertionsAreKeptInOrder()
    {
        RunGraph graph = new();
        graph = RunProjection.ApplyAssertion(graph, Assertion("a", succeeded: true, at: 1));
        graph = RunProjection.ApplyAssertion(graph, Assertion("b", succeeded: false, at: 2));
        graph = RunProjection.ApplyAssertion(graph, Assertion("c", succeeded: true, at: 3));

        Assert.Equal(["a", "b", "c"], graph.Assertions.Select(assertion => assertion.Target));
    }

    [Fact]
    public void AssertionsStayOutOfTheLogCollections()
    {
        // Structured rather than folded into the log stream, so a reader gets the run's checks as a
        // summary instead of reconstructing them from prose.
        RunGraph graph = RunProjection.ApplyAssertion(new RunGraph(), Assertion("user", succeeded: true));

        Assert.Single(graph.Assertions);
        Assert.Empty(graph.Stages);
    }

    [Fact]
    public void TheSameCheckMadeRepeatedlyIsKept()
    {
        // A poll that re-checks a value, or an assertion inside a loop, legitimately produces the
        // same shape more than once. Only an exact redelivery is suppressed.
        RunGraph graph = new();
        graph = RunProjection.ApplyAssertion(graph, Assertion("user", succeeded: false, at: 1));
        graph = RunProjection.ApplyAssertion(graph, Assertion("user", succeeded: false, at: 2));
        graph = RunProjection.ApplyAssertion(graph, Assertion("user", succeeded: true, at: 3));

        Assert.Equal(3, graph.Assertions.Count);
    }

    [Fact]
    public void ReplayingTheSameAssertionDoesNotDuplicateIt()
    {
        PipeAssertionSignal signal = Assertion("user", succeeded: true, at: 1);

        RunGraph graph = RunProjection.ApplyAssertion(new RunGraph(), signal);
        graph = RunProjection.ApplyAssertion(graph, signal);

        Assert.Single(graph.Assertions);
    }

    [Fact]
    public void TheEnclosingScopeIsCarriedThrough()
    {
        RunGraph graph = RunProjection.ApplyAssertion(new RunGraph(), Assertion("user", succeeded: true, scope: "order checks"));

        Assert.Equal("order checks", Assert.Single(graph.Assertions).Scope);
    }

    private static PipeAssertionSignal Assertion(
        string target,
        bool succeeded,
        string expected = "",
        string actual = "",
        string reason = "",
        string scope = "",
        int at = 0) => new()
        {
            SessionId = "session-1",
            Entry = new DebugAssertionEntry
            {
                OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at),
                TargetKind = DebugAssertionTargetKind.Variable,
                Target = target,
                AssertionName = "Be",
                Arguments = [DebugLogField.Of("expected", expected)],
                Succeeded = succeeded,
                Actual = new DebugValueDescription { Summary = actual, Shape = DebugValueShape.Text },
                AssertionScope = scope
            }
        };
}
