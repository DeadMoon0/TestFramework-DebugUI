using System;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;


namespace TestFramework.DebugUI.Controls.Runs;

/// <summary>
/// One run in the list.
/// </summary>
public partial class UC_RunItem : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates a row for one session.</summary>
    public UC_RunItem(string sessionId)
    {
        SessionId = sessionId;

        InitializeComponent();

        subscriptions.Add(Summary().Select(run => run is null ? string.Empty : Describe(run))
            .BindToDependencyProperty(tbName, TextBlock.TextProperty));

        subscriptions.Add(Summary().Select(run => run is null ? string.Empty : Subtitle(run))
            .BindToDependencyProperty(tbWhen, TextBlock.TextProperty));

        subscriptions.Add(Summary().Select(run => run is not null && run.IsWaitingAtBreakpoint ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(bPaused, VisibilityProperty));

        subscriptions.Add(Summary().Select(StateBrush)
            .BindToDependencyProperty(bState, Border.BackgroundProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => string.Equals(state.SelectedSessionId, SessionId, StringComparison.Ordinal))
            .Select(selected => selected ? (Brush)FindResource("SurfaceRaisedHover") : (Brush)FindResource("SurfaceRaised"))
            .BindToDependencyProperty(bRoot, Border.BackgroundProperty));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Gets the session this row shows.</summary>
    public string SessionId { get; }

    private IObservable<RunSummary?> Summary()
        => StateStore<MainState>.Default.Bind(state =>
            state.Runs.FirstOrDefault(run => string.Equals(run.SessionId, SessionId, StringComparison.Ordinal)));

    private static string Describe(RunSummary run) => run.Name;

    /// <summary>
    /// The line under the name: when it ran, and what became of it.
    /// </summary>
    /// <remarks>
    /// A recorded run that never finished is called out as such. The previous UI discarded those
    /// entirely, which hid precisely the runs worth opening — the ones whose host died.
    /// </remarks>
    private static string Subtitle(RunSummary run)
    {
        string when = run.StartedAtUtc == default
            ? "unknown time"
            : run.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

        string what = run switch
        {
            { IsLive: true } => "live",
            { IsFinished: true } => "finished",
            _ => "did not finish"
        };

        return $"{when} · {what}";
    }

    /// <summary>
    /// The dot beside the name, coloured by how the run stands.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="RunSummary.Health"/> rather than from "did it finish", so a run that
    /// finished with a broken check is not shown in the same green as one that passed — which is
    /// what this list did until the counts existed to tell them apart.
    /// </remarks>
    private Brush StateBrush(RunSummary? run) => (Brush)FindResource(run?.Health switch
    {
        RunHealth.Waiting => "StatePaused",
        RunHealth.Running => "StateRunning",
        RunHealth.Passed => "StateComplete",
        RunHealth.Unproven => "StateTimeout",
        RunHealth.Failed => "StateError",
        RunHealth.Aborted => "StateError",
        _ => "StateNotRun"
    });

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => MainWindow.Shell.SelectRun(SessionId);
}
