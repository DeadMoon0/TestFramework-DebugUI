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
using Axiom.State;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Search;

/// <summary>
/// One search over the run on screen.
/// </summary>
/// <remarks>
/// <para>
/// The query language lives in <see cref="SearchQuery"/> and the searching in <see cref="RunSearch"/>, both
/// pure and both tested without a window. This is the bar, the list and the keyboard.
/// </para>
/// <para>
/// Re-run on every keystroke rather than on a timer. <see cref="RunSearch"/> stops at its own cap, so the
/// work is bounded by the number of hits shown rather than by the size of the run — and a find bar that
/// answers a fifth of a second after you stop typing feels broken in a way a slightly slower one does not.
/// </para>
/// </remarks>
public partial class UC_Search : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    private ImmutableList<SearchHit> hits = ImmutableList<SearchHit>.Empty;
    private readonly List<Border> rows = [];
    private int highlighted = -1;

    /// <summary>Creates the bar and binds it.</summary>
    public UC_Search()
    {
        InitializeComponent();

        ShowHints();

        // Re-run when the run changes as well as when the query does: a live run gains steps and log lines
        // while the bar is open, and a result list that goes stale as the run proceeds is worse than none.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun)
            .Subscribe(_ => Run()));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveTiming)
            .Subscribe(_ => Run()));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveDiff)
            .Subscribe(_ => Run()));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader closes the bar.</summary>
    public event Action? Closed;

    /// <summary>Whether the bar is up.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// Opens the bar and puts the caret in it.
    /// </summary>
    /// <remarks>
    /// The previous query is kept and selected rather than cleared. Someone reopening the bar is usually
    /// looking for the same thing again, and a selected query is one keystroke from being replaced anyway.
    /// </remarks>
    public void Open()
    {
        Visibility = Visibility.Visible;

        tbQuery.Focus();
        tbQuery.SelectAll();

        Run();
    }

    /// <summary>Puts the bar away.</summary>
    public void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    /// <summary>Opens the bar, or puts it away when it is already up.</summary>
    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    private void Run()
    {
        if (!IsOpen)
            return;

        SearchQuery query = SearchQuery.Parse(tbQuery.Text);

        MainState state = StateStore<MainState>.Default.GetValue(current => current);
        SearchResults results = RunSearch.Find(query, state.ActiveRun, state.ActiveDiff, state.ActiveTiming);

        hits = results.Hits;
        highlighted = hits.Count == 0 ? -1 : 0;

        ShowCount(query, results);
        ShowProblem(results);
        ShowHits();
    }

    private void ShowCount(SearchQuery query, SearchResults results)
    {
        if (query.IsEmpty)
        {
            tbCount.Text = string.Empty;
            return;
        }

        // Broken down by kind, because "31 results" over a run does not say whether the thing being looked for
        // was a step or a line somebody logged.
        List<string> parts = [];

        foreach (SearchScope scope in Enum.GetValues<SearchScope>())
        {
            int count = results.CountIn(scope);

            if (count > 0)
                parts.Add(Counted(scope, count));
        }

        tbCount.Text = parts.Count == 0
            ? "nothing"
            : string.Join(" · ", parts) + (results.Truncated ? $" (first {RunSearch.MaximumHits})" : string.Empty);
    }

    /// <summary>
    /// A count of one kind of hit, in words.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than composed from the enum's own name, which is plural and would read "1 steps".
    /// A log line is called that rather than "1 log", because a log is the whole of one and this is a line in it.
    /// </remarks>
    private static string Counted(SearchScope scope, int count) => (scope, count) switch
    {
        (SearchScope.Steps, 1) => "1 step",
        (SearchScope.Logs, 1) => "1 log line",
        (SearchScope.Values, 1) => "1 value",
        (SearchScope.Checks, 1) => "1 check",
        (SearchScope.Logs, _) => $"{count.ToString(CultureInfo.CurrentCulture)} log lines",
        _ => $"{count.ToString(CultureInfo.CurrentCulture)} {scope.ToString().ToLowerInvariant()}"
    };

    private void ShowProblem(SearchResults results)
    {
        bool written = !string.IsNullOrWhiteSpace(tbQuery.Text);

        spHints.Visibility = written ? Visibility.Collapsed : Visibility.Visible;

        tbProblem.Visibility = results.Problem is null ? Visibility.Collapsed : Visibility.Visible;
        tbProblem.Text = results.Problem ?? string.Empty;

        // "Nothing matches" only once there is a query and no complaint about it. Saying it while the reader is
        // halfway through typing a regular expression would be telling them their run is empty when it is
        // their expression that is unfinished.
        tbNothing.Visibility = written && results.Problem is null && hits.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowHits()
    {
        spHits.Children.Clear();
        rows.Clear();

        svHits.Visibility = hits.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        SearchScope? heading = null;

        foreach (SearchHit hit in hits)
        {
            if (heading != hit.Scope)
            {
                heading = hit.Scope;
                spHits.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("PanelHeading"),
                    Margin = new Thickness(2, 8, 0, 4),
                    Text = hit.Scope.ToString().ToUpperInvariant()
                });
            }

            spHits.Children.Add(Row(hit));
        }

        Paint();
    }

    private Border Row(SearchHit hit)
    {
        TextBlock title = new()
        {
            Text = hit.Title,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 12,

            // Trimmed rather than wrapped. A log line can be three hundred characters, and a result list where
            // one row is six lines tall is a list you cannot scan.
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock detail = new()
        {
            Text = hit.Detail,
            Foreground = (Brush)FindResource("TextSecondary"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Border row = new()
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            ToolTip = hit.Title,
            Child = new StackPanel { Children = { title, detail } }
        };

        int index = rows.Count;

        row.MouseLeftButtonUp += (_, _) =>
        {
            highlighted = index;
            Activate();
        };

        row.MouseEnter += (_, _) =>
        {
            highlighted = index;
            Paint();
        };

        rows.Add(row);
        return row;
    }

    private void Paint()
    {
        for (int index = 0; index < rows.Count; index++)
        {
            rows[index].Background = index == highlighted
                ? (Brush)FindResource("SurfaceRaisedHover")
                : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Goes to whatever is highlighted.
    /// </summary>
    /// <remarks>
    /// The bar stays open. A reader working through six results wants to look at each one and come back, and
    /// closing on the first would make them retype the query five times.
    /// </remarks>
    private void Activate()
    {
        if (highlighted < 0 || highlighted >= hits.Count)
            return;

        SearchHit hit = hits[highlighted];

        Paint();

        if (hit.StageName is { } stage && hit.StepId is { } stepId)
        {
            MainWindow.Shell.SelectStep(stage, stepId);
            return;
        }

        if (hit.ValueKey is { } key)
            Opened?.Invoke(key, hit.IsArtifact);
    }

    /// <summary>Raised when the reader picks a value, so the window can open the inspector on it.</summary>
    public event Action<string, bool>? Opened;

    private void Move(int by)
    {
        if (hits.Count == 0)
            return;

        highlighted = Math.Clamp(highlighted + by, 0, hits.Count - 1);

        Paint();

        if (highlighted >= 0 && highlighted < rows.Count)
            rows[highlighted].BringIntoView();
    }

    /// <summary>
    /// Lists what can be written, once, off the query language's own definition.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="SearchQuery.Hints"/> rather than written here, so a filter added to the parser
    /// cannot be a filter nobody is told about.
    /// </remarks>
    private void ShowHints()
    {
        foreach (string hint in SearchQuery.Hints)
        {
            spHints.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("MutedText"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextFaint"),
                Margin = new Thickness(0, 0, 0, 3),
                Text = hint
            });
        }
    }

    private void tbQuery_TextChanged(object sender, TextChangedEventArgs e) => Run();

    private void tbQuery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handled here rather than left to the window's shortcut table: while the caret is in this box these
        // keys mean "move through the results", and the board must not also act on them.
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Activate();
                e.Handled = true;
                break;

            // Handled here because the window's shortcut table deliberately leaves editable text boxes alone,
            // so Escape would not otherwise reach it from inside this one.
            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Close();
}
