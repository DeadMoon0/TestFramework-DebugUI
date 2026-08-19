using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers coalescing incoming envelopes into batched dispatches.
/// </summary>
public class RunIngestServiceTests : IDisposable
{
    private readonly List<IDisposable> disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in disposables) disposable.Dispose();
        GC.SuppressFinalize(this);
    }

    private (StateStore<MainState> Store, RunIngestService Ingest, Counter Counter) Create(TimeSpan? window = null)
    {
        StateStore<MainState> store = StateStore<MainState>.Create().AddReducer(new MainReducer()).Build();
        RunIngestService ingest = new(store, window ?? TimeSpan.FromMilliseconds(30));

        Counter counter = new();
        store.Bind(state => state.Runs).Subscribe(_ => counter.Increment());

        disposables.Add(ingest);
        disposables.Add(store);

        return (store, ingest, counter);
    }

    [Fact]
    public void ABurstBecomesOneDispatch()
    {
        // The reason batching exists: a log-heavy run emits thousands of events per second, and
        // each dispatch clones the state and notifies every binding.
        (StateStore<MainState> store, RunIngestService ingest, Counter counter) = Create();

        counter.Reset();

        ingest.Accept(Init("s1"));
        for (int i = 0; i < 200; i++)
            ingest.Accept(StepRunning("s1", at: i + 1));

        ingest.Flush();

        Assert.Equal(1, counter.Count);
        Assert.Single(store.GetValue(state => state.Runs));
    }

    [Fact]
    public void ABreakpointIsDeliveredWithoutWaitingForTheWindow()
    {
        // A paused run is waiting for a human. Making them wait an extra window to find out is the
        // one case where latency matters more than throughput.
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromSeconds(30));

        ingest.Accept(Init("s1"));
        ingest.Accept(Breakpoint("s1"));

        // No flush, and a window far longer than this test would wait.
        Assert.True(store.GetValue(state => state.Runs)[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public void ABreakpointCarriesTheEventsThatLedToItAlong()
    {
        // Flushing only the breakpoint would show a pause before the events explaining it.
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromSeconds(30));

        ingest.Accept(Init("s1"));
        ingest.Accept(StepRunning("s1", at: 1));
        ingest.Accept(Breakpoint("s1"));

        Assert.Equal(DebugLifecycleState.Running, store.GetValue(state => state.ActiveRun).Stages[0].Steps[0].Lifecycle);
        Assert.True(store.GetValue(state => state.ActiveRun).Stages[0].Steps[0].IsWaitingAtBreakpoint);
    }

    [Fact]
    public async Task TheWindowFlushesOnItsOwn()
    {
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromMilliseconds(20));

        ingest.Accept(Init("s1"));

        // No explicit flush: the timer is what has to deliver this.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (store.GetValue(state => state.Runs.Count) == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Single(store.GetValue(state => state.Runs));
    }

    [Fact]
    public void FlushingAnEmptyBufferDispatchesNothing()
    {
        (_, RunIngestService ingest, Counter counter) = Create();

        counter.Reset();
        ingest.Flush();
        ingest.Flush();

        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void DisposingDeliversWhatWasAlreadyAccepted()
    {
        // Events accepted have been reported as received; dropping them on shutdown would lose the
        // tail of the last run.
        (StateStore<MainState> store, RunIngestService ingest, _) = Create(window: TimeSpan.FromSeconds(30));

        ingest.Accept(Init("s1"));
        ingest.Dispose();

        Assert.Single(store.GetValue(state => state.Runs));
    }

    private sealed class Counter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);

        public void Reset() => Volatile.Write(ref count, 0);
    }

    private static DebugEnvelope Init(string sessionId) => DebugEnvelopeCodec.Wrap(new PipeInitTimelineRunSignal
    {
        SessionId = sessionId,
        Name = "Run",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step()] }],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    }, 1);

    private static DebugEnvelope StepRunning(string sessionId, int at) => DebugEnvelopeCodec.Wrap(new PipeEntityTransitionSignal
    {
        SessionId = sessionId,
        EntityKind = DebugEntityKind.Step,
        Stage = "Main",
        StepId = 0,
        State = DebugLifecycleState.Running,
        OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at)
    }, 2);

    private static DebugEnvelope Breakpoint(string sessionId) => DebugEnvelopeCodec.Wrap(new PipeBreakpointHitRequestSignal
    {
        SessionId = sessionId,
        Stage = "Main",
        StepId = 0
    }, 3);

    private static DebugStepState Step() => new()
    {
        Name = "A",
        Description = "A",
        DoesReturn = false,
        Phase = StepExecutionPhase.Act,
        Parallelization = StepParallelizationMode.Parallelizable
    };
}
