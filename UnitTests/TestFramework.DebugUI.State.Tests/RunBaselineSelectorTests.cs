using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers choosing which earlier run a run is compared against.
/// </summary>
/// <remarks>
/// The baseline is "the last time this test passed". Getting it wrong is worse than having no baseline:
/// a diff against the wrong run is a confident answer to a question nobody asked, and the reader has no
/// way to tell from the badges that the comparison was meaningless.
/// </remarks>
public class RunBaselineSelectorTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-08-17T12:00:00Z");

    [Fact]
    public void OnlyEarlierRunsOfTheSameTestAreCandidates()
    {
        RunSummary current = Run("current", "Suite.TheTest", Noon);

        ImmutableList<RunSummary> candidates = RunBaselineSelector.CandidatesFor(
            [
                Run("same-earlier", "Suite.TheTest", Noon.AddMinutes(-5)),
                Run("other-test", "Suite.DifferentTest", Noon.AddMinutes(-1)),
                Run("same-later", "Suite.TheTest", Noon.AddMinutes(5)),
                current
            ],
            current);

        Assert.Equal(["same-earlier"], candidates.Select(run => run.SessionId));
    }

    [Fact]
    public void TheNewestEarlierRunIsTriedFirst()
    {
        RunSummary current = Run("current", "Suite.TheTest", Noon);

        ImmutableList<RunSummary> candidates = RunBaselineSelector.CandidatesFor(
            [
                Run("oldest", "Suite.TheTest", Noon.AddHours(-3)),
                Run("newest", "Suite.TheTest", Noon.AddMinutes(-1)),
                Run("middle", "Suite.TheTest", Noon.AddHours(-1)),
                current
            ],
            current);

        Assert.Equal(["newest", "middle", "oldest"], candidates.Select(run => run.SessionId));
    }

    [Fact]
    public void ARunStillProducingEventsIsNotACandidate()
    {
        // Its values have not settled, so anything compared against them is compared against a
        // half-finished run.
        RunSummary current = Run("current", "Suite.TheTest", Noon);

        ImmutableList<RunSummary> candidates = RunBaselineSelector.CandidatesFor(
            [Run("live", "Suite.TheTest", Noon.AddMinutes(-1)) with { IsLive = true }, current],
            current);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ARunWithNoTestIdentityHasNoBaselineRatherThanAnArbitraryOne()
    {
        // Without a fully qualified name there is no way to know which runs are the same test, and
        // guessing would diff unrelated values.
        RunSummary current = Run("current", fullyQualifiedName: null, Noon);

        Assert.Empty(RunBaselineSelector.CandidatesFor([Run("other", null, Noon.AddMinutes(-1)), current], current));
    }

    [Fact]
    public void TheNumberOfRunsReplayedIsBounded()
    {
        RunSummary current = Run("current", "Suite.TheTest", Noon);

        List<RunSummary> history = [current];
        for (int index = 1; index <= 30; index++)
            history.Add(Run($"run-{index}", "Suite.TheTest", Noon.AddMinutes(-index)));

        Assert.Equal(RunBaselineSelector.MaximumCandidates, RunBaselineSelector.CandidatesFor(history, current).Count);
    }

    [Fact]
    public void CandidateOrderIsStableWhenTwoRunsShareAnInstant()
    {
        // A baseline that changes between refreshes would make the badges flicker between two
        // different answers, both of them defensible.
        RunSummary current = Run("current", "Suite.TheTest", Noon);
        RunSummary first = Run("aaa", "Suite.TheTest", Noon.AddMinutes(-1));
        RunSummary second = Run("bbb", "Suite.TheTest", Noon.AddMinutes(-1));

        Assert.Equal(
            RunBaselineSelector.CandidatesFor([first, second, current], current).Select(run => run.SessionId),
            RunBaselineSelector.CandidatesFor([second, first, current], current).Select(run => run.SessionId));
    }

    [Fact]
    public void ARunThatPassedIsUsableAsABaseline()
    {
        Assert.True(RunBaselineSelector.IsUsableBaseline(Finished(assertionHeld: true)));
    }

    [Fact]
    public void ARunWhoseAssertionFailedIsNotABaseline()
    {
        // The whole point of the feature: compare against the last run that WORKED.
        Assert.False(RunBaselineSelector.IsUsableBaseline(Finished(assertionHeld: false)));
    }

    [Fact]
    public void ARunThatFinishedCleanlyIsABaselineEvenIfTheTimelineAssertedNothing()
    {
        // This started out the other way round, requiring an assertion, and real runs disproved it:
        // a test whose checks are Assert.Equal calls in the test method asserts nothing the framework
        // can see, and every value sample in this repository is written that way. Demanding a timeline
        // assertion treats missing information as evidence of failure and denies those tests a
        // baseline entirely. Finished with nothing failed is the strongest claim available here.
        Assert.True(RunBaselineSelector.IsUsableBaseline(Finished(assertionHeld: null)));
    }

    [Fact]
    public void ARunThatNeverReachedItsFinishIsNotABaseline()
    {
        // A killed test host. Its values are whatever it got to before dying.
        RunGraph aborted = RunProjection.ApplyAssertion(Structure(), Assertion(true));

        Assert.False(RunBaselineSelector.IsUsableBaseline(aborted));
    }

    [Fact]
    public void ReplayingAJournalReachesTheSameGraphTheReducerWouldBuild()
    {
        // The baseline is folded outside the store, so it has to agree with the store's own path or a
        // value could be compared against something the board never showed.
        ImmutableList<DebugEnvelope> envelopes =
        [
            Envelope(Init()),
            Envelope(ValueUpdate("greeting", "hello")),
            Envelope(new PipeTimelineRunFinishedSignal { SessionId = "baseline" })
        ];

        RunGraph replayed = RunBaselineSelector.Replay(envelopes);

        Assert.True(replayed.IsFinished);
        Assert.Equal("hello", replayed.Variables["greeting"].Description.Preview?.Text);
    }

    [Fact]
    public void AFrameThisBuildCannotReadIsSkippedRatherThanLosingTheWholeBaseline()
    {
        // An older UI reading a newer run's journal. Most of a baseline beats none, and the
        // alternative is that one unknown signal silently disables the feature.
        ImmutableList<DebugEnvelope> envelopes =
        [
            Envelope(Init()),
            new DebugEnvelope
            {
                V = 1,
                SessionId = "baseline",
                Seq = 2,
                AtUtc = Noon,
                Kind = (PipeSignalKind)9999,
                Payload = new Newtonsoft.Json.Linq.JObject()
            },
            Envelope(ValueUpdate("greeting", "hello"))
        ];

        RunGraph replayed = RunBaselineSelector.Replay(envelopes);

        Assert.Equal("hello", replayed.Variables["greeting"].Description.Preview?.Text);
    }

    [Fact]
    public void ComparingReportsWhichValuesMovedAndAgainstWhichRun()
    {
        RunGraph baselineGraph = RunProjection.ApplyValueUpdate(Structure(), ValueUpdate("greeting", "hello"));
        RunBaseline baseline = RunBaselineSelector.BaselineFrom(Run("baseline", "Suite.TheTest", Noon.AddMinutes(-1)), baselineGraph);

        RunGraph currentGraph = RunProjection.ApplyValueUpdate(Structure(), ValueUpdate("greeting", "goodbye"));

        ValueDiff diff = RunBaselineSelector.Compare(currentGraph, baseline);

        Assert.True(diff.HasBaseline);
        Assert.Equal("baseline", diff.Baseline!.SessionId);
        Assert.Equal(ValueChangeKind.Changed, diff.ForVariable("greeting"));
        Assert.Equal(1, diff.ChangedCount);
    }

    [Fact]
    public void HavingNoBaselineSaysWhyRatherThanShowingNothing()
    {
        ValueDiff diff = RunBaselineSelector.Unavailable("This test has not passed before.");

        Assert.False(diff.HasBaseline);
        Assert.Equal("This test has not passed before.", diff.Unavailable);
        Assert.Equal(0, diff.ChangedCount);
    }

    private static RunSummary Run(string sessionId, string? fullyQualifiedName, DateTimeOffset startedAt) => new()
    {
        SessionId = sessionId,
        Name = "timeline",
        StartedAtUtc = startedAt,
        FullyQualifiedName = fullyQualifiedName,
        IsFinished = true
    };

    /// <summary>A finished run with one step complete and optionally one assertion.</summary>
    private static RunGraph Finished(bool? assertionHeld)
    {
        RunGraph graph = RunProjection.ApplyTransition(Structure(), new PipeEntityTransitionSignal
        {
            SessionId = "baseline",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Complete,
            PreviousState = DebugLifecycleState.Running,
            OccurredAtUtc = Noon
        });

        if (assertionHeld is { } held)
            graph = RunProjection.ApplyAssertion(graph, Assertion(held));

        return RunProjection.ApplyRunFinished(graph, new PipeTimelineRunFinishedSignal { SessionId = "baseline" });
    }

    private static PipeAssertionSignal Assertion(bool succeeded) => new()
    {
        SessionId = "baseline",
        Entry = new DebugAssertionEntry
        {
            OccurredAtUtc = Noon,
            TargetKind = DebugAssertionTargetKind.Variable,
            Target = "greeting",
            AssertionName = "Be",
            Arguments = [DebugLogField.Of("expected", "hello")],
            Succeeded = succeeded,
            Actual = new DebugValueDescription { Summary = succeeded ? "\"hello\"" : "\"goodbye\"", Shape = DebugValueShape.Text }
        }
    };

    private static DebugEnvelope Envelope(IPipeSignal signal)
        => DebugEnvelopeCodec.Wrap(signal, 1);

    private static PipeValueUpdateSignal ValueUpdate(string key, string text) => new()
    {
        SessionId = "baseline",
        Name = key,
        ValueKind = DebugValueKind.Variable,
        Stage = "Main",
        StepId = 0,
        ObservedAtUtc = Noon,
        Envelope = new DebugValueEnvelope
        {
            Kind = DebugValueKind.Variable,
            TypeName = "System.String",
            SchemaKey = "tf.value.text",
            Description = new DebugValueDescription
            {
                Summary = text,
                Shape = DebugValueShape.Text,
                Preview = new DebugValuePreview { Form = DebugPreviewForm.Text, Text = text }
            }
        }
    };

    private static PipeInitTimelineRunSignal Init() => new()
    {
        SessionId = "baseline",
        Name = "timeline",
        ProjectPath = "project",
        Identity = new TestIdentity
        {
            FullyQualifiedName = "Suite.TheTest",
            DisplayName = "TheTest",
            Framework = TestFrameworkKind.XUnit,
            AssemblyPath = "Suite.dll"
        },
        RunStructure = StructureSnapshot()
    };

    private static RunGraph Structure() => RunProjection.ApplyInit(Init());

    private static TimelineRunStructure StructureSnapshot() => new()
    {
        Variables = new Dictionary<TestFramework.Core.Variables.VariableIdentifier, DebugValue>(),
        Artifacts = new Dictionary<TestFramework.Core.Artifacts.ArtifactIdentifier, DebugValue>(),
        Stages =
        [
            new DebugStageState
            {
                Name = "Main",
                Description = string.Empty,
                Steps =
                [
                    new DebugStepState
                    {
                        Name = "Work",
                        Description = string.Empty,
                        Phase = StepExecutionPhase.Act,
                        Parallelization = StepParallelizationMode.Parallelizable,
                        DoesReturn = false
                    }
                ]
            }
        ]
    };
}
