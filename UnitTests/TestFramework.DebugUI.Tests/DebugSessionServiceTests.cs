using TestFramework.Core.Debugger;
using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Tests.Support;
using TestFrameworkDebugUI;

namespace TestFramework.DebugUI.Tests;

public sealed class DebugSessionServiceTests
{
    public DebugSessionServiceTests()
    {
        StateTestHelpers.EnsureDispatcherInitialized();
        DebugSessionService.StopCurrent();
    }

    [Fact]
    public async Task Service_ReplaysPersistedCompletedRun_OnRestart()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        using DebugStoreScope storeScope = DebugStoreScope.Create();

        string sessionId;

        MainState firstState = new();
        using (DebugSessionService firstService = DebugSessionService.EnsureStarted(firstState))
        {
            await firstService.WaitUntilReadyAsync();
            using RunDebuggerPiped debugger = new();

            sessionId = Guid.NewGuid().ToString("N");
            await debugger.SignalInitTimelineRunAsync(sessionId, "Persisted Run", "project.csproj", CreateRunStructure());
            await debugger.SignalTimelineRunFinishedAsync(sessionId);

            StateTestHelpers.Eventually(() => firstState.ActiveRun?.SessionId == sessionId && firstState.ActiveRun.IsFinished, "Expected first service to observe a finished persisted run.");
            StateTestHelpers.Eventually(
                () => firstState.PipeConnection.Status == PipeConnectionStatus.Disconnected,
                "Expected the first service to observe the pipe disconnect after the finished run.");
            Assert.Equal(PipeConnectionStatus.Disconnected, firstState.PipeConnection.Status);
            Assert.Equal(pipeScope.PipeName, firstState.PipeConnection.PipeName);
        }

        DebugSessionService.StopCurrent();

        MainState secondState = new();
        using (DebugSessionService secondService = DebugSessionService.EnsureStarted(secondState))
        {
            await secondService.WaitUntilReadyAsync();

            Assert.NotNull(secondState.ActiveRun);
            Assert.Equal(sessionId, secondState.ActiveRun!.SessionId);
            Assert.True(secondState.ActiveRun.IsFinished);
            Assert.Equal(PipeConnectionStatus.Listening, secondState.PipeConnection.Status);
            Assert.False(secondState.PipeConnection.IsConnected);
            Assert.Equal(string.Empty, secondState.PipeConnection.ConnectedSessionId);
            Assert.Equal(sessionId, secondState.PipeConnection.LastSessionId);
            Assert.Single(secondService.GetStoredSessions());
        }
    }

    [Fact]
    public async Task ContinueActiveBreakpointAsync_ReleasesWaitingDebuggerSignal()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        using DebugStoreScope storeScope = DebugStoreScope.Create();

        MainState state = new();
        using DebugSessionService service = DebugSessionService.EnsureStarted(state);
        await service.WaitUntilReadyAsync();

        using RunDebuggerPiped debugger = new();
        string sessionId = Guid.NewGuid().ToString("N");

        await debugger.SignalInitTimelineRunAsync(sessionId, "Breakpoint Run", "project.csproj", CreateRunStructureWithSingleStep());
        StateTestHelpers.Eventually(() => state.ActiveRun?.SessionId == sessionId, "Expected breakpoint run to be active before toggling breakpoint.");
        await service.ToggleStepBreakpointAsync("Main", 0);

        Task waitTask = debugger.SignalAndWaitBreakpointHitAsync(sessionId, "Main", 0);

        StateTestHelpers.Eventually(() => state.HasPendingBreakpoint, "Expected a pending breakpoint to be visible in the UI state.");
        Assert.False(waitTask.IsCompleted);

        bool continued = await service.ContinueActiveBreakpointAsync();

        Assert.True(continued);
        StateTestHelpers.Eventually(() => !state.HasPendingBreakpoint, "Expected the pending breakpoint flag to clear once continue was acknowledged.");
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ToggleStepBreakpointAsync_PersistsConfiguredBreakpointIntoNextRun()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        using DebugStoreScope storeScope = DebugStoreScope.Create();

        MainState state = new();
        using DebugSessionService service = DebugSessionService.EnsureStarted(state);
        await service.WaitUntilReadyAsync();

        string firstSessionId = Guid.NewGuid().ToString("N");
        string secondSessionId = Guid.NewGuid().ToString("N");

        using RunDebuggerPiped firstDebugger = new();

        await firstDebugger.SignalInitTimelineRunAsync(firstSessionId, "Breakpoint Test", "project.csproj", CreateRunStructureWithSingleStep());
        StateTestHelpers.Eventually(() => state.ActiveRun?.SessionId == firstSessionId, "Expected first run to be active.");

        bool enabled = await service.ToggleStepBreakpointAsync("Main", 0);
        string key = BreakpointConfig.CreateKey("project.csproj", "Breakpoint Test", "Main", 0);

        Assert.True(enabled);
        Assert.True(state.BreakpointConfigs.ContainsKey(key));

        await firstDebugger.SignalTimelineRunFinishedAsync(firstSessionId);

        using RunDebuggerPiped secondDebugger = new();
        await secondDebugger.SignalInitTimelineRunAsync(secondSessionId, "Breakpoint Test", "project.csproj", CreateRunStructureWithSingleStep());

        StateTestHelpers.Eventually(() => state.ActiveRun?.SessionId == secondSessionId, "Expected second run to become active.");
        Assert.True(state.BreakpointConfigs.ContainsKey(key));
    }

    [Fact]
    public async Task ToggleStepBreakpointAsync_AllowsMultipleBreakpointsAcrossTests()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        using DebugStoreScope storeScope = DebugStoreScope.Create();

        MainState state = new();
        using DebugSessionService service = DebugSessionService.EnsureStarted(state);
        await service.WaitUntilReadyAsync();

        using RunDebuggerPiped debugger = new();
        await debugger.SignalInitTimelineRunAsync(Guid.NewGuid().ToString("N"), "Test-A", "project.csproj", CreateRunStructureWithSingleStep());
        StateTestHelpers.Eventually(() => state.ActiveRun?.Name == "Test-A", "Expected Test-A run to become active.");

        Assert.True(await service.ToggleStepBreakpointAsync("Main", 0));
        string keyA = BreakpointConfig.CreateKey("project.csproj", "Test-A", "Main", 0);
        Assert.True(state.BreakpointConfigs.ContainsKey(keyA));

        await debugger.SignalInitTimelineRunAsync(Guid.NewGuid().ToString("N"), "Test-B", "project.csproj", CreateRunStructureWithSingleStep());
        StateTestHelpers.Eventually(() => state.ActiveRun?.Name == "Test-B", "Expected Test-B run to become active.");

        Assert.True(await service.ToggleStepBreakpointAsync("Main", 0));
        string keyB = BreakpointConfig.CreateKey("project.csproj", "Test-B", "Main", 0);
        Assert.True(state.BreakpointConfigs.ContainsKey(keyB));

        Assert.Equal(2, state.BreakpointConfigs.Count);
    }

    private static TimelineRunStructure CreateRunStructure()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<TestFramework.Core.Variables.VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<TestFramework.Core.Artifacts.ArtifactIdentifier, ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Main stage",
                    Steps = []
                }
            ]
        };
    }

    private static TimelineRunStructure CreateRunStructureWithSingleStep()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<TestFramework.Core.Variables.VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<TestFramework.Core.Artifacts.ArtifactIdentifier, ArtifactState>(),
            Stages =
            [
                new DebugStageState
                {
                    Name = "Main",
                    Description = "Main stage",
                    Steps =
                    [
                        new DebugStepState
                        {
                            Name = "Step 1",
                            Description = "Breakpoint step",
                            DoesReturn = true,
                            ErrorHandlingOptions = new TestFramework.Core.Steps.Options.ErrorHandlingOptions(),
                            ExecutionOptions = new TestFramework.Core.Steps.Options.ExecutionOptions(),
                            IOContract = new TestFramework.Core.Steps.Options.StepIOContract(),
                            Phase = TestFramework.Core.Steps.Options.StepExecutionPhase.Act,
                            LabelOptions = new TestFramework.Core.Steps.Options.LabelOptions(),
                            RetryOptions = new TestFramework.Core.Steps.Options.RetryOptions(),
                            TimeOutOptions = new TestFramework.Core.Steps.Options.TimeOutOptions()
                        }
                    ]
                }
            ]
        };
    }
}