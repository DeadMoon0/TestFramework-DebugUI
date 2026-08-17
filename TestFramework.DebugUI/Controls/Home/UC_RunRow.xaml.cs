using System;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Home;

/// <summary>
/// One run as a single line in the list.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="UC_HomeCard"/>: a card for the handful of runs worth looking at, a row
/// for the rest. Both open a run and both read their verdict from the same table, but a row is a
/// twenty-sixth of the height, which is what lets fifty of them be a list rather than a wall.
/// </remarks>
public partial class UC_RunRow : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    private bool hovered;

    /// <summary>Creates a row for one session.</summary>
    public UC_RunRow(string sessionId)
    {
        SessionId = sessionId;

        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs.FirstOrDefault(run => string.Equals(run.SessionId, SessionId, StringComparison.Ordinal)))
            .Subscribe(Show));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => string.Equals(state.SelectedSessionId, SessionId, StringComparison.Ordinal))
            .Subscribe(selected => { IsSelected = selected; Paint(); }));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Gets the session this row shows.</summary>
    public string SessionId { get; }

    /// <summary>Raised when the reader opens this run.</summary>
    public event Action? Opened;

    private bool IsSelected { get; set; }

    private void Show(RunSummary? run)
    {
        if (run is null)
            return;

        tbName.Text = run.ShortName;
        tbWhere.Text = RunTree.ClassOf(run.Test) is var type && type == RunTree.NoIdentity ? "no identity" : Leaf(type);
        tbWhen.Text = run.StartedAtUtc == default
            ? "—"
            : run.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        tbTook.Text = run.Duration is { } took ? Took(took) : "—";
        tbSteps.Text = Steps(run.Progress);
        tbChecks.Text = Checks(run.Progress);

        ToolTip = run.Test;

        Brush ink = (Brush)FindResource(HealthLook.BrushKey(run.Health));

        bEdge.Background = ink;

        // A run nobody has replayed has nothing to report, so it is dimmed as a whole rather than filled
        // with dashes that look like measurements of zero.
        bool known = run.Progress?.IsKnown == true;

        tbName.Foreground = (Brush)FindResource(known ? "TextPrimary" : "TextSecondary");
        tbSteps.Foreground = (Brush)FindResource(known ? "TextSecondary" : "TextFaint");
        tbChecks.Foreground = run.Progress?.AssertionsFailed > 0 ? ink : (Brush)FindResource(known ? "TextSecondary" : "TextFaint");
    }

    private static string Leaf(string name)
    {
        int lastDot = name.LastIndexOf('.');

        return lastDot >= 0 && lastDot < name.Length - 1 ? name[(lastDot + 1)..] : name;
    }

    /// <summary>
    /// How far the run got, as a fraction rather than a sentence.
    /// </summary>
    /// <remarks>
    /// An em dash for a run that has never been opened. The card this replaces spent a whole line on
    /// "not opened yet — pick it to replay the recording", which was true of fifty rows at once and so
    /// told the reader nothing about any of them. The heading above the list says it once.
    /// </remarks>
    private static string Steps(RunProgress? progress)
    {
        if (progress is null || !progress.IsKnown)
            return "—";

        int done = progress.Complete + progress.Failed + progress.Skipped;

        return progress.Steps > 0 ? $"{done}/{progress.Steps} steps" : $"{progress.Complete} steps";
    }

    private static string Checks(RunProgress? progress)
    {
        if (progress is null || !progress.IsKnown)
            return "—";

        if (!progress.HasAssertions)
            return "no checks";

        return progress.AssertionsFailed > 0
            ? $"{progress.AssertionsFailed} broke"
            : $"{progress.AssertionsPassed} held";
    }

    private static string Took(TimeSpan took)
        => took < TimeSpan.FromSeconds(1) ? $"{took.TotalMilliseconds:F0} ms"
            : took < TimeSpan.FromMinutes(1) ? $"{took.TotalSeconds:F1} s"
            : $"{(int)took.TotalMinutes}m {took.Seconds}s";

    private void Paint()
        => bRoot.Background = IsSelected || hovered
            ? (Brush)FindResource("SurfaceRaisedHover")
            : Brushes.Transparent;

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        hovered = true;
        Paint();
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        hovered = false;
        Paint();
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        MainWindow.Shell.SelectRun(SessionId);
        Opened?.Invoke();
    }
}
