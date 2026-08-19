using Axiom.State;
using Axiom.State.Actions;
using Axiom.State.Reducers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;
using Xunit.Abstractions;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Guards the shape decision the whole state model rests on.
/// </summary>
/// <remarks>
/// Axiom deep-clones the entire state on every dispatch, and <c>Scope</c>/<c>Selector</c> narrow
/// what a reducer writes, not what is copied. The model therefore keeps the run graph behind
/// immutable class references so the clone walks a handful of nodes instead of the whole run. If
/// someone later "simplifies" the graph into nested value types, these tests are what notices —
/// the symptom in the app would just be a UI that gets slower as runs get bigger.
/// </remarks>
public class StateCloneBudgetTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<IDisposable> stores = [];

    public void Dispose()
    {
        foreach (IDisposable store in stores) store.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly StateAction<RunGraph> SetActiveRunAction = new(nameof(StateCloneBudgetTests), nameof(SetActiveRunAction));
    private static readonly StateAction<string> SelectRunAction = new(nameof(StateCloneBudgetTests), nameof(SelectRunAction));

    private class MainReducer : Reducer<MainState>
    {
        public MainReducer()
        {
            On(SetActiveRunAction, (state, graph) => state with { ActiveRun = graph });
            On(SelectRunAction, (state, sessionId) => state with { SelectedSessionId = sessionId });
        }
    }

    private StateStore<MainState> CreateStore()
    {
        StateStore<MainState> store = StateStore<MainState>.Create().AddReducer(new MainReducer()).Build();
        stores.Add(store);
        return store;
    }

    [Fact]
    public void ALargeRunGraphDoesNotSlowDispatch()
    {
        StateStore<MainState> store = CreateStore();
        store.Dispatch(SetActiveRunAction, BuildRun(stages: 10, stepsPerStage: 20, logsPerStep: 50));

        // Warm the compiled cloner so the measurement is steady-state rather than first-use.
        for (int i = 0; i < 20; i++) store.Dispatch(SelectRunAction, i.ToString());

        Stopwatch stopwatch = Stopwatch.StartNew();
        const int iterations = 200;
        for (int i = 0; i < iterations; i++) store.Dispatch(SelectRunAction, i.ToString());
        stopwatch.Stop();

        double perDispatch = stopwatch.Elapsed.TotalMilliseconds / iterations;
        output.WriteLine($"200 steps, 10000 log entries: {perDispatch:F4} ms per dispatch");

        // Generous by two orders of magnitude against what the value-typed shape measured, so this
        // fails on a design regression rather than on a loaded machine.
        Assert.True(perDispatch < 1.0, $"Dispatch cost {perDispatch:F4} ms suggests the run graph is being deep-cloned.");
    }

    [Fact]
    public void GraphSizeDoesNotChangeDispatchCost()
    {
        // The property that matters: cost is flat in the size of the run, because the graph is one
        // reference. A value-typed graph would make this scale with the step and log count.
        StateStore<MainState> small = CreateStore();
        small.Dispatch(SetActiveRunAction, BuildRun(stages: 1, stepsPerStage: 1, logsPerStep: 1));

        StateStore<MainState> large = CreateStore();
        large.Dispatch(SetActiveRunAction, BuildRun(stages: 20, stepsPerStage: 40, logsPerStep: 100));

        double smallCost = Measure(small);
        double largeCost = Measure(large);

        output.WriteLine($"small={smallCost:F4} ms  large={largeCost:F4} ms");

        Assert.True(
            largeCost < smallCost + 0.5,
            $"Dispatch cost grew with run size (small {smallCost:F4} ms, large {largeCost:F4} ms), so the graph is being copied rather than referenced.");
    }

    [Fact]
    public void ReplacingTheGraphLeavesTheEarlierSnapshotIntact()
    {
        // Reducers replace the reference; nothing mutates through it. Otherwise the previous state
        // changes underneath anyone still holding it — and because the reference did not move, the
        // change can go unnoticed entirely.
        StateStore<MainState> store = CreateStore();

        RunGraph first = BuildRun(stages: 1, stepsPerStage: 2, logsPerStep: 1);
        store.Dispatch(SetActiveRunAction, first);
        RunGraph captured = store.GetValue(state => state.ActiveRun);

        store.Dispatch(SetActiveRunAction, BuildRun(stages: 3, stepsPerStage: 4, logsPerStep: 1));

        Assert.Single(captured.Stages);
        Assert.Equal(3, store.GetValue(state => state.ActiveRun).Stages.Count);
    }

    [Fact]
    public void UnselectedRunsCostOnlyASummary()
    {
        // Many sessions ingested, one rendered. Twenty parallel runs must not each drag a projected
        // graph through the per-dispatch clone.
        StateStore<MainState> store = CreateStore();
        store.Dispatch(SetActiveRunAction, BuildRun(stages: 10, stepsPerStage: 20, logsPerStep: 50));

        double before = Measure(store);

        ImmutableList<RunSummary>.Builder summaries = ImmutableList.CreateBuilder<RunSummary>();
        for (int i = 0; i < 20; i++)
            summaries.Add(new RunSummary { SessionId = $"s{i}", Name = $"Run {i}", IsLive = true });

        StateStore<MainState> withSummaries = CreateStore();
        withSummaries.Dispatch(SetActiveRunAction, BuildRun(stages: 10, stepsPerStage: 20, logsPerStep: 50));

        double after = Measure(withSummaries);

        output.WriteLine($"one run={before:F4} ms  twenty sessions={after:F4} ms");
        Assert.True(after < before + 0.5, "Tracking more sessions should not measurably change dispatch cost.");
    }

    private static double Measure(StateStore<MainState> store)
    {
        for (int i = 0; i < 20; i++) store.Dispatch(SelectRunAction, i.ToString());

        Stopwatch stopwatch = Stopwatch.StartNew();
        const int iterations = 200;
        for (int i = 0; i < iterations; i++) store.Dispatch(SelectRunAction, "m" + i);
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds / iterations;
    }

    private static RunGraph BuildRun(int stages, int stepsPerStage, int logsPerStep)
    {
        ImmutableList<StageNode>.Builder stageBuilder = ImmutableList.CreateBuilder<StageNode>();

        for (int s = 0; s < stages; s++)
        {
            ImmutableList<StepNode>.Builder stepBuilder = ImmutableList.CreateBuilder<StepNode>();

            for (int i = 0; i < stepsPerStage; i++)
            {
                ImmutableList<LogNode>.Builder logBuilder = ImmutableList.CreateBuilder<LogNode>();
                for (int l = 0; l < logsPerStep; l++)
                    logBuilder.Add(new LogNode { Template = $"log {l}", Level = DebugLogLevel.Information });

                stepBuilder.Add(new StepNode
                {
                    StepId = i,
                    Name = $"Step {i}",
                    Lifecycle = DebugLifecycleState.Complete,
                    Attempts = ImmutableList.Create(new AttemptNode { Attempt = 1, Logs = logBuilder.ToImmutable() })
                });
            }

            stageBuilder.Add(new StageNode { Name = $"Stage {s}", Steps = stepBuilder.ToImmutable() });
        }

        return new RunGraph { Stages = stageBuilder.ToImmutable() };
    }
}
