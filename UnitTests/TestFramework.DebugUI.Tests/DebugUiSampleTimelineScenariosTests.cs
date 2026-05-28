using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Tests.Support;

namespace TestFramework.DebugUI.Tests;

public class DebugUiSampleTimelineScenariosTests
{
    public DebugUiSampleTimelineScenariosTests()
    {
        StateTestHelpers.EnsureDispatcherInitialized();
    }

    public static IEnumerable<object[]> ScenarioData()
    {
        return DebugUiSampleTimelineScenarios.All.Select(scenario => new object[] { scenario });
    }

    [Theory]
    [MemberData(nameof(ScenarioData))]
    public async Task SampleScenario_CanBeProjectedIntoCanonicalUiState(DebugUiSampleScenario scenario)
    {
        MainState mainState = new MainState();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyInitTimelineRunAsync(new InitTimelineRunSignal
        {
            SessionId = "sample-session",
            Name = scenario.Key,
            ProjectPath = $"samples/{scenario.Key}.timeline",
            RunStructure = scenario.RunStructure
        });

        await scenario.DriveAsync(reducer);

        RunState runState = Assert.IsType<RunState>(mainState.ActiveRun);
        IReadOnlyList<StageNodeState> stages = DebugRunStateQueries.GetOrderedStages(runState);

        Assert.NotEmpty(stages);
        Assert.Equal(scenario.Key, runState.Name);
        Assert.All(stages, stage =>
        {
            Assert.NotEmpty(DebugRunStateQueries.GetExecutionLayerKeys(stage));
            Assert.NotEmpty(DebugRunStateQueries.GetOrderedSteps(stage));
        });
    }

    [Fact]
    public async Task LocalIoPipelineScenario_ExposesArtifactsLogsAndMaterializedLayers()
    {
        DebugUiSampleScenario scenario = DebugUiSampleTimelineScenarios.All.Single(candidate => candidate.Key == "LocalIO Pipeline");
        MainState mainState = await ExecuteScenarioAsync(scenario);

        StageNodeState stageState = Assert.Single(DebugRunStateQueries.GetOrderedStages(mainState.ActiveRun!));
        StepNodeState cmdTriggerStep = DebugRunStateQueries.GetOrderedSteps(stageState).Single(step => step.Name == "CmdTrigger");
        StepNodeState fileExistsStep = DebugRunStateQueries.GetOrderedSteps(stageState).Single(step => step.Name == "FileExistsEvent");

        Assert.True(mainState.ActiveRun!.Artifacts.ContainsKey("stdout"));
        Assert.True(mainState.ActiveRun.Artifacts.ContainsKey("resultFile"));
        Assert.Equal("stdout.log", mainState.ActiveRun.Artifacts["stdout"].Envelope.DisplayText);
        Assert.Equal("results.json", mainState.ActiveRun.Artifacts["resultFile"].Envelope.DisplayText);
        Assert.Contains("Command finished with exit code 0.", DebugRunStateQueries.GetDebugOut(DebugRunStateQueries.GetLatestAttempt(cmdTriggerStep)!));
        Assert.Contains("Detected result file.", DebugRunStateQueries.GetDebugOut(DebugRunStateQueries.GetLatestAttempt(fileExistsStep)!));
    }

    [Fact]
    public async Task RetryScenario_ProducesTwoAttemptsWithTimeoutThenSuccess()
    {
        DebugUiSampleScenario scenario = DebugUiSampleTimelineScenarios.All.Single(candidate => candidate.Key == "Retry And Timeout");
        MainState mainState = await ExecuteScenarioAsync(scenario);

        StepNodeState stepState = DebugRunStateQueries.GetOrderedSteps(Assert.Single(DebugRunStateQueries.GetOrderedStages(mainState.ActiveRun!))).Single();
        IReadOnlyList<StepAttemptState> attempts = DebugRunStateQueries.GetOrderedAttempts(stepState);

        Assert.Equal(2, attempts.Count);
        Assert.Equal(DebugLifecycleState.Timeout, attempts[0].LifecycleState);
        Assert.Equal(DebugLifecycleState.Complete, attempts[1].LifecycleState);
        Assert.Contains("Retry succeeded.", DebugRunStateQueries.GetDebugOut(attempts[1]));
    }

    [Fact]
    public async Task BreakpointScenario_PreservesBreakpointState_ForInspectorUi()
    {
        DebugUiSampleScenario scenario = DebugUiSampleTimelineScenarios.All.Single(candidate => candidate.Key == "Breakpoint Inspection");
        MainState mainState = await ExecuteScenarioAsync(scenario);

        StageNodeState stageState = Assert.Single(DebugRunStateQueries.GetOrderedStages(mainState.ActiveRun!));
        StepNodeState inspectStep = DebugRunStateQueries.GetOrderedSteps(stageState).First(step => step.Name == "Inspect Payload");
        StepAttemptState latestAttempt = Assert.IsType<StepAttemptState>(DebugRunStateQueries.GetLatestAttempt(inspectStep));

        Assert.True(inspectStep.BreakpointHitCount > 0);
        Assert.NotNull(inspectStep.LastBreakpointAtUtc);
        Assert.Contains("Breakpoint acknowledged; awaiting continue.", DebugRunStateQueries.GetDebugOut(latestAttempt));
    }

    private static async Task<MainState> ExecuteScenarioAsync(DebugUiSampleScenario scenario)
    {
        MainState mainState = new MainState();
        DebugRunStateReducer reducer = new DebugRunStateReducer(mainState);

        await reducer.ApplyInitTimelineRunAsync(new InitTimelineRunSignal
        {
            SessionId = "sample-session",
            Name = scenario.Key,
            ProjectPath = $"samples/{scenario.Key}.timeline",
            RunStructure = scenario.RunStructure
        });

        await scenario.DriveAsync(reducer);
        return mainState;
    }
}