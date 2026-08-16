using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Shell;

/// <summary>
/// Whether the debugger is hearing anything.
/// </summary>
/// <remarks>
/// Reports the transport only, never the run. A failing test leaves this green, because the test
/// failing is the tool working — showing an alarm there would teach the reader to ignore the one
/// indicator that means the tool itself is broken.
/// </remarks>
public partial class UC_TransportStatus : UserControl
{
    private static readonly Dictionary<TransportStatus, string> Labels = new()
    {
        [TransportStatus.Idle] = "Idle",
        [TransportStatus.Listening] = "Listening",
        [TransportStatus.Attached] = "Attached",
        [TransportStatus.Faulted] = "Transport failed"
    };

    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates the indicator and binds it.</summary>
    public UC_TransportStatus()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.Transport)
            .Select(status => Labels[status])
            .BindToDependencyProperty(tbStatus, TextBlock.TextProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.Transport)
            .Select(BrushFor)
            .BindToDependencyProperty(bIndicator, Border.BackgroundProperty));

        // Disposed rather than discarded. The reference app leaks every one of these; a window that
        // is opened and closed repeatedly would keep every dead control subscribed to the store.
        Unloaded += (_, _) => subscriptions.Dispose();
    }

    private Brush BrushFor(TransportStatus status) => status switch
    {
        TransportStatus.Attached => (Brush)FindResource("StateRunning"),
        TransportStatus.Listening => (Brush)FindResource("StateComplete"),
        TransportStatus.Faulted => (Brush)FindResource("StateError"),
        _ => (Brush)FindResource("StateNotRun")
    };
}
