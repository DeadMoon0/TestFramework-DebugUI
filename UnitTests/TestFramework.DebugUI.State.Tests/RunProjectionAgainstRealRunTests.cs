using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Groups every test class that executes a timeline.
/// </summary>
/// <remarks>
/// Membership is decided by whether a class <em>runs</em> a timeline, not by whether it reads a
/// journal. Core resolves the journal root the first time any run asks, and caches the answer for
/// the life of the process — so the first timeline to execute in this assembly fixes the directory
/// for all of them. A class that runs one outside this collection can therefore resolve the root
/// before the fixture exists, and every journal-reading test afterwards finds an empty folder and
/// fails somewhere unrelated to its own subject.
/// </remarks>
[CollectionDefinition(TimelineRunCollection.Name)]
public sealed class TimelineRunCollection : ICollectionFixture<JournalFixture>
{
    /// <summary>The collection name. Any class that runs a timeline must join it.</summary>
    public const string Name = "Timeline runs";
}

public sealed class JournalFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tf-projection-tests", Guid.NewGuid().ToString("N"));

    public JournalFixture()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("TESTFRAMEWORK_DEBUG_JOURNAL_DIR", Root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TESTFRAMEWORK_DEBUG_JOURNAL_DIR", null);

        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}

/// <summary>
/// Drives the projection with signals a real run produced, rather than ones the test constructed.
/// </summary>
/// <remarks>
/// <para>
/// Every other projection test builds its own signals, which verifies the rules but not the mapping:
/// a wrong field name, or a default that never gets overwritten, passes happily when the test and
/// the code agree with each other and disagree with Core. This runs an actual timeline, reads the
/// journal Core wrote, and replays it — so the field-by-field contract is exercised end to end.
/// </para>
/// <para>
/// The journal is the natural fixture for this. It carries exactly the envelopes the pipe carries,
/// so a green run here is also evidence the replay path works.
/// </para>
/// </remarks>
[Collection(TimelineRunCollection.Name)]
public class RunProjectionAgainstRealRunTests(JournalFixture fixture)
{
    private string JournalRoot => fixture.Root;

    [Fact]
    public async Task ARealRunProjectsIntoAGraphThatMatchesIt()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new SetVariableStep("orderId", 42))
            .Name("produce")
            .Trigger(new SetVariableStep("total", 99))
            .Name("consume")
            .Build();

        await timeline.SetupRun().RunAsync();

        RunGraph graph = Replay(out int envelopeCount);

        // If the journal is empty the assertions below would pass vacuously, which would make this
        // test worse than useless.
        Assert.True(envelopeCount > 0, "The run wrote no journal, so nothing was actually verified.");

        // The board matches the timeline that ran: system stages are real, so assert on the one
        // authored here rather than on a fixed stage count.
        StageNode main = graph.Stages.Single(stage => stage.Steps.Any(step => step.DisplayName == "produce"));
        Assert.Equal(2, main.Steps.Count);
        Assert.Equal(["produce", "consume"], main.Steps.Select(step => step.DisplayName));

        // Every authored step reached a terminal state and recorded an attempt.
        Assert.All(main.Steps, step =>
        {
            Assert.Equal(DebugLifecycleState.Complete, step.Lifecycle);
            Assert.NotEmpty(step.Attempts);
        });

        // Values set during the run arrived with real display text, not defaults.
        Assert.Contains("orderId", graph.Variables.Keys);
        Assert.Equal("42", graph.Variables["orderId"].DisplayText);
        Assert.False(string.IsNullOrEmpty(graph.Variables["orderId"].SchemaKey));

        Assert.True(graph.IsFinished);
        Assert.Equal(DebugLifecycleState.Complete, graph.Lifecycle);
    }

    [Fact]
    public async Task AFailingRunCarriesItsReasonThroughToTheAttempt()
    {
        // The end-to-end version of the failure-detail work: an exception raised in a step has to
        // survive Core, the envelope, the journal and the projection to reach the step panel.
        Timeline timeline = Timeline.Create()
            .Trigger(new ThrowingStep("kaboom"))
            .Name("explodes")
            .Build();

        await timeline.SetupRun().RunAsync();

        RunGraph graph = Replay(out _);

        StepNode step = graph.Stages
            .SelectMany(stage => stage.Steps)
            .Single(candidate => candidate.DisplayName == "explodes");

        Assert.Equal(DebugLifecycleState.Error, step.Lifecycle);

        AttemptNode attempt = Assert.Single(step.Attempts);
        Assert.NotNull(attempt.Failure);
        Assert.Equal("kaboom", attempt.Failure!.Message);
        Assert.Contains("InvalidOperationException", attempt.Failure.ExceptionType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayingTheSameJournalTwiceProducesTheSameGraph()
    {
        // Idempotence against real data rather than synthetic signals, which is what a late attach
        // or a reconnect actually exercises.
        Timeline timeline = Timeline.Create()
            .Trigger(new SetVariableStep("orderId", 7))
            .Name("produce")
            .Build();

        await timeline.SetupRun().RunAsync();

        RunGraph once = Replay(out _);
        RunGraph twice = ReplayTwice();

        Assert.Equal(once.Stages.Count, twice.Stages.Count);
        Assert.Equal(once.Variables.Count, twice.Variables.Count);
        Assert.Equal(
            once.Stages.SelectMany(s => s.Steps).Select(s => s.Attempts.Count),
            twice.Stages.SelectMany(s => s.Steps).Select(s => s.Attempts.Count));
    }

    private RunGraph Replay(out int envelopeCount)
    {
        RunGraph graph = new();
        envelopeCount = 0;

        foreach (DebugEnvelope envelope in ReadJournal())
        {
            envelopeCount++;
            graph = Apply(graph, envelope);
        }

        return graph;
    }

    private RunGraph ReplayTwice()
    {
        RunGraph graph = new();

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (DebugEnvelope envelope in ReadJournal())
                graph = Apply(graph, envelope);
        }

        return graph;
    }

    private static RunGraph Apply(RunGraph graph, DebugEnvelope envelope)
    {
        IPipeSignal signal = DebugEnvelopeCodec.Unwrap(envelope);

        return signal switch
        {
            PipeInitTimelineRunSignal init => RunProjection.ApplyInit(init),
            PipeEntityTransitionSignal transition => RunProjection.ApplyTransition(graph, transition),
            PipeValueUpdateSignal value => RunProjection.ApplyValueUpdate(graph, value),
            PipeLogEntrySignal log => RunProjection.ApplyLogEntry(graph, log),
            PipeAssertionSignal assertion => RunProjection.ApplyAssertion(graph, assertion),
            PipeBreakpointHitRequestSignal breakpoint => RunProjection.ApplyBreakpointHit(graph, breakpoint),
            PipeTimelineRunFinishedSignal finished => RunProjection.ApplyRunFinished(graph, finished),
            _ => graph
        };
    }

    private IEnumerable<DebugEnvelope> ReadJournal()
    {
        string runs = Path.Combine(JournalRoot, "runs");
        if (!Directory.Exists(runs))
            yield break;

        string? journal = Directory.EnumerateFiles(runs, "*.ndjson").OrderBy(path => path, StringComparer.Ordinal).LastOrDefault();
        if (journal is null)
            yield break;

        // FileShare.ReadWrite because a run may still hold the file open; the plain File helpers
        // request FileShare.Read and fail against a live writer.
        using FileStream stream = new(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);

        while (reader.ReadLine() is string line)
        {
            if (line.Length > 0)
                yield return DebugEnvelopeCodec.Deserialize(line);
        }
    }

    private sealed class SetVariableStep(string key, int value) : Step<EmptyStepResultContext>
    {
        public override string Name => "set";
        public override string Description => "Sets a variable.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            variableStore.SetVariable(key, value);
            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }

        public override Step<EmptyStepResultContext> Clone() => new SetVariableStep(key, value).WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) => contract.Outputs.Add(new StepIOEntry(key, StepIOKind.Variable));
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    private sealed class ThrowingStep(string message) : Step<EmptyStepResultContext>
    {
        public override string Name => "throwing";
        public override string Description => "Always throws.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);

        public override Step<EmptyStepResultContext> Clone() => new ThrowingStep(message).WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }
}
