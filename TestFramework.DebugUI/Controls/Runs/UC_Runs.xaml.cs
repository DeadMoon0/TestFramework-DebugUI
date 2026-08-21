using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Axiom.State;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Controls.Home;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.Controls.Runs;

/// <summary>
/// Which run to look at, as a tree of project, class, test and the executions of it.
/// </summary>
/// <remarks>
/// <para>
/// This answers one question — which run — and it is worth stating the rule, because every future
/// temptation will be to add a column. A flat list of runs answers "what happened recently"; it does not
/// answer "how is this test doing", which is what anyone re-running a failure actually has in mind, and five
/// runs of one test read as five unrelated rows. The grouping is the answer and it comes from
/// <see cref="RunTree"/>, so this rail and the runs page cannot group the same runs differently.
/// </para>
/// <para>
/// Anything aggregate belongs on the runs page instead: counts across runs, the six-column table, what is
/// flaky, searching every recording. Those want the whole width and are read occasionally; this wants three
/// hundred pixels and is read constantly. One badge per node is the whole of what this shows about health.
/// </para>
/// </remarks>
public partial class UC_Runs : UserControl, IDisposable, IPanelActions
{
    /// <summary>How far each level is stepped in.</summary>
    /// <remarks>
    /// Deliberately tight. Four levels at a comfortable indent would spend a third of the rail's width on
    /// whitespace, and it is the deepest rows — the runs themselves — that would pay for it.
    /// </remarks>
    private const double Step = 11;

    private readonly CompositeDisposable subscriptions = [];

    /// <summary>
    /// Which branches are open, by node key.
    /// </summary>
    /// <remarks>
    /// Held here rather than in the store: it is how one person has arranged one rail, it changes on every
    /// click, and putting it in the state tree would clone the whole set on each one. Rebuilt rows read it,
    /// so a run arriving mid-session does not collapse what the reader had opened.
    /// </remarks>
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);

    private ImmutableList<RunSummary> runs = ImmutableList<RunSummary>.Empty;
    private string? selected;

    /// <summary>Creates the tree and binds it.</summary>
    public UC_Runs()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectAll)
            .Subscribe(all =>
            {
                runs = all;
                Rebuild();
            }));

        // Rebuilt on selection too, so the highlight moves and the branch holding the selected run is
        // opened. A live run selects itself the moment it announces, and a rail that left it collapsed
        // would be hiding the one run the reader is about to be shown.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectSelectedSessionId)
            .Subscribe(sessionId =>
            {
                selected = sessionId;
                Reveal(sessionId);
                Rebuild();
            }));

    }

    /// <summary>Raised when the reader asks for the whole journal rather than one run.</summary>
    public event Action? OverviewRequested;

    /// <inheritdoc />
    public FrameworkElement Actions => spActions;

    /// <inheritdoc />
    public void ReclaimActions()
    {
        PanelActions.Detach(spActions);
        bActionSlot.Child = spActions;
    }

    /// <summary>Puts the caret in the filter.</summary>
    public void FocusFilter()
    {
        tbFilter.Focus();
        tbFilter.SelectAll();
    }

    /// <summary>
    /// Opens every branch above a run, so selecting it makes it visible.
    /// </summary>
    private void Reveal(string? sessionId)
    {
        if (sessionId is null)
            return;

        RunSummary? run = runs.Find(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal));

        if (run is null)
            return;

        expanded.Add(ProjectKey(run.Project));
        expanded.Add(ClassKey(RunTree.ClassOf(run.Test)));
        expanded.Add(TestKey(run.Test));
    }

    /// <summary>
    /// Draws the whole tree.
    /// </summary>
    /// <remarks>
    /// Rebuilt whole rather than reconciled, which is the same call the runs page makes for the same reason:
    /// it is a few dozen rows, it changes only when a run appears or changes state, and a rebuilt tree cannot
    /// drift from the runs it was built from.
    /// </remarks>
    private void Rebuild()
    {
        spTree.Children.Clear();

        ImmutableList<RunSummary> shown = Filtered(out bool filtering);

        tbFilterHint.Visibility = tbFilter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        btClearFilter.Visibility = tbFilter.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        tbCount.Text = filtering
            ? $"{shown.Count.ToString(CultureInfo.CurrentCulture)} of {runs.Count.ToString(CultureInfo.CurrentCulture)}"
            : string.Empty;

        tbEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        tbEmpty.Text = runs.Count == 0
            ? "No runs yet. Start a test and it will appear here, whether or not this window was open when it ran."
            : "No run matches that name.";

        if (shown.Count == 0)
            return;

        // The root, which is also the way to the runs page: the question "how is everything doing" is asked
        // of the whole journal, and this is the row that means the whole journal.
        spTree.Children.Add(GroupRow(
            "All runs",
            shown.Count,
            RunTree.WorstOf(shown),
            indent: 0,
            bold: true,
            chevron: null,
            tip: "Open the runs page",
            act: () => OverviewRequested?.Invoke()));

        ImmutableList<ProjectGroup> projects = RunTree.Of(shown);

        // A single project is a level that never varies, so it is not drawn — it would cost every row beneath
        // it an indent to say something the reader already knows. Two or more and it earns its row back.
        bool showProjects = projects.Count > 1;

        foreach (ProjectGroup project in projects)
        {
            double classIndent = showProjects ? Step : 0;

            if (showProjects)
            {
                string projectKey = ProjectKey(project.Project);

                spTree.Children.Add(GroupRow(
                    project.Project,
                    project.Runs.Count,
                    project.Health,
                    indent: 0,
                    bold: true,
                    chevron: expanded.Contains(projectKey),
                    tip: project.Project,
                    act: () => Toggle(projectKey)));

                if (!expanded.Contains(projectKey))
                    continue;
            }

            foreach (ClassGroup type in project.Classes)
            {
                string classKey = ClassKey(type.Class);

                spTree.Children.Add(GroupRow(
                    type.DisplayName,
                    type.Runs.Count,
                    type.Health,
                    indent: classIndent,
                    bold: false,
                    chevron: expanded.Contains(classKey),
                    tip: type.Class,
                    act: () => Toggle(classKey)));

                if (!expanded.Contains(classKey))
                    continue;

                foreach (TestGroup test in type.Tests)
                {
                    string testKey = TestKey(test.Test);

                    // A test with one run has nothing to expand into: its own row would be followed by a
                    // single child saying the same thing. Clicking it opens that run directly.
                    bool single = test.Runs.Count == 1;

                    spTree.Children.Add(GroupRow(
                        test.DisplayName,
                        test.Runs.Count,
                        test.Health,
                        indent: classIndent + Step,
                        bold: false,
                        chevron: single ? null : expanded.Contains(testKey),
                        tip: test.Test,
                        act: single
                            ? () => MainWindow.Shell.SelectRun(test.Runs[0].SessionId)
                            : () => Toggle(testKey),
                        selected: single && IsSelected(test.Runs[0])));

                    if (single || !expanded.Contains(testKey))
                        continue;

                    foreach (RunSummary run in test.Runs)
                        spTree.Children.Add(RunRow(run, classIndent + (Step * 2)));
                }
            }
        }
    }

    /// <summary>
    /// The runs the filter leaves, or all of them when there is no filter.
    /// </summary>
    /// <remarks>
    /// A filter opens everything that survived it. Narrowing to three runs and then having to expand three
    /// branches by hand to see them would make the box worse than useless.
    /// </remarks>
    private ImmutableList<RunSummary> Filtered(out bool filtering)
    {
        filtering = false;

        if (!SearchPattern.TryParse(tbFilter.Text, out SearchPattern? pattern, out _) || pattern is null)
            return runs;

        filtering = true;

        ImmutableList<RunSummary> matched = [.. runs.Where(run => RunTree.Matches(run, pattern))];

        foreach (RunSummary run in matched)
        {
            expanded.Add(ProjectKey(run.Project));
            expanded.Add(ClassKey(RunTree.ClassOf(run.Test)));
            expanded.Add(TestKey(run.Test));
        }

        return matched;
    }

    private void Toggle(string key)
    {
        if (!expanded.Remove(key))
            expanded.Add(key);

        Rebuild();
    }

    private bool IsSelected(RunSummary run)
        => string.Equals(run.SessionId, selected, StringComparison.Ordinal);

    /// <summary>
    /// A branch: a health edge, a name, and how many runs are under it.
    /// </summary>
    /// <remarks>
    /// The same shape the runs page draws its rail in, deliberately — the health edge means one thing across
    /// the whole tool, which is the reason <see cref="HealthLook"/> exists at all.
    /// </remarks>
    private UIElement GroupRow(
        string text,
        int count,
        RunHealth health,
        double indent,
        bool bold,
        bool? chevron,
        string? tip,
        Action act,
        bool selected = false)
    {
        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Reserved even where there is nothing to draw, so the names of siblings line up whether or not each
        // of them happens to have children.
        FrameworkElement twist = chevron is bool open ? Chevron(open) : new Border { Width = 12 };
        Grid.SetColumn(twist, 0);

        Border edge = new()
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)FindResource(HealthLook.BrushKey(health)),
            Margin = new Thickness(2, 0, 7, 0)
        };

        Grid.SetColumn(edge, 1);

        TextBlock name = new()
        {
            Text = text,
            Foreground = (Brush)FindResource(selected ? "TextPrimary" : "TextSecondary"),
            FontSize = 12,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Grid.SetColumn(name, 2);

        TextBlock tally = new()
        {
            Text = count.ToString(CultureInfo.CurrentCulture),
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Grid.SetColumn(tally, 3);

        content.Children.Add(twist);
        content.Children.Add(edge);
        content.Children.Add(name);
        content.Children.Add(tally);

        return Clickable(content, indent, selected, tip, act);
    }

    /// <summary>
    /// One execution: when it ran and what became of it.
    /// </summary>
    /// <remarks>
    /// The time rather than a count, because within one test the runs differ by when they happened and by
    /// nothing else a name could carry. The word beside it is the verdict, not "did it finish" — a run that
    /// finished with a broken check must not read the same as one that passed.
    /// </remarks>
    private UIElement RunRow(RunSummary run, double indent)
    {
        bool isSelected = IsSelected(run);

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border edge = new()
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)FindResource(HealthLook.BrushKey(run.Health)),
            Margin = new Thickness(14, 0, 7, 0)
        };

        TextBlock when = new()
        {
            Text = run.StartedAtUtc == default
                ? "unknown time"
                : run.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            Foreground = (Brush)FindResource(isSelected ? "TextPrimary" : "TextSecondary"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(when, 1);

        TextBlock what = new()
        {
            Text = Verdict(run),
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Grid.SetColumn(what, 2);

        content.Children.Add(edge);
        content.Children.Add(when);
        content.Children.Add(what);

        return Clickable(content, indent, isSelected, run.Test, () => MainWindow.Shell.SelectRun(run.SessionId));
    }

    /// <summary>What became of a run, in the one word the rail has room for.</summary>
    private static string Verdict(RunSummary run) => run.Health switch
    {
        RunHealth.Waiting => "paused",
        RunHealth.Running => "running",
        RunHealth.Failed => "failed",
        RunHealth.Aborted => "did not finish",
        RunHealth.Unproven => "proved nothing",
        RunHealth.Passed => "passed",
        _ => "not opened"
    };

    private Border Clickable(UIElement content, double indent, bool selected, string? tip, Action act)
    {
        Border row = new()
        {
            CornerRadius = new CornerRadius(4),
            Background = selected ? (Brush)FindResource("SurfaceRaised") : Brushes.Transparent,
            Padding = new Thickness(4, 5, 8, 5),
            Margin = new Thickness(indent, 0, 0, 1),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content
        };

        row.MouseLeftButtonUp += (_, _) => act();
        row.MouseEnter += (_, _) => { if (!selected) row.Background = (Brush)FindResource("SurfaceRaised"); };
        row.MouseLeave += (_, _) => { if (!selected) row.Background = Brushes.Transparent; };

        return row;
    }

    private Path Chevron(bool open) => new()
    {
        Data = (Geometry)FindResource("IconChevron"),
        Stroke = (Brush)FindResource("TextFaint"),
        StrokeThickness = 1.4,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = 12,
        Height = 18,
        Stretch = Stretch.None,
        VerticalAlignment = VerticalAlignment.Center,

        // One geometry for both states, turned rather than swapped, so the two can never be drawn at
        // different weights or sizes.
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(open ? 90 : 0)
    };

    private static string ProjectKey(string project) => "project:" + project;

    private static string ClassKey(string type) => "class:" + type;

    private static string TestKey(string test) => "test:" + test;

    private void tbFilter_TextChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void tbFilter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Escape clears rather than closing anything: this rail is not something the reader opened, so there
        // is nothing to put away, and a filter left on after they have stopped thinking about it is a rail
        // that appears to have lost most of their runs.
        if (e.Key != Key.Escape)
            return;

        if (tbFilter.Text.Length > 0)
            tbFilter.Text = string.Empty;

        e.Handled = true;
    }

    private void btClearFilter_Click(object sender, RoutedEventArgs e) => tbFilter.Text = string.Empty;

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
