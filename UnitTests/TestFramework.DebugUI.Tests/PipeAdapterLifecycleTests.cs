using System.Collections.Concurrent;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.PipeAdapter;
using TestFramework.DebugUI.Tests.Support;

namespace TestFramework.DebugUI.Tests;

[Collection("WpfHost")]
public sealed class PipeAdapterLifecycleTests
{
    public PipeAdapterLifecycleTests()
    {
        StateTestHelpers.EnsureDispatcherInitialized();
    }

    [Fact]
    public async Task Host_RejectsNonInitFirstSignal_AndAcceptsFollowingConnection()
    {
        using PipeTestScope scope = PipeTestScope.Create();
        using RecordingPipeHost host = StartHost();

        await host.WaitUntilReadyAsync().WaitAsync(TimeSpan.FromSeconds(5));

        string badSessionId = "bad-session";
        string goodSessionId = Guid.NewGuid().ToString("N");

        using RunDebuggerPiped badDebugger = new RunDebuggerPiped();
        await badDebugger.SignalEntityTransitionAsync(badSessionId, DebugEntityKind.Run, null, null, DebugLifecycleState.Initialized);

        StateTestHelpers.Eventually(
            () => host.DetachReasons.Any(reason => reason.Contains("Expected InitTimelineRun as first signal but received EntityTransition.", StringComparison.Ordinal)),
            "Expected host to detach the invalid first-signal connection.");

        using RunDebuggerPiped goodDebugger = new RunDebuggerPiped();
        await goodDebugger.SignalInitTimelineRunAsync(goodSessionId, "Run", "project.csproj", CreateRunStructure());
        await goodDebugger.SignalTimelineRunFinishedAsync(goodSessionId);

        StateTestHelpers.Eventually(() => host.InitializedSessionIds.Contains(goodSessionId), "Expected host to accept the next clean connection.");
        StateTestHelpers.Eventually(
            () => host.DetachReasons.Any(reason => reason.Contains("Run completed.", StringComparison.Ordinal)),
            "Expected host to detach after timeline completion.");
    }

    [Fact]
    public async Task Debugger_CreatedBeforeHostReady_CanStillAttach_WhenFirstSignalIsSent()
    {
        using PipeTestScope scope = PipeTestScope.Create();
        using RunDebuggerPiped debugger = new RunDebuggerPiped();
        using RecordingPipeHost host = StartHost();

        await host.WaitUntilReadyAsync().WaitAsync(TimeSpan.FromSeconds(5));

        string sessionId = Guid.NewGuid().ToString("N");
        await debugger.SignalInitTimelineRunAsync(sessionId, "Run", "project.csproj", CreateRunStructure());
        await debugger.SignalTimelineRunFinishedAsync(sessionId);

        StateTestHelpers.Eventually(() => host.InitializedSessionIds.Contains(sessionId), "Expected deferred connection to attach after the host became ready.");
    }

    private static RecordingPipeHost StartHost()
    {
        RecordingPipeHost host = new RecordingPipeHost();
        host.Begin();
        return host;
    }

    private static TimelineRunStructure CreateRunStructure()
    {
        return new TimelineRunStructure
        {
            Variables = new Dictionary<VariableIdentifier, VariableState>(),
            Artifacts = new Dictionary<ArtifactIdentifier, TestFramework.Core.Debugger.ArtifactState>(),
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

    private sealed class RecordingPipeHost : RunDebuggerHostPiped
    {
        public ConcurrentQueue<string> DetachReasons { get; } = new();

        public ConcurrentQueue<string> InitializedSessionIds { get; } = new();

        protected override Task OnPipeConnectionAttachedAsync()
        {
            return Task.CompletedTask;
        }

        protected override Task OnPipeConnectionDetachedAsync(string reason)
        {
            DetachReasons.Enqueue(reason);
            return Task.CompletedTask;
        }

        internal override Task OnEntityTransitionAsync(EntityTransitionSignal signal)
        {
            return Task.CompletedTask;
        }

        internal override Task OnInitTimelineRunAsync(InitTimelineRunSignal signal)
        {
            InitializedSessionIds.Enqueue(signal.SessionId);
            return Task.CompletedTask;
        }

        internal override Task OnTimelineRunFinishedAsync(TimelineRunFinishedSignal signal)
        {
            return Task.CompletedTask;
        }

        internal override Task OnValueUpdateAsync(ValueUpdateSignal signal)
        {
            return Task.CompletedTask;
        }

        internal override Task OnLogEntryAsync(LogEntrySignal signal)
        {
            return Task.CompletedTask;
        }

        internal override Task OnAssertionAsync(AssertionSignal signal)
        {
            return Task.CompletedTask;
        }

        internal override Task OnBreakpointHitRequestAsync(BreakpointHitRequestSignal signal)
        {
            return Task.CompletedTask;
        }
    }
}