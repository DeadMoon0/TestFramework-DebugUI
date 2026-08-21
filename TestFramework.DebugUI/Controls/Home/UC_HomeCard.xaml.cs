using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.Controls.Home;

/// <summary>
/// One run on the home page.
/// </summary>
/// <remarks>
/// Everything shown here comes from <see cref="RunProgress"/>, which is kept for every session
/// rather than only the selected one — a card must not have to open a run to describe it.
/// </remarks>
public partial class UC_HomeCard : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    private bool hovered;

    /// <summary>Creates a card for one session.</summary>
    public UC_HomeCard(string sessionId)
    {
        SessionId = sessionId;

        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectBySession(SessionId))
            .Subscribe(Show));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectIsSelected(SessionId))
            .Subscribe(selected => { IsSelected = selected; Paint(); }));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Gets the session this card shows.</summary>
    public string SessionId { get; }

    /// <summary>Raised when the reader opens this run.</summary>
    public event Action? Opened;

    private bool IsSelected { get; set; }

    private void Show(RunSummary? run)
    {
        if (run is null)
            return;

        tbName.Text = run.Name;
        tbWhere.Text = Where(run);
        tbWhere.ToolTip = run.FullyQualifiedName;
        tbCounts.Text = Counts(run.Progress);
        tbWhen.Text = When(run);

        tbHealth.Text = HealthLook.Label(run.Health);
        bHealth.Background = (Brush)FindResource(HealthLook.BrushKey(run.Health));
        bEdge.Background = (Brush)FindResource(HealthLook.BrushKey(run.Health));

        DrawBar(run.Progress);
    }

    /// <summary>
    /// Where the run came from, in the part of the name that distinguishes it.
    /// </summary>
    /// <remarks>
    /// The tail of the qualified name rather than the head. Every run in a suite shares its opening
    /// namespaces, so a line trimmed from the right shows each card the same identical prefix and
    /// tells the reader nothing. The whole name is on the tooltip for when it is wanted.
    /// </remarks>
    private static string Where(RunSummary run)
    {
        if (string.IsNullOrWhiteSpace(run.FullyQualifiedName))
            return "no test identity recorded";

        string[] parts = run.FullyQualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries);

        // The last segment is the test method, which is already the card's title.
        if (parts.Length > 1 && string.Equals(parts[^1], run.Name, StringComparison.Ordinal))
            parts = parts[..^1];

        return parts.Length <= 2 ? string.Join('.', parts) : $"…{string.Join('.', parts[^2..])}";
    }

    /// <summary>
    /// The one line of numbers under the name.
    /// </summary>
    /// <remarks>
    /// A run listed from disk that nobody has opened says so rather than showing zeros, which would
    /// read as a run in which nothing failed. It is a fallback rather than a common case now: cards are
    /// only made for runs that need attention, and a run nobody has opened is never one of those.
    /// </remarks>
    private static string Counts(RunProgress? progress)
    {
        if (progress is null || !progress.IsKnown)
            return "not opened yet";

        List<string> parts =
        [
            progress.Steps > 0
                ? $"{progress.Complete + progress.Failed + progress.Skipped} of {progress.Steps} steps"
                : $"{progress.Complete} step(s)"
        ];

        if (progress.Failed > 0)
            parts.Add($"{progress.Failed} failed");

        if (progress.Skipped > 0)
            parts.Add($"{progress.Skipped} skipped");

        parts.Add(progress.HasAssertions
            ? progress.AssertionsFailed > 0
                ? $"{progress.AssertionsFailed} check(s) broke"
                : $"{progress.AssertionsPassed} check(s) held"
            : "nothing asserted");

        return string.Join(" · ", parts);
    }

    private static string When(RunSummary run)
    {
        string when = run.StartedAtUtc == default
            ? "unknown time"
            : run.StartedAtUtc.ToLocalTime().ToString("ddd HH:mm:ss", CultureInfo.CurrentCulture);

        return run.Duration is { } took ? $"{when} · took {Took(took)}" : when;
    }

    private static string Took(TimeSpan took)
        => took < TimeSpan.FromSeconds(1) ? $"{took.TotalMilliseconds:F0} ms"
            : took < TimeSpan.FromMinutes(1) ? $"{took.TotalSeconds:F1} s"
            : $"{(int)took.TotalMinutes}m {took.Seconds}s";

    /// <summary>
    /// Draws the run in proportion: what passed, what failed, and what has not happened yet.
    /// </summary>
    private void DrawBar(RunProgress? progress)
    {
        int complete = progress?.Complete ?? 0;
        int failed = progress?.Failed ?? 0;
        int skipped = progress?.Skipped ?? 0;
        int outstanding = progress?.Outstanding ?? 0;

        // A run with nothing to show gets an empty groove rather than a bar of one colour, which
        // would claim a state it has not reached.
        if (complete + failed + skipped + outstanding == 0)
            outstanding = 1;

        cdComplete.Width = new GridLength(complete, GridUnitType.Star);
        cdFailed.Width = new GridLength(failed, GridUnitType.Star);
        cdSkipped.Width = new GridLength(skipped, GridUnitType.Star);
        cdOutstanding.Width = new GridLength(outstanding, GridUnitType.Star);
    }

    private void Paint()
        => bRoot.Background = (Brush)FindResource(IsSelected || hovered ? "SurfaceRaisedHover" : "SurfaceRaised");

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
