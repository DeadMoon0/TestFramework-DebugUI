using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.Controls.Shell;

/// <summary>
/// Whether the debugger is hearing anything, and on click, what from.
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

    /// <summary>Kept only while the popup is open, so a closed popup costs nothing to keep current.</summary>
    private IDisposable? liveDetails;

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
        Unloaded += (_, _) =>
        {
            liveDetails?.Dispose();
            subscriptions.Dispose();
        };
    }

    private void btBadge_Click(object sender, RoutedEventArgs e) => popDetails.IsOpen = !popDetails.IsOpen;

    /// <summary>
    /// Puts the details centred under the badge.
    /// </summary>
    /// <remarks>
    /// Both sizes are measured rather than assumed: the badge's width changes with its label — "Idle"
    /// and "Transport failed" are not the same size — and the popup's comes from its own content.
    /// Returning a single candidate means WPF will still slide it to keep it on screen, which is the
    /// one adjustment worth having.
    /// </remarks>
    public CustomPopupPlacement[] PlaceUnderBadge(Size popupSize, Size targetSize, Point offset)
        => [new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + 6), PopupPrimaryAxis.Horizontal)];

    /// <summary>
    /// Keeps the popup current for as long as it is open.
    /// </summary>
    /// <remarks>
    /// A snapshot would be wrong here more often than right: the reason to open this is usually that
    /// something is connecting or finishing right now, and a list that froze the moment it appeared
    /// would be answering the question as it was a second ago.
    /// </remarks>
    private void popDetails_Opened(object sender, EventArgs e)
    {
        liveDetails = StateStore<MainState>.Default
            .Bind(state => state.Runs)
            .Subscribe(Describe);
    }

    private void popDetails_Closed(object sender, EventArgs e)
    {
        liveDetails?.Dispose();
        liveDetails = null;
    }

    private void Describe(ImmutableList<RunSummary> runs)
    {
        PipeRunEventSource pipe = MainWindow.Shell.Pipe;

        int attached = pipe.AttachedRunCount;

        // Never composed with the status word. Pairing them produced "Attached - nothing attached",
        // which was true of a sticky status and nonsense to read.
        tbGeneral.Text = attached switch
        {
            0 => "Nothing attached",
            1 => "1 run attached",
            _ => $"{attached} runs attached at once"
        };

        tbGeneralNote.Text = $"Up to {pipe.MaxConcurrentRuns} at once, one connection each.";
        tbPipeName.Text = pipe.PipeName;

        // Named from the transport, described from the store: the transport knows which sessions are
        // connected, and the store knows what each of them is doing.
        ImmutableList<string> sessionIds = pipe.AttachedSessionIds.ToImmutableList();

        spConnections.Children.Clear();

        foreach (string sessionId in sessionIds)
        {
            RunSummary? run = runs.FirstOrDefault(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal));

            spConnections.Children.Add(Row(sessionId, run));
        }

        tbNoConnections.Visibility = sessionIds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One connection, and what it is doing.</summary>
    private UIElement Row(string sessionId, RunSummary? run)
    {
        StackPanel lines = new() { Margin = new Thickness(0, 0, 0, 8) };

        lines.Children.Add(new TextBlock
        {
            Text = run?.ShortName ?? "Connected",
            Style = (Style)FindResource("BodyText"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        lines.Children.Add(new TextBlock
        {
            Text = Doing(run, sessionId),
            Style = (Style)FindResource("MutedText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0)
        });

        return lines;
    }

    /// <summary>
    /// What a connection is doing, in the terms someone watching would use.
    /// </summary>
    /// <remarks>
    /// A connection with no run in the store has connected but not yet sent its structure — that gap is
    /// real and worth naming, because it is what "attached but the board is empty" looks like.
    /// </remarks>
    private static string Doing(RunSummary? run, string sessionId)
    {
        if (run is null)
            return $"{Short(sessionId)} — waiting for structure";

        if (run.IsWaitingAtBreakpoint)
            return "Held at a breakpoint";

        if (run.IsLive)
        {
            return run.Progress is { } progress && progress.Steps > 0
                ? $"Running — {progress.Complete} of {progress.Steps} steps"
                : "Running";
        }

        return $"Finished — {run.Health.ToString().ToLowerInvariant()}";
    }

    /// <summary>Enough of a session id to tell two apart without filling the row with it.</summary>
    private static string Short(string sessionId)
        => sessionId.Length <= 8 ? sessionId : sessionId[..8];

    private Brush BrushFor(TransportStatus status) => status switch
    {
        TransportStatus.Attached => (Brush)FindResource("StateRunning"),
        TransportStatus.Listening => (Brush)FindResource("StateComplete"),
        TransportStatus.Faulted => (Brush)FindResource("StateError"),
        _ => (Brush)FindResource("StateNotRun")
    };
}
