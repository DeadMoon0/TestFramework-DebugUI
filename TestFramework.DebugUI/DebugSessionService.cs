using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.DebugUI;
using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.State;

namespace TestFrameworkDebugUI;

internal sealed class DebugSessionService : IDisposable
{
    private static readonly object Sync = new();
    private static DebugSessionStore sharedStore = new();
    private static DebugSessionService? current;

    private readonly MainState mainState;
    private readonly DebugRunStateReducer reducer;
    private readonly DebugBrokerHost brokerHost;
    private readonly Task startupTask;
    private int disposed;

    private DebugSessionService(MainState mainState)
    {
        this.mainState = mainState;
        reducer = new DebugRunStateReducer(mainState);
        brokerHost = new DebugBrokerHost(sharedStore, ShouldPauseAtBreakpoint);
        brokerHost.PipeServerReady += OnPipeServerReadyAsync;
        brokerHost.PipeConnectionAttached += OnPipeConnectionAttachedAsync;
        brokerHost.PipeConnectionDetached += OnPipeConnectionDetachedAsync;
        brokerHost.SignalStored += OnSignalStoredAsync;
        startupTask = InitializeAsync();
    }

    internal static DebugSessionService EnsureStarted(MainState mainState)
    {
        lock (Sync)
        {
            if (current is not null && ReferenceEquals(current.mainState, mainState))
                return current;

            current?.Dispose();
            current = new DebugSessionService(mainState);
            return current;
        }
    }

    internal static void StopCurrent()
    {
        lock (Sync)
        {
            current?.Dispose();
            current = null;
        }
    }

    internal Task WaitUntilReadyAsync()
    {
        return startupTask;
    }

    internal IReadOnlyList<StoredDebugSessionInfo> GetStoredSessions()
    {
        return brokerHost.Store.ListSessions();
    }


    internal Task<bool> ToggleStepBreakpointAsync(string stageName, int stepId)
    {
        var run = mainState.ActiveRun;
        if (run == null)
            return Task.FromResult(false);

        string key = BreakpointConfig.CreateKey(run.ProjectPath, run.Name, stageName, stepId);
        if (mainState.BreakpointConfigs.ContainsKey(key))
        {
            mainState.BreakpointConfigs.Remove(key);
            return Task.FromResult(false);
        }

        mainState.BreakpointConfigs[key] = new BreakpointConfig
        {
            Key = key,
            ProjectPath = run.ProjectPath,
            TestName = run.Name,
            StageName = stageName,
            StepId = stepId
        };
        return Task.FromResult(true);
    }

    internal async Task<bool> ContinueActiveBreakpointAsync()
    {
        if (!brokerHost.TryContinueActiveBreakpoint(out string sessionId, out string stageName, out int stepId))
            return false;

        await reducer.ApplyBreakpointReleasedAsync(sessionId, stageName, stepId);
        return true;
    }

    internal static void ResetSharedStoreForTests()
    {
        lock (Sync)
        {
            sharedStore = new DebugSessionStore();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
            return;

        brokerHost.PipeServerReady -= OnPipeServerReadyAsync;
        brokerHost.PipeConnectionAttached -= OnPipeConnectionAttachedAsync;
        brokerHost.PipeConnectionDetached -= OnPipeConnectionDetachedAsync;
        brokerHost.SignalStored -= OnSignalStoredAsync;
        brokerHost.Dispose();
    }

    private async Task InitializeAsync()
    {
        string? lastSessionId = await ReplayPersistedSessionsAsync();
        brokerHost.Begin();
        await brokerHost.WaitUntilReadyAsync();
        await NormalizeConnectionStateAfterReplayAsync(lastSessionId);
    }

    private async Task<string?> ReplayPersistedSessionsAsync()
    {
        string? lastSessionId = null;
        foreach (StoredDebugSessionInfo session in brokerHost.Store.ListSessions().OrderBy(session => session.CreatedAtUtc))
        {
            lastSessionId = session.SessionId;
            foreach (StoredDebugSignalEnvelope envelope in brokerHost.Store.LoadSignals(session.SessionId).OrderBy(signal => signal.SequenceNumber))
                await ApplySignalAsync(envelope.DeserializeSignal());
        }

        return lastSessionId;
    }

    private Task OnPipeServerReadyAsync(string pipeName)
    {
        return reducer.ApplyPipeServerReadyAsync(pipeName);
    }

    private Task OnPipeConnectionAttachedAsync()
    {
        return reducer.ApplyPipeConnectionAttachedAsync(brokerHost.BrokerPipeName);
    }

    private Task OnPipeConnectionDetachedAsync(string reason)
    {
        return reducer.ApplyPipeConnectionDetachedAsync(reason);
    }

    private Task OnSignalStoredAsync(StoredDebugSignalEnvelope envelope)
    {
        return ApplySignalAsync(envelope.DeserializeSignal());
    }

    private Task ApplySignalAsync(ISignal signal)
    {
        return signal switch
        {
            InitTimelineRunSignal initTimelineRunSignal => ApplyInitTimelineRunAsync(initTimelineRunSignal),
            EntityTransitionSignal entityTransitionSignal => reducer.ApplyEntityTransitionAsync(entityTransitionSignal),
            ValueUpdateSignal valueUpdateSignal => reducer.ApplyValueUpdateAsync(valueUpdateSignal),
            LogEntrySignal logEntrySignal => reducer.ApplyLogEntryAsync(logEntrySignal),
            AssertionSignal assertionSignal => reducer.ApplyAssertionAsync(assertionSignal),
            BreakpointHitRequestSignal breakpointHitRequestSignal => reducer.ApplyBreakpointHitRequestAsync(breakpointHitRequestSignal),
            TimelineRunFinishedSignal timelineRunFinishedSignal => reducer.ApplyTimelineRunFinishedAsync(timelineRunFinishedSignal),
            _ => Task.CompletedTask
        };
    }

    private Task NormalizeConnectionStateAfterReplayAsync(string? lastSessionId)
    {
        return reducer.ApplyReplayRecoveredAsync(lastSessionId);
    }

    private async Task ApplyInitTimelineRunAsync(InitTimelineRunSignal signal)
    {
        await reducer.ApplyInitTimelineRunAsync(signal);
    }

    private bool ShouldPauseAtBreakpoint(BreakpointHitRequestSignal signal)
    {
        if (!TryGetRunForSession(signal.SessionId, out RunState runState))
            return false;

        string key = BreakpointConfig.CreateKey(runState.ProjectPath, runState.Name, signal.Stage, signal.StepId);
        return mainState.BreakpointConfigs.ContainsKey(key);
    }

    private bool TryGetRunForSession(string sessionId, out RunState runState)
    {
        if (mainState.ActiveRun is not null && StringComparer.Ordinal.Equals(mainState.ActiveRun.SessionId, sessionId))
        {
            runState = mainState.ActiveRun;
            return true;
        }

        return mainState.CompletedRuns.TryGetValue(sessionId, out runState!);
    }
}