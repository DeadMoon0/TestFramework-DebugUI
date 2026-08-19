using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers listing and replaying recorded runs.
/// </summary>
/// <remarks>
/// This is the capability the whole journal exists for: opening a run the UI was never connected
/// for. The tests drive real runs rather than hand-written files, so the listing and the replay are
/// exercised against what Core actually writes.
/// </remarks>
[Collection(TimelineRunCollection.Name)]
public class JournalRunEventSourceTests(JournalFixture fixture)
{
    private string RunsDirectory => Path.Combine(fixture.Root, "runs");

    [Fact]
    public async Task ARecordedRunCanBeListedWithoutReadingItsEvents()
    {
        // Listing must stay cheap however much a run logged, so it reads the sidecars only.
        await RunTimelineAsync("listed");

        ImmutableList<AvailableRun> runs = JournalRunEventSource.ListRuns(RunsDirectory);

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.False(string.IsNullOrWhiteSpace(run.Name)));
        Assert.All(runs, run => Assert.True(File.Exists(run.JournalPath)));
    }

    [Fact]
    public async Task ACompletedRunIsMarkedFinished()
    {
        await RunTimelineAsync("finished");

        AvailableRun run = JournalRunEventSource.ListRuns(RunsDirectory)[0];

        Assert.True(run.IsFinished);
    }

    [Fact]
    public async Task ReplayingARecordedRunRebuildsItsBoard()
    {
        // The headline capability: a run the store never saw live is projected from disk through
        // exactly the path a live run takes.
        await RunTimelineAsync("replayed");

        AvailableRun run = JournalRunEventSource.ListRuns(RunsDirectory)[0];

        using StateStore<MainState> store = StateStore<MainState>.Create().AddReducer(new MainReducer()).Build();
        using RunIngestService ingest = new(store, TimeSpan.FromMilliseconds(10));

        JournalRunEventSource source = new(run.JournalPath);
        source.EnvelopeReceived += ingest.Accept;
        source.Start();
        ingest.Flush();

        Assert.Single(store.GetValue(state => state.Runs));
        Assert.True(store.GetValue(state => state.ActiveRun).IsFinished);

        StepNode step = store.GetValue(state => state.ActiveRun)
            .Stages
            .SelectMany(stage => stage.Steps)
            .Single(candidate => candidate.DisplayName == "replayed");

        Assert.Equal(DebugLifecycleState.Complete, step.Lifecycle);
    }

    [Fact]
    public void AnUnreadableSidecarDoesNotHideTheOtherRuns()
    {
        // A half-written sidecar, or one from a newer build, must not blank the picker.
        Directory.CreateDirectory(RunsDirectory);
        File.WriteAllText(Path.Combine(RunsDirectory, "99999999-999999999-corrupt.meta.json"), "{ not json");

        ImmutableList<AvailableRun> runs = JournalRunEventSource.ListRuns(RunsDirectory);

        Assert.DoesNotContain(runs, run => run.SessionId == "corrupt");
    }

    [Fact]
    public void ARecordingFromAnotherProtocolVersionIsNotOfferedButIsCounted()
    {
        // The alternative is worse than hiding it: every event in it fails to decode, so the run opens as a
        // board with a name and nothing on it, and nothing says why.
        Directory.CreateDirectory(RunsDirectory);

        File.WriteAllText(
            Path.Combine(RunsDirectory, "20200101-000000000-fromanotherbuild.meta.json"),
            $$"""
            {
              "ProtocolVersion": {{DebugProtocol.Version - 1}},
              "SessionId": "fromanotherbuild",
              "Name": "OlderRun",
              "StartedAtUtc": "2020-01-01T00:00:00+00:00",
              "JournalFileName": "20200101-000000000-fromanotherbuild.ndjson",
              "ProjectPath": "old.csproj",
              "Outcome": 1
            }
            """);

        Assert.DoesNotContain(JournalRunEventSource.ListRuns(RunsDirectory), run => run.SessionId == "fromanotherbuild");
        Assert.True(JournalRunEventSource.CountFromOtherBuilds(RunsDirectory) >= 1);
    }

    [Fact]
    public void ListingAnAbsentDirectoryIsEmptyRatherThanAnError()
    {
        // What a machine that has never run a test looks like.
        Assert.Empty(JournalRunEventSource.ListRuns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public async Task ATornFinalLineDoesNotDiscardTheRunBeforeIt()
    {
        // A killed host can leave a partial last line. Everything up to it is still the record of
        // what happened, and is exactly what someone would open the UI to see.
        await RunTimelineAsync("torn");

        AvailableRun run = JournalRunEventSource.ListRuns(RunsDirectory)[0];
        File.AppendAllText(run.JournalPath, "{\"V\":2,\"SessionId\":\"x\",\"Seq\":9");

        int count = 0;
        JournalRunEventSource source = new(run.JournalPath);
        source.EnvelopeReceived += _ => count++;
        source.Start();

        Assert.True(count > 0, "The torn line discarded the run's history.");
    }

    private static async Task RunTimelineAsync(string label)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new NoopStep())
            .Name(label)
            .Build();

        await timeline.SetupRun().RunAsync();
    }

    private sealed class NoopStep : Step<EmptyStepResultContext>
    {
        public override string Name => "noop";
        public override string Description => "Does nothing.";
        public override bool DoesReturn => false;

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
            => Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);

        public override Step<EmptyStepResultContext> Clone() => new NoopStep().WithClonedOptions(this);
        public override void DeclareIO(StepIOContract contract) { }
        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }
}
