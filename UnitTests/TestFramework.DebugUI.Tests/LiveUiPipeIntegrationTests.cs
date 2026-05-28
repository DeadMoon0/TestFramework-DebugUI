using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Variables;
using TestFramework.DebugUI;
using TestFramework.Core.Timelines;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Tests.Support;
using TestFramework.LocalIO;
using TestFramework.Simple;
using TestFrameworkDebugUI;
using TestFrameworkDebugUI.Controls.TimelineBoard.Timeline;
using TestFrameworkDebugUI.Controls.TimelineBoard.Timeline.TimelineItem;
using WpfStateService.Dispatching;

namespace TestFramework.DebugUI.Tests;

[Collection("WpfHost")]
public sealed class LiveUiPipeIntegrationTests
{
    [Fact]
    public async Task MainWindow_HostsPipeSession_AndTracksConnectionLifecycle()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        await using WpfHostHandle host = await WpfHostHandle.StartAsync();

        bool reachedListeningState = await WaitUntilAsync(
            () => host.ReadStateAsync(state =>
                state.PipeConnection.Status == PipeConnectionStatus.Listening
                && state.PipeConnection.PipeName == pipeScope.PipeName),
            TimeSpan.FromSeconds(10));

        if (!reachedListeningState)
        {
            PipeSnapshot initialSnapshot = await host.ReadStateAsync(state => new PipeSnapshot(
                state.PipeConnection.PipeName,
                state.PipeConnection.Status,
                state.PipeConnection.ConnectionCount,
                state.PipeConnection.DisconnectCount,
                state.PipeConnection.ConnectedSessionId,
                state.PipeConnection.LastSessionId,
                state.PipeConnection.LastDisconnectReason,
                state.PipeConnection.LastFailureReason,
                state.PipeConnection.DebugInfo,
                state.ActiveRun?.SessionId,
                state.ActiveRun?.IsFinished ?? false));

            Assert.Fail($"DebugUI never reached the listening state. Expected pipe '{pipeScope.PipeName}', actual pipe '{initialSnapshot.PipeName}', status '{initialSnapshot.Status}', last failure '{initialSnapshot.LastFailureReason}', debug '{initialSnapshot.DebugInfo}'.");
        }

        Timeline timeline = Timeline.Create()
            .Trigger(SimpleExt.Trigger.Action(() => Thread.Sleep(500)))
            .Name("hold-open")
            .Build();

        Task<TimelineRun> runTask = timeline.SetupRun().RunAsync();

        await WaitUntilAsync(
            () => host.ReadStateAsync(state =>
                state.PipeConnection.Status == PipeConnectionStatus.Connected
                && state.PipeConnection.IsConnected
                && state.PipeConnection.ConnectionCount == 1
                && !string.IsNullOrWhiteSpace(state.PipeConnection.ConnectedSessionId)),
            TimeSpan.FromSeconds(10),
            "DebugUI never observed an attached pipe session.");

        TimelineRun run = await runTask;
        run.EnsureRanToCompletion();

        await WaitUntilAsync(
            () => host.ReadStateAsync(state =>
                state.PipeConnection.Status == PipeConnectionStatus.Disconnected
                && !state.PipeConnection.IsConnected
                && state.PipeConnection.DisconnectCount == 1
                && state.ActiveRun is not null
                && state.ActiveRun.IsFinished),
            TimeSpan.FromSeconds(10),
            "DebugUI never observed the completed disconnect state.");

        PipeSnapshot snapshot = await host.ReadStateAsync(state => new PipeSnapshot(
            state.PipeConnection.PipeName,
            state.PipeConnection.Status,
            state.PipeConnection.ConnectionCount,
            state.PipeConnection.DisconnectCount,
            state.PipeConnection.ConnectedSessionId,
            state.PipeConnection.LastSessionId,
            state.PipeConnection.LastDisconnectReason,
            state.PipeConnection.LastFailureReason,
            state.PipeConnection.DebugInfo,
            state.ActiveRun?.SessionId,
            state.ActiveRun?.IsFinished ?? false));

        Assert.Equal(pipeScope.PipeName, snapshot.PipeName);
        Assert.Equal(PipeConnectionStatus.Disconnected, snapshot.Status);
        Assert.Equal(1, snapshot.ConnectionCount);
        Assert.Equal(1, snapshot.DisconnectCount);
        Assert.Equal(string.Empty, snapshot.ConnectedSessionId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.LastSessionId));
        Assert.Equal(snapshot.ActiveRunSessionId, snapshot.LastSessionId);
        Assert.Equal("Run completed.", snapshot.LastDisconnectReason);
        Assert.Equal(string.Empty, snapshot.LastFailureReason);
        Assert.True(snapshot.ActiveRunFinished);
        Assert.Contains("Piped Debugger Attached", snapshot.DebugInfo);
        Assert.Contains("Piped Debugger Dettached (Run completed.)", snapshot.DebugInfo);
    }

    [Fact]
    public async Task MainWindow_RebuildsTimelineVisuals_WhenActiveRunIsReplaced()
    {
        await using WpfHostHandle host = await WpfHostHandle.StartAsync();

        await WaitUntilAsync(
            () => host.InvokeAsync(window => FindDescendant<UC_Timeline>(window) is not null),
            TimeSpan.FromSeconds(10),
            "Timeline control never loaded.");

        await host.InvokeAsync(async _ =>
        {
            DebugRunStateReducer reducer = new(MainWindow.State);
            await reducer.ApplyInitTimelineRunAsync(CreateRunSignal("session-1", "First Run", [CreateStage("Stage A", 1)]));
        });

        await WaitUntilAsync(
            () => host.InvokeAsync(window => GetTimelineVisualCount(window) == 2),
            TimeSpan.FromSeconds(10),
            "Timeline did not render the first run shape.");

        await host.InvokeAsync(async _ =>
        {
            DebugRunStateReducer reducer = new(MainWindow.State);
            await reducer.ApplyInitTimelineRunAsync(CreateRunSignal("session-2", "Second Run", [CreateStage("Stage B", 3)]));
        });

        await WaitUntilAsync(
            () => host.InvokeAsync(window => GetTimelineVisualCount(window) == 4),
            TimeSpan.FromSeconds(10),
            "Timeline did not rebuild for the replacement run.");

        int timelineVisualCount = await host.InvokeAsync(GetTimelineVisualCount);
        Assert.Equal(4, timelineVisualCount);
    }

    [Fact]
    public async Task MainWindow_UpdatesTimelineItemVisuals_Across_SetupArtifact_Then_LocalIo_Run()
    {
        await using WpfHostHandle host = await WpfHostHandle.StartAsync();
        DebugRunStateReducer reducer = new(MainWindow.State);

        await reducer.ApplyInitTimelineRunAsync(CreateRunSignal("artifact-only", "SetupArtifact", []));
        await reducer.ApplyTimelineRunFinishedAsync(new TimelineRunFinishedSignal { SessionId = "artifact-only" });

        await reducer.ApplyInitTimelineRunAsync(CreateRunSignal("localio", "LocalIo", [CreateStageWithArtifactOutput("Main", "event-file")]));
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "localio",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Running,
            PreviousState = DebugLifecycleState.Initialized
        });
        await reducer.ApplyValueUpdateAsync(new ValueUpdateSignal
        {
            SessionId = "localio",
            Name = "event-file",
            ValueKind = DebugValueKind.Artifact,
            Stage = "Main",
            StepId = 0,
            Envelope = new DebugValueEnvelope
            {
                Kind = DebugValueKind.Artifact,
                TypeName = "System.String",
                DisplayText = "event-ready",
                SchemaKey = "artifact/string",
                Core = new Newtonsoft.Json.Linq.JObject { ["value"] = "event-ready" }
            }
        });
        await reducer.ApplyEntityTransitionAsync(new EntityTransitionSignal
        {
            SessionId = "localio",
            EntityKind = DebugEntityKind.Step,
            Stage = "Main",
            StepId = 0,
            State = DebugLifecycleState.Complete,
            PreviousState = DebugLifecycleState.Running,
            OutcomeState = DebugLifecycleState.Complete
        });
        await reducer.ApplyTimelineRunFinishedAsync(new TimelineRunFinishedSignal { SessionId = "localio" });

        await WaitUntilAsync(
            () => host.InvokeAsync(window => GetTimelineItemCount(window) > 0),
            TimeSpan.FromSeconds(10),
            "Replacement run never rendered any timeline items.");

        await WaitUntilAsync(
            () => host.InvokeAsync(window => GetTimelineItemOutputCount(window) > 0),
            TimeSpan.FromSeconds(10),
            "Replacement run never rendered any timeline item output visuals.");

        int timelineItemCount = await host.InvokeAsync(GetTimelineItemCount);
        int outputCount = await host.InvokeAsync(GetTimelineItemOutputCount);

        Assert.True(timelineItemCount > 0);
        Assert.True(outputCount > 0);
    }

    [Fact]
    public async Task MainWindow_Tracks_RealSampleSequence_WhileUiStaysOpen()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        await using WpfHostHandle host = await WpfHostHandle.StartAsync();

        await WaitUntilAsync(
            () => host.ReadStateAsync(state =>
                state.PipeConnection.Status == PipeConnectionStatus.Listening
                && state.PipeConnection.PipeName == pipeScope.PipeName),
            TimeSpan.FromSeconds(10),
            "DebugUI never reached the listening state for the persistent sample sequence.");

        string artifactVersionPath = CreateUniqueSampleFile("debugui-live-version");
        string artifactVersionFile = Path.GetFileName(artifactVersionPath);
        string localIoPath = CreateUniqueSampleFile("debugui-live-event");
        string localIoFile = Path.GetFileName(localIoPath);
        int retryAttempts = 0;

        try
        {
            Timeline artifactVersionTimeline = Timeline.Create()
                .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdAppendFirst"), Var.Ref<string>("cwd")))
                .RegisterArtifact("log-file", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
                .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdAppendSecond"), Var.Ref<string>("cwd")))
                .CaptureArtifactVersion("log-file", "after-second-write")
                .Name("ArtifactVersionJourney")
                .Build();

            Timeline forEachTimeline = Timeline.Create()
                .ForEach(new[] { "alpha", "beta", "gamma" }, "item", loop =>
                {
                    loop.Trigger(SimpleExt.Trigger.Action(_ => { }, Var.Ref<string>("item")));
                })
                .Build();

            Timeline retryTimeline = Timeline.Create()
                .Trigger(SimpleExt.Trigger.Action(() =>
                {
                    retryAttempts++;
                    if (retryAttempts < 3)
                    {
                        throw new InvalidOperationException("Planned sample failure.");
                    }
                }))
                .Name("RetrySample")
                .WithRetry(2, CalcDelays.None)
                .Build();

            Timeline localIoTimeline = Timeline.Create()
                .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
                .WaitForEvent(LocalIOExt.Events.FileExists(Var.Ref<string>("artifactPath")))
                .WithTimeOut(TimeSpan.FromSeconds(10))
                .RegisterArtifact("event-file", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
                .Name("LocalIoEventAndArtifact")
                .Build();

            TimelineRun artifactVersionRun = await artifactVersionTimeline.SetupRun()
                .AddVariable("cmdAppendFirst", AppendText(artifactVersionFile, "line-one"))
                .AddVariable("cmdAppendSecond", AppendText(artifactVersionFile, "line-two"))
                .AddVariable("cwd", AppContext.BaseDirectory)
                .AddVariable("artifactPath", artifactVersionPath)
                .RunAsync();
            artifactVersionRun.EnsureRanToCompletion();
            await WaitForUiRunAsync(host, expectedConnectionCount: 1);

            TimelineRun forEachRun = await forEachTimeline.SetupRun().RunAsync();
            forEachRun.EnsureRanToCompletion();
            await WaitForUiRunAsync(host, expectedConnectionCount: 2);

            TimelineRun retryRun = await retryTimeline.SetupRun().RunAsync();
            retryRun.EnsureRanToCompletion();
            await WaitForUiRunAsync(host, expectedConnectionCount: 3);

            TimelineRun localIoRun = await localIoTimeline.SetupRun()
                .AddVariable("cmdCreate", DelayedWriteText(localIoFile, "event-ready"))
                .AddVariable("cwd", AppContext.BaseDirectory)
                .AddVariable("artifactPath", localIoPath)
                .RunAsync();
            localIoRun.EnsureRanToCompletion();
            await WaitForUiRunAsync(host, expectedConnectionCount: 4, minimumOutputCount: 1);

            Assert.Equal(3, retryAttempts);

            PipeSnapshot snapshot = await host.ReadStateAsync(state => new PipeSnapshot(
                state.PipeConnection.PipeName,
                state.PipeConnection.Status,
                state.PipeConnection.ConnectionCount,
                state.PipeConnection.DisconnectCount,
                state.PipeConnection.ConnectedSessionId,
                state.PipeConnection.LastSessionId,
                state.PipeConnection.LastDisconnectReason,
                state.PipeConnection.LastFailureReason,
                state.PipeConnection.DebugInfo,
                state.ActiveRun?.SessionId,
                state.ActiveRun?.IsFinished ?? false));

            Assert.Equal(pipeScope.PipeName, snapshot.PipeName);
            Assert.Equal(4, snapshot.ConnectionCount);
            Assert.Equal(4, snapshot.DisconnectCount);
            Assert.Equal(PipeConnectionStatus.Disconnected, snapshot.Status);
            Assert.Equal("Run completed.", snapshot.LastDisconnectReason);
            Assert.Equal(string.Empty, snapshot.LastFailureReason);
        }
        finally
        {
            TryDeleteFile(artifactVersionPath);
            TryDeleteFile(localIoPath);
        }
    }

    [Fact]
    public async Task MainWindow_ReplaysCompletedRun_WhenUiIsReopened()
    {
        using PipeTestScope pipeScope = PipeTestScope.Create();
        using DebugStoreScope storeScope = DebugStoreScope.Create();

        string sessionId;

        await using (WpfHostHandle firstHost = await WpfHostHandle.StartAsync())
        {
            await WaitUntilAsync(
                () => firstHost.ReadStateAsync(state =>
                    state.PipeConnection.Status == PipeConnectionStatus.Listening
                    && state.PipeConnection.PipeName == pipeScope.PipeName),
                TimeSpan.FromSeconds(10),
                "DebugUI never reached the listening state for the reopen test.");

            Timeline timeline = Timeline.Create()
                .Trigger(SimpleExt.Trigger.Action(() => { }))
                .Name("PersistedUiReplay")
                .Build();

            TimelineRun run = await timeline.SetupRun().RunAsync();
            run.EnsureRanToCompletion();

            await WaitUntilAsync(
                () => firstHost.ReadStateAsync(state => state.ActiveRun is not null && state.ActiveRun.IsFinished),
                TimeSpan.FromSeconds(10),
                "DebugUI never observed the completed run before shutdown.");

            sessionId = await firstHost.ReadStateAsync(state => state.ActiveRun!.SessionId);
        }

        await using WpfHostHandle reopenedHost = await WpfHostHandle.StartAsync();

        await WaitUntilAsync(
            () => reopenedHost.ReadStateAsync(state =>
                state.ActiveRun is not null
                && state.ActiveRun.SessionId == sessionId
                && state.ActiveRun.IsFinished
                && state.PipeConnection.Status == PipeConnectionStatus.Listening
                && !state.PipeConnection.IsConnected
                && state.PipeConnection.ConnectedSessionId == string.Empty),
            TimeSpan.FromSeconds(10),
            "Reopened DebugUI did not replay the completed persisted run.");

        PipeSnapshot snapshot = await reopenedHost.ReadStateAsync(state => new PipeSnapshot(
            state.PipeConnection.PipeName,
            state.PipeConnection.Status,
            state.PipeConnection.ConnectionCount,
            state.PipeConnection.DisconnectCount,
            state.PipeConnection.ConnectedSessionId,
            state.PipeConnection.LastSessionId,
            state.PipeConnection.LastDisconnectReason,
            state.PipeConnection.LastFailureReason,
            state.PipeConnection.DebugInfo,
            state.ActiveRun?.SessionId,
            state.ActiveRun?.IsFinished ?? false));

        Assert.Equal(pipeScope.PipeName, snapshot.PipeName);
        Assert.Equal(PipeConnectionStatus.Listening, snapshot.Status);
        Assert.Equal(sessionId, snapshot.ActiveRunSessionId);
        Assert.True(snapshot.ActiveRunFinished);
        Assert.Equal(string.Empty, snapshot.ConnectedSessionId);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, string failureMessage)
    {
        if (await WaitUntilAsync(predicate, timeout))
        {
            return;
        }

        Assert.Fail(failureMessage);
    }

    private sealed record PipeSnapshot(
        string PipeName,
        PipeConnectionStatus Status,
        int ConnectionCount,
        int DisconnectCount,
        string ConnectedSessionId,
        string LastSessionId,
        string LastDisconnectReason,
        string LastFailureReason,
        string DebugInfo,
        string? ActiveRunSessionId,
        bool ActiveRunFinished);

    private static async Task WaitForUiRunAsync(WpfHostHandle host, int expectedConnectionCount, int minimumOutputCount = 0)
    {
        bool completedStateObserved = await WaitUntilAsync(
            () => host.ReadStateAsync(state =>
                state.ActiveRun is not null
                && state.ActiveRun.IsFinished
                && state.PipeConnection.ConnectionCount == expectedConnectionCount
                && state.PipeConnection.DisconnectCount == expectedConnectionCount),
            TimeSpan.FromSeconds(15));

        if (!completedStateObserved)
        {
            PipeSnapshot snapshot = await host.ReadStateAsync(state => new PipeSnapshot(
                state.PipeConnection.PipeName,
                state.PipeConnection.Status,
                state.PipeConnection.ConnectionCount,
                state.PipeConnection.DisconnectCount,
                state.PipeConnection.ConnectedSessionId,
                state.PipeConnection.LastSessionId,
                state.PipeConnection.LastDisconnectReason,
                state.PipeConnection.LastFailureReason,
                state.PipeConnection.DebugInfo,
                state.ActiveRun?.SessionId,
                state.ActiveRun?.IsFinished ?? false));
            int actualTimelineItemCount = await host.InvokeAsync(GetTimelineItemCount);
            int actualOutputCount = await host.InvokeAsync(GetTimelineItemOutputCount);

            Assert.Fail(
                $"DebugUI state never reflected a completed run for connection #{expectedConnectionCount}. "
                + $"Status={snapshot.Status}, Connections={snapshot.ConnectionCount}, Disconnects={snapshot.DisconnectCount}, "
                + $"ConnectedSession='{snapshot.ConnectedSessionId}', LastSession='{snapshot.LastSessionId}', ActiveRun='{snapshot.ActiveRunSessionId}', "
                + $"ActiveRunFinished={snapshot.ActiveRunFinished}, LastDisconnect='{snapshot.LastDisconnectReason}', LastFailure='{snapshot.LastFailureReason}', "
                + $"TimelineItems={actualTimelineItemCount}, Outputs={actualOutputCount}, Debug='{snapshot.DebugInfo}'.");
        }

        int expectedTimelineItemCount = await host.ReadStateAsync(state =>
            state.ActiveRun is null
                ? 0
                : DebugRunStateQueries.GetOrderedStages(state.ActiveRun)
                    .Sum(stage => DebugRunStateQueries.GetOrderedSteps(stage).Count));

        await WaitUntilAsync(
            () => host.InvokeAsync(window => GetTimelineItemCount(window) == expectedTimelineItemCount),
            TimeSpan.FromSeconds(15),
            $"Timeline items never rebuilt to {expectedTimelineItemCount} for connection #{expectedConnectionCount}.");

        if (minimumOutputCount > 0)
        {
            await WaitUntilAsync(
                () => host.InvokeAsync(window => GetTimelineItemOutputCount(window) >= minimumOutputCount),
                TimeSpan.FromSeconds(15),
                $"Timeline outputs never reached {minimumOutputCount} for connection #{expectedConnectionCount}.");
        }
    }

    private static InitTimelineRunSignal CreateRunSignal(string sessionId, string name, params DebugStageState[] stages)
    {
        return new InitTimelineRunSignal
        {
            SessionId = sessionId,
            Name = name,
            ProjectPath = "project.csproj",
            RunStructure = new TimelineRunStructure
            {
                Variables = new Dictionary<VariableIdentifier, VariableState>(),
                Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
                Stages = stages
            }
        };
    }

    private static DebugStageState CreateStage(string name, int stepCount)
    {
        return new DebugStageState
        {
            Name = name,
            Description = $"{name} description",
            Steps = Enumerable.Range(0, stepCount)
                .Select(index => new DebugStepState
                {
                    Name = $"Step {index + 1}",
                    Description = $"Step {index + 1} description",
                    DoesReturn = true,
                    ErrorHandlingOptions = new ErrorHandlingOptions(),
                    ExecutionOptions = new ExecutionOptions(),
                    IOContract = new StepIOContract(),
                    Phase = StepExecutionPhase.Act,
                    LabelOptions = new LabelOptions(),
                    RetryOptions = new RetryOptions(),
                    TimeOutOptions = new TimeOutOptions()
                })
                .ToArray()
        };
    }

    private static DebugStageState CreateStageWithArtifactOutput(string stageName, string artifactKey)
    {
        StepIOContract ioContract = new();
        ioContract.Outputs.Add(new StepIOEntry(artifactKey, StepIOKind.Artifact, true, typeof(string)));

        return new DebugStageState
        {
            Name = stageName,
            Description = $"{stageName} description",
            Steps =
            [
                new DebugStepState
                {
                    Name = "LocalIo step",
                    Description = "Produces an artifact output",
                    DoesReturn = true,
                    ErrorHandlingOptions = new ErrorHandlingOptions(),
                    ExecutionOptions = new ExecutionOptions(),
                    IOContract = ioContract,
                    Phase = StepExecutionPhase.Act,
                    LabelOptions = new LabelOptions(),
                    RetryOptions = new RetryOptions(),
                    TimeOutOptions = new TimeOutOptions()
                }
            ]
        };
    }

    private static string CreateUniqueSampleFile(string prefix)
        => Path.Combine(AppContext.BaseDirectory, $"{prefix}-{Guid.NewGuid():N}.txt");

    private static string AppendText(string fileName, string text)
        => $"echo {text} >> \"{fileName}\"";

    private static string DelayedWriteText(string fileName, string text)
        => OperatingSystem.IsWindows()
            ? $"ping 127.0.0.1 -n 2 > nul & echo {text} > \"{fileName}\""
            : $"sleep 1; echo {text} > \"{fileName}\"";

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static int GetTimelineVisualCount(MainWindow window)
    {
        UC_Timeline timeline = Assert.IsType<UC_Timeline>(FindDescendant<UC_Timeline>(window));
        StackPanel content = Assert.IsType<StackPanel>(FindDescendant<StackPanel>(timeline));
        return content.Children.Count;
    }

    private static int GetTimelineItemCount(MainWindow window)
    {
        return FindDescendants<UC_TimelineItem>(window).Count();
    }

    private static int GetTimelineItemOutputCount(MainWindow window)
    {
        return FindDescendants<UC_TimelineItem>(window)
            .Select(item => FindNamedDescendant<StackPanel>(item, "spOutput"))
            .Where(panel => panel is not null)
            .Sum(panel => panel!.Children.Count);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed)
        {
            return typed;
        }

        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < children; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            T? match = FindDescendant<T>(child);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed)
        {
            yield return typed;
        }

        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < children; index++)
        {
            foreach (T match in FindDescendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return match;
            }
        }
    }

    private static T? FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        return FindDescendants<T>(root).FirstOrDefault(element => StringComparer.Ordinal.Equals(element.Name, name));
    }

    private sealed class WpfHostHandle(MainWindow window, Thread thread, Task closedTask, bool ownsPipeNameOverride) : IAsyncDisposable
    {
        private const string PipeNameEnvironmentVariable = "TESTFRAMEWORK_DEBUG_PIPE_NAME";

        public static async Task<WpfHostHandle> StartAsync()
        {
            TaskCompletionSource<MainWindow> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            string? existingPipeName = Environment.GetEnvironmentVariable(PipeNameEnvironmentVariable);
            bool ownsPipeNameOverride = string.IsNullOrWhiteSpace(existingPipeName);
            string pipeName = existingPipeName ?? $"TestFrameworkDebugTests_{Guid.NewGuid():N}";

            Thread thread = new(() =>
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    Environment.SetEnvironmentVariable(PipeNameEnvironmentVariable, pipeName);
                    MainWindow.State = new MainState();

                    MainWindow window = new();
                    window.Loaded += (_, _) => ready.TrySetResult(window);
                    window.Closed += (_, _) =>
                    {
                        closed.TrySetResult();
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    };

                    window.Show();
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    ready.TrySetException(ex);
                    closed.TrySetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "DebugUI-WpfHost-Test"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            MainWindow window = await ready.Task;
            await window.WaitUntilPipeReadyAsync();
            return new WpfHostHandle(window, thread, closed.Task, ownsPipeNameOverride);
        }

        public Task<T> ReadStateAsync<T>(Func<MainState, T> selector)
        {
            return window.Dispatcher.InvokeAsync(() => selector(MainWindow.State)).Task;
        }

        public Task<T> InvokeAsync<T>(Func<MainWindow, T> selector)
        {
            return window.Dispatcher.InvokeAsync(() => selector(window)).Task;
        }

        public Task InvokeAsync(Func<MainWindow, Task> action)
        {
            return window.Dispatcher.InvokeAsync(() => action(window)).Task.Unwrap();
        }

        public async ValueTask DisposeAsync()
        {
            await window.Dispatcher.InvokeAsync(() =>
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            });

            await closedTask;
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF host thread did not exit cleanly.");
            if (ownsPipeNameOverride)
            {
                Environment.SetEnvironmentVariable(PipeNameEnvironmentVariable, null);
            }
            StateTestHelpers.EnsureDispatcherInitialized();
        }
    }
}