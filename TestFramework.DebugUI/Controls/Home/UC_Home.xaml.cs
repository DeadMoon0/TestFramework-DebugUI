using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Home;

/// <summary>
/// Every run the window knows about, live and recorded, arranged by project and test.
/// </summary>
/// <remarks>
/// The landing surface. The board answers "what did this run do"; this answers the question you have
/// before that one — "which run should I be looking at" — which the previous UI never answered at
/// all, and which a list down the side answers only for runs you can already name.
/// </remarks>
public partial class UC_Home : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>
    /// What the card grid is narrowed to.
    /// </summary>
    /// <remarks>
    /// Deliberately not in the store: it is where this page is scrolled to, not something the
    /// application knows about, and nothing else in the window has any use for it.
    /// </remarks>
    private readonly BehaviorSubject<RunScope> scope = new(RunScope.Everything);

    /// <summary>Creates the page and binds it.</summary>
    public UC_Home()
    {
        InitializeComponent();

        IObservable<ImmutableList<RunSummary>> runs = StateStore<MainState>.Default.Bind(state => state.Runs);

        subscriptions.Add(runs
            .CombineLatest(scope, (all, narrowed) => (IEnumerable<RunSummary>)[.. all.Where(narrowed.Covers)])
            .BindToCollection(wpRuns.Children, run => run.SessionId, run => Card(run.SessionId)));

        subscriptions.Add(runs.CombineLatest(scope, (all, narrowed) => (all, narrowed)).Subscribe(pair => ShowTree(pair.all, pair.narrowed)));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => Overview(state.Runs))
            .BindToDependencyProperty(tbOverview, TextBlock.TextProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader leaves the page, either by closing it or by opening a run.</summary>
    public event Action? Closed;

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

    /// <summary>
    /// Draws the project and test rail.
    /// </summary>
    /// <remarks>
    /// Rebuilt whole rather than reconciled: the tree is a handful of rows and changes only when a
    /// run appears or changes state, so the simple thing is fast enough and cannot drift.
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
                selected: narrowed.IsProject(projectName), bold: true, () => scope.OnNext(RunScope.OfProject(projectName))));

            foreach (TestGroup test in project.Tests)
            {
                string testName = test.Test;

                spTree.Children.Add(Row(test.DisplayName, test.Runs.Count, test.Health, indent: 14,
                    selected: narrowed.IsTest(testName), bold: false, () => scope.OnNext(RunScope.OfTest(testName)), testName));
            }
        }
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
            Background = (Brush)FindResource(BrushFor(health)),
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
            Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture),
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

    private static string BrushFor(RunHealth health) => health switch
    {
        RunHealth.Running => "StateRunning",
        RunHealth.Waiting => "StatePaused",
        RunHealth.Passed => "StateComplete",
        RunHealth.Unproven => "StateTimeout",
        RunHealth.Failed => "StateError",
        RunHealth.Aborted => "StateError",
        _ => "StateNotRun"
    };

    private UC_HomeCard Card(string sessionId)
    {
        UC_HomeCard card = new(sessionId);

        // Opening a run means going to look at it, so the page gets out of the way rather than
        // leaving the reader to close what they just navigated away from.
        card.Opened += () => Closed?.Invoke();

        return card;
    }

    private void btRefresh_Click(object sender, RoutedEventArgs e) => MainWindow.Shell.RefreshRecordedRuns();

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}

/// <summary>
/// What the card grid is narrowed to.
/// </summary>
/// <remarks>
/// A project or a test rather than an arbitrary predicate, because those are the only two things the
/// rail can select and a filter that could be anything cannot be shown as selected.
/// </remarks>
internal sealed record RunScope
{
    /// <summary>Everything the window knows about.</summary>
    internal static RunScope Everything { get; } = new();

    private string? Project { get; init; }

    private string? Test { get; init; }

    internal bool IsEverything => Project is null && Test is null;

    internal static RunScope OfProject(string project) => new() { Project = project };

    internal static RunScope OfTest(string test) => new() { Test = test };

    internal bool IsProject(string project) => string.Equals(Project, project, StringComparison.Ordinal);

    internal bool IsTest(string test) => string.Equals(Test, test, StringComparison.Ordinal);

    /// <summary>Whether a run belongs in this scope.</summary>
    internal bool Covers(RunSummary run)
        => (Project is null || string.Equals(run.Project, Project, StringComparison.Ordinal))
           && (Test is null || string.Equals(run.Test, Test, StringComparison.Ordinal));
}
