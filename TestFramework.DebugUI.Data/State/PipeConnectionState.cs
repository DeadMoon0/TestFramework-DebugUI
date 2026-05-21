using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Bindable state for the named pipe transport that feeds the DebugUI.
/// It captures connection lifecycle, current session identity, counters, and diagnostic text.
/// </summary>
public class PipeConnectionState : StateObject
{
    public string PipeName { get => GetValue(PipeNameProperty); set => SetValue(PipeNameProperty, value); }
    public static StateProperty<string> PipeNameProperty { get; } = Property(nameof(PipeName), "");

    public PipeConnectionStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public static StateProperty<PipeConnectionStatus> StatusProperty { get; } = Property(nameof(Status), PipeConnectionStatus.Idle);

    public bool IsConnected { get => GetValue(IsConnectedProperty); set => SetValue(IsConnectedProperty, value); }
    public static StateProperty<bool> IsConnectedProperty { get; } = Property(nameof(IsConnected), false);

    public string ConnectedSessionId { get => GetValue(ConnectedSessionIdProperty); set => SetValue(ConnectedSessionIdProperty, value); }
    public static StateProperty<string> ConnectedSessionIdProperty { get; } = Property(nameof(ConnectedSessionId), "");

    public string LastSessionId { get => GetValue(LastSessionIdProperty); set => SetValue(LastSessionIdProperty, value); }
    public static StateProperty<string> LastSessionIdProperty { get; } = Property(nameof(LastSessionId), "");

    public int ConnectionCount { get => GetValue(ConnectionCountProperty); set => SetValue(ConnectionCountProperty, value); }
    public static StateProperty<int> ConnectionCountProperty { get; } = Property(nameof(ConnectionCount), 0);

    public int DisconnectCount { get => GetValue(DisconnectCountProperty); set => SetValue(DisconnectCountProperty, value); }
    public static StateProperty<int> DisconnectCountProperty { get; } = Property(nameof(DisconnectCount), 0);

    public DateTimeOffset? LastConnectedAtUtc { get => GetValue(LastConnectedAtUtcProperty); set => SetValue(LastConnectedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastConnectedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastConnectedAtUtc), null);

    public DateTimeOffset? LastDisconnectedAtUtc { get => GetValue(LastDisconnectedAtUtcProperty); set => SetValue(LastDisconnectedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastDisconnectedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastDisconnectedAtUtc), null);

    public DateTimeOffset? LastUpdatedAtUtc { get => GetValue(LastUpdatedAtUtcProperty); set => SetValue(LastUpdatedAtUtcProperty, value); }
    public static StateProperty<DateTimeOffset?> LastUpdatedAtUtcProperty { get; } = Property<DateTimeOffset?>(nameof(LastUpdatedAtUtc), null);

    public string LastDisconnectReason { get => GetValue(LastDisconnectReasonProperty); set => SetValue(LastDisconnectReasonProperty, value); }
    public static StateProperty<string> LastDisconnectReasonProperty { get; } = Property(nameof(LastDisconnectReason), "");

    public string LastFailureReason { get => GetValue(LastFailureReasonProperty); set => SetValue(LastFailureReasonProperty, value); }
    public static StateProperty<string> LastFailureReasonProperty { get; } = Property(nameof(LastFailureReason), "");

    public string DebugInfo { get => GetValue(DebugInfoProperty); set => SetValue(DebugInfoProperty, value); }
    public static StateProperty<string> DebugInfoProperty { get; } = Property(nameof(DebugInfo), "");
}