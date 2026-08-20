using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State.Bundles;
using TestFramework.DebugUI.State;

using TestFramework.DebugUI.Controls.Dock;

namespace TestFramework.DebugUI.Controls.Home;

/// <summary>
/// Every run the window knows about, live and recorded, triaged and then listed.
/// </summary>
/// <remarks>
/// <para>
/// The landing surface. The board answers "what did this run do"; this answers the question you have
/// before that one — "which run should I be looking at" — which the previous UI never answered at
/// all, and which a list down the side answers only for runs you can already name.
/// </para>
/// <para>
/// It answers it in two parts. Runs that need attention are few and get a card each. The rest are the
/// bulk — passes and recordings nobody has replayed — and become one line apiece in a columned list,
/// gathered under the burst of activity they arrived in.
/// </para>
/// </remarks>
public partial class UC_Home : UserControl, IDisposable, IPanelActions
{
    /// <summary>
    /// How many cards the attention strip will show.
    /// </summary>
    /// <remarks>
    /// A cap, because the whole point is that this strip is short. A suite that fails forty tests would
    /// otherwise rebuild exactly the wall this replaced; past the cap the rest are counted, and they are
    /// all in the list underneath anyway.
    /// </remarks>
    private const int AttentionCards = 6;

    private readonly CompositeDisposable subscriptions = [];

    /// <summary>
    /// What the list is narrowed to.
    /// </summary>
    /// <remarks>
    /// Deliberately not in the store: it is where this page is scrolled to, not something the
    /// application knows about, and nothing else in the window has any use for it.
    /// </remarks>
    private readonly BehaviorSubject<RunScope> scope = new(RunScope.Everything);

    /// <summary>
    /// What the page was last built for.
    /// </summary>
    /// <remarks>
    /// The list is rebuilt whole rather than reconciled, so it must not rebuild on every event a live run
    /// produces — that would be several times a second, and it would take the reader's hover and scroll
    /// position with it each time. This is what the page's shape depends on: which runs there are, how
    /// each stands, and what the rail has narrowed to. A step completing changes none of those, and the
    /// rows update themselves from their own subscriptions.
    /// </remarks>
    private string shape = string.Empty;

    /// <summary>Creates the page and binds it.</summary>
    public UC_Home()
    {
        InitializeComponent();

        IObservable<ImmutableList<RunSummary>> runs = StateStore<MainState>.Default.Bind(state => state.Runs);

        subscriptions.Add(runs
            .CombineLatest(scope, (all, narrowed) => (all, narrowed))
            .Subscribe(pair => Refresh(pair.all, pair.narrowed)));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => Overview(state.Runs))
            .BindToDependencyProperty(tbOverview, TextBlock.TextProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

    }

    /// <inheritdoc />
    public FrameworkElement Actions => spActions;

    /// <inheritdoc />
    public void ReclaimActions()
    {
        PanelActions.Detach(spActions);
        bActionSlot.Child = spActions;
    }

    /// <summary>Raised when the reader leaves the page, either by closing it or by opening a run.</summary>
    public event Action? Closed;

    /// <summary>
    /// Raised when the reader asks to share what the page is currently showing.
    /// </summary>
    /// <remarks>
    /// Carries the journals rather than the runs, because a bundle is made of files and the page is the only
    /// thing that knows which runs the rail has narrowed to.
    /// </remarks>
    public event Action<ImmutableList<string>, string>? ShareRequested;

    /// <summary>
    /// The line under the heading: the whole set in one sentence.
    /// </summary>
    /// <remarks>
    /// Only states that are actually present are named, so the line says "7 runs · 2 failed" rather
    /// than reciting six zeroes to establish that nothing else happened.
    /// </remarks>
    private static string Overview(ImmutableList<RunSummary> runs)
    {
        if (runs.Count == 0)
            return "nothing recorded and nothing attached";

        List<string> parts = [$"{runs.Count} run(s)"];

        void Count(RunHealth health, string label)
        {
            int count = runs.Count(run => run.Health == health);
            if (count > 0)
                parts.Add($"{count} {label}");
        }

        Count(RunHealth.Running, "running");
        Count(RunHealth.Waiting, "paused");
        Count(RunHealth.Failed, "failed");
        Count(RunHealth.Aborted, "aborted");
        Count(RunHealth.Passed, "passed");
        Count(RunHealth.Unproven, "unproven");
        Count(RunHealth.Unknown, "not opened");

        return string.Join(" · ", parts);
    }

    private void Refresh(ImmutableList<RunSummary> all, RunScope narrowed)
    {
        string next = Shape(all, narrowed);

        if (string.Equals(next, shape, StringComparison.Ordinal))
            return;

        shape = next;

        ShowTree(all, narrowed);
        ShowRuns([.. all.Where(narrowed.Covers)]);
    }

    /// <summary>Everything the page's layout depends on, as one comparable string.</summary>
    private static string Shape(ImmutableList<RunSummary> all, RunScope narrowed)
    {
        StringBuilder builder = new(narrowed.Key);

        foreach (RunSummary run in all)
        {
            builder.Append('|').Append(run.SessionId).Append(':').Append((int)run.Health);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Draws the project, class and test rail.
    /// </summary>
    /// <remarks>
    /// Rebuilt whole rather than reconciled: the tree is a handful of rows and changes only when a run
    /// appears or changes state, so the simple thing is fast enough and cannot drift.
    /// </remarks>
    private void ShowTree(ImmutableList<RunSummary> runs, RunScope narrowed)
    {
        spTree.Children.Clear();

        spTree.Children.Add(Row("All runs", runs.Count, RunTree.WorstOf(runs), indent: 0,
            selected: narrowed.IsEverything, bold: true, () => scope.OnNext(RunScope.Everything)));

        foreach (ProjectGroup project in RunTree.Of(runs))
        {
            string projectName = project.Project;

            spTree.Children.Add(Row(projectName, project.Runs.Count, project.Health, indent: 0,
                selected: narrowed.IsProject(projectName), bold: true,
                () => scope.OnNext(RunScope.OfProject(projectName)), projectName));

            foreach (ClassGroup type in project.Classes)
            {
                string className = type.Class;

                spTree.Children.Add(Row(type.DisplayName, type.Runs.Count, type.Health, indent: 12,
                    selected: narrowed.IsClass(className), bold: false,
                    () => scope.OnNext(narrowed.IsClass(className) ? RunScope.OfProject(projectName) : RunScope.OfClass(className)),
                    className));

                // Only the class being looked at spells out its tests. All of them at once was twenty
                // rows of one repeated prefix, each truncated in the middle of the part that differed.
                if (!narrowed.InClass(className))
                    continue;

                foreach (TestGroup test in type.Tests)
                {
                    string testName = test.Test;

                    spTree.Children.Add(Row(test.DisplayName, test.Runs.Count, test.Health, indent: 26,
                        selected: narrowed.IsTest(testName), bold: false,
                        () => scope.OnNext(RunScope.OfTest(testName)), testName));
                }
            }
        }
    }

    /// <summary>
    /// Draws the attention strip and the batched list.
    /// </summary>
    private void ShowRuns(ImmutableList<RunSummary> runs)
    {
        ImmutableList<RunSummary> attention =
        [
            .. runs
                .Where(run => RunTree.NeedsAttention(run.Health))
                .OrderByDescending(run => run.StartedAtUtc)
        ];

        wpAttention.Children.Clear();

        foreach (RunSummary run in attention.Take(AttentionCards))
            wpAttention.Children.Add(Card(run.SessionId));

        spAttention.Visibility = attention.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        tbAttention.Text = Heading(attention);

        int hidden = attention.Count - AttentionCards;

        tbAttentionMore.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        tbAttentionMore.Text = hidden > 0 ? $"and {hidden} more in the list below" : string.Empty;

        spRuns.Children.Clear();
        gColumns.Visibility = runs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (RunBatch batch in RunBatches.Of(runs))
        {
            spRuns.Children.Add(BatchHeader(batch));

            foreach (RunSummary run in batch.Runs)
                spRuns.Children.Add(RowFor(run.SessionId));
        }
    }

    /// <summary>
    /// What the attention strip is called, which is what is in it.
    /// </summary>
    /// <remarks>
    /// Named by the states actually present. "NEEDS ATTENTION" over a single unproven run overstates it,
    /// and over two failures understates it.
    /// </remarks>
    private static string Heading(ImmutableList<RunSummary> attention)
    {
        List<string> parts = [];

        void Count(RunHealth health, string label)
        {
            int count = attention.Count(run => run.Health == health);
            if (count > 0)
                parts.Add($"{count} {label}");
        }

        Count(RunHealth.Waiting, "PAUSED");
        Count(RunHealth.Running, "RUNNING");
        Count(RunHealth.Failed, "FAILED");
        Count(RunHealth.Aborted, "ABORTED");
        Count(RunHealth.Unproven, "UNPROVEN");

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// The line above one burst of runs.
    /// </summary>
    /// <remarks>
    /// This is where "not opened yet" is said. It is true of most of the rows underneath, so it is stated
    /// once as a property of the batch instead of fifty times as a property of each run.
    /// </remarks>
    private UIElement BatchHeader(RunBatch batch)
    {
        List<string> parts = [$"{batch.Runs.Count} run(s)"];

        if (batch.NotOpened == batch.Runs.Count)
            parts.Add("none opened yet");
        else if (batch.NotOpened > 0)
            parts.Add($"{batch.NotOpened} not opened");

        TextBlock when = new()
        {
            Text = When(batch.LastStartedAtUtc),
            Foreground = (Brush)FindResource("TextSecondary"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock detail = new()
        {
            Text = string.Join(" · ", parts),
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 10,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Children = { when, detail }
        };

        return new Border
        {
            Child = content,
            Margin = new Thickness(0, 10, 0, 4),
            Padding = new Thickness(3, 0, 0, 4),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("SurfaceRaised")
        };
    }

    /// <summary>
    /// When a burst happened, in the terms someone would use out loud.
    /// </summary>
    /// <remarks>
    /// A date is only worth printing once it is not today's. Most of what this tool lists was produced in
    /// the last few minutes, and "Mo 17:20:58" spends its width establishing a day the reader is standing
    /// in.
    /// </remarks>
    private static string When(DateTimeOffset at)
    {
        if (at == default)
            return "Time not recorded";

        DateTime local = at.ToLocalTime().DateTime;
        DateTime today = DateTime.Today;

        string clock = local.ToString("HH:mm", CultureInfo.CurrentCulture);

        if (local.Date == today)
            return $"Today {clock}";

        if (local.Date == today.AddDays(-1))
            return $"Yesterday {clock}";

        return local.ToString("ddd d MMM HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// One row of the rail: a health edge, a name, and how many runs are under it.
    /// </summary>
    /// <remarks>
    /// The count is the reason the rail earns its width — it is what turns "a test" into "a test that
    /// has been run nine times today", which is the shape of a problem being worked on.
    /// </remarks>
    private UIElement Row(string text, int count, RunHealth health, double indent, bool selected, bool bold, Action open, string? tip = null)
    {
        Border edge = new()
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)FindResource(HealthLook.BrushKey(health)),
            Margin = new Thickness(0, 0, 8, 0)
        };

        TextBlock name = new()
        {
            Text = text,
            Foreground = (Brush)FindResource(selected ? "TextPrimary" : "TextSecondary"),
            FontSize = 12,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock tally = new()
        {
            Text = count.ToString(CultureInfo.CurrentCulture),
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(name, 1);
        Grid.SetColumn(tally, 2);
        content.Children.Add(edge);
        content.Children.Add(name);
        content.Children.Add(tally);

        Border row = new()
        {
            CornerRadius = new CornerRadius(4),
            Background = selected ? (Brush)FindResource("SurfaceRaised") : Brushes.Transparent,
            Padding = new Thickness(6, 5, 8, 5),
            Margin = new Thickness(indent, 0, 0, 2),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content
        };

        row.MouseLeftButtonUp += (_, _) => open();
        row.MouseEnter += (_, _) => { if (!selected) row.Background = (Brush)FindResource("SurfaceRaised"); };
        row.MouseLeave += (_, _) => { if (!selected) row.Background = Brushes.Transparent; };

        return row;
    }

    private UC_HomeCard Card(string sessionId)
    {
        UC_HomeCard card = new(sessionId);

        // Opening a run means going to look at it, so the page gets out of the way rather than
        // leaving the reader to close what they just navigated away from.
        card.Opened += () => Closed?.Invoke();

        return card;
    }

    private UC_RunRow RowFor(string sessionId)
    {
        UC_RunRow row = new(sessionId);

        row.Opened += () => Closed?.Invoke();

        return row;
    }

    /// <summary>
    /// Offers to share every run the rail has narrowed to.
    /// </summary>
    /// <remarks>
    /// Scope rather than selection: narrowing to a class and sharing it is the same gesture as reading it, and
    /// it means "send them this test's runs" needs no separate way to pick them. Live runs are left out — a run
    /// still being written has no journal to send.
    /// </remarks>
    private void btShare_Click(object sender, RoutedEventArgs e)
    {
        RunScope narrowed = scope.Value;

        ImmutableList<RunSummary> runs =
        [
            .. StateStore<MainState>.Default
                .GetValue(state => state.Runs)
                .Where(run => narrowed.Covers(run) && !string.IsNullOrWhiteSpace(run.JournalPath))
        ];

        if (runs.Count == 0)
        {
            MainWindow.Shell.Report(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Warning,
                Source = FeedSource.App,
                Title = "There is nothing here to share.",
                Detail = "A run has to have been recorded before it can be sent."
            });

            return;
        }

        ShareRequested?.Invoke(
            [.. runs.Select(run => run.JournalPath!)],
            runs.Count == 1 ? runs[0].ShortName : $"{runs.Count} runs");
    }

    /// <summary>
    /// Takes in a bundle someone else exported.
    /// </summary>
    /// <remarks>
    /// Only the asking happens here. What an import means - duplicates, files that never arrived, which run to
    /// select - is shared with the path a double-click takes, so the two cannot drift apart.
    /// </remarks>
    private void btImport_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = BundleFormat.DialogFilter,
            DefaultExt = BundleFormat.Extension,
            Multiselect = false,
            Title = "Open a shared run"
        };

        if (dialog.ShowDialog() != true)
            return;

        if (BundleImport.Open(dialog.FileName))
            Closed?.Invoke();
    }

    private void btRefresh_Click(object sender, RoutedEventArgs e) => MainWindow.Shell.RefreshRecordedRuns();


    /// <summary>
    /// Lets go of the store.
    /// </summary>
    /// <remarks>
    /// Called by the host when the window closes, not when the panel leaves the visual tree. Moving a panel to
    /// another dock takes it out of one parent and puts it in another, and WPF raises <c>Unloaded</c> in
    /// between — so disposing there would kill a panel the first time it was ever dragged. A closed panel
    /// keeping its subscriptions also means it reopens showing whatever the reader left in it.
    /// </remarks>
    public void Dispose()
    {
        subscriptions.Dispose();
        GC.SuppressFinalize(this);
    }

}

/// <summary>
/// What the list is narrowed to.
/// </summary>
/// <remarks>
/// A project, a class or a test rather than an arbitrary predicate, because those are the only things the
/// rail can select and a filter that could be anything cannot be shown as selected.
/// </remarks>
internal sealed record RunScope
{
    /// <summary>Everything the window knows about.</summary>
    internal static RunScope Everything { get; } = new();

    private string? Project { get; init; }

    private string? Class { get; init; }

    private string? Test { get; init; }

    internal bool IsEverything => Project is null && Class is null && Test is null;

    /// <summary>The scope as one comparable string, for deciding whether the page must be rebuilt.</summary>
    internal string Key => $"{Project}{Class}{Test}";

    internal static RunScope OfProject(string project) => new() { Project = project };

    internal static RunScope OfClass(string type) => new() { Class = type };

    internal static RunScope OfTest(string test) => new() { Test = test };

    internal bool IsProject(string project) => string.Equals(Project, project, StringComparison.Ordinal);

    internal bool IsClass(string type) => string.Equals(Class, type, StringComparison.Ordinal);

    internal bool IsTest(string test) => string.Equals(Test, test, StringComparison.Ordinal);

    /// <summary>
    /// Whether a class should be showing its tests.
    /// </summary>
    /// <remarks>
    /// True for the selected class, and also for the class of a selected test — narrowing to one test
    /// must not collapse the list it was picked from.
    /// </remarks>
    internal bool InClass(string type)
        => IsClass(type) || (Test is not null && string.Equals(RunTree.ClassOf(Test), type, StringComparison.Ordinal));

    /// <summary>Whether a run belongs in this scope.</summary>
    internal bool Covers(RunSummary run)
        => (Project is null || string.Equals(run.Project, Project, StringComparison.Ordinal))
           && (Class is null || string.Equals(RunTree.ClassOf(run.Test), Class, StringComparison.Ordinal))
           && (Test is null || string.Equals(run.Test, Test, StringComparison.Ordinal));
}
