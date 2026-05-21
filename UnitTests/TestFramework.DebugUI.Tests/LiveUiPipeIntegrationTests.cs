using System.Windows;
using System.Windows.Threading;
using TestFramework.Core.Timelines;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Tests.Support;
using TestFramework.Simple;
using TestFrameworkDebugUI;
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

        await WaitUntilAsync(
            () => host.ReadStateAsync(state =>
                state.PipeConnection.Status == PipeConnectionStatus.Listening
                && state.PipeConnection.PipeName == pipeScope.PipeName),
            TimeSpan.FromSeconds(10),
            "DebugUI never reached the listening state.");

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

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, string failureMessage)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(25);
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

    private sealed class WpfHostHandle(MainWindow window, Thread thread, Task closedTask) : IAsyncDisposable
    {
        public static async Task<WpfHostHandle> StartAsync()
        {
            TaskCompletionSource<MainWindow> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Thread thread = new(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                    MainWindow.State = new MainState();

                    App app = new()
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    app.InitializeComponent();

                    MainWindow window = new();
                    app.MainWindow = window;
                    window.Loaded += (_, _) => ready.TrySetResult(window);
                    window.Closed += (_, _) => closed.TrySetResult();

                    app.Run(window);
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
            return new WpfHostHandle(window, thread, closed.Task);
        }

        public Task<T> ReadStateAsync<T>(Func<MainState, T> selector)
        {
            return window.Dispatcher.InvokeAsync(() => selector(MainWindow.State)).Task;
        }

        public async ValueTask DisposeAsync()
        {
            await window.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current is not null)
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    window.Close();
                }
            });

            await closedTask;
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF host thread did not exit cleanly.");
            StateTestHelpers.EnsureDispatcherInitialized();
        }
    }
}