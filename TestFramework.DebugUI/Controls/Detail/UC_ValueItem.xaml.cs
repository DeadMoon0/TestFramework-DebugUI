using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// One value — a variable or an artifact — shown as the facts it was described with.
/// </summary>
/// <remarks>
/// Variables and artifacts sit in one list because they are one thing to a reader: something the run
/// produced and a later step consumed. Splitting them into separate rails made the reader hold the
/// distinction, which only matters to the framework.
/// </remarks>
public partial class UC_ValueItem : UserControl
{
    private readonly CompositeDisposable subscriptions = [];
    private ValueBody? body;

    /// <summary>Creates a row for one value.</summary>
    /// <param name="key">The value's identifier.</param>
    /// <param name="isArtifact">Whether the value is an artifact rather than a variable.</param>
    public UC_ValueItem(string key, bool isArtifact)
    {
        Key = key;
        IsArtifact = isArtifact;

        InitializeComponent();

        bdEdge.Background = (Brush)FindResource(isArtifact ? "FlowArtifact" : "FlowVariable");

        subscriptions.Add(isArtifact
            ? StateStore<MainState>.Default
                .Bind(state => state.ActiveRun.Artifacts.TryGetValue(Key, out ArtifactNode? artifact) ? artifact : null)
                .Subscribe(ShowArtifact)
            : StateStore<MainState>.Default
                .Bind(state => state.ActiveRun.Variables.TryGetValue(Key, out ValueNode? value) ? value : null)
                .Subscribe(ShowVariable));

        // Bound separately from the value itself: the comparison arrives after the board does,
        // because it has to read an earlier run off disk.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => isArtifact ? state.ActiveDiff.ForArtifact(Key) : state.ActiveDiff.ForVariable(Key))
            .Subscribe(ShowChange));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Gets the value this row shows.</summary>
    public string Key { get; }

    /// <summary>Gets whether this row shows an artifact rather than a variable.</summary>
    public bool IsArtifact { get; }

    /// <summary>Raised when the reader asks to see this value in full.</summary>
    public event Action<string, bool>? Opened;

    private void bdRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Opened?.Invoke(Key, IsArtifact);

    private void bdRoot_MouseEnter(object sender, MouseEventArgs e)
        => bdRoot.Background = (Brush)FindResource("SurfaceRaisedHover");

    private void bdRoot_MouseLeave(object sender, MouseEventArgs e)
        => bdRoot.Background = (Brush)FindResource("SurfaceRaised");

    /// <summary>
    /// Marks how this value stands against the last run of the same test that passed.
    /// </summary>
    /// <remarks>
    /// Unchanged values carry no mark at all. A rail where every row is badged is a rail where the
    /// badges stop being read, and "the same as last time" is the answer a reader can afford to skim.
    /// </remarks>
    private void ShowChange(ValueChangeKind? change)
    {
        if (change is null or ValueChangeKind.Unchanged)
        {
            bdChange.Visibility = Visibility.Collapsed;
            return;
        }

        (string glyph, string brush, string meaning) = change switch
        {
            ValueChangeKind.Changed => ("~", "StateTimeout", "changed since the last passing run"),
            ValueChangeKind.Added => ("+", "StateComplete", "this run produced it; the last passing run did not"),
            ValueChangeKind.Removed => ("-", "StateError", "the last passing run produced it; this run did not"),
            _ => ("?", "StatePaused", "cannot be compared with the last passing run")
        };

        Brush colour = (Brush)FindResource(brush);

        tbChange.Text = glyph;
        tbChange.Foreground = colour;
        bdChange.BorderBrush = colour;
        bdChange.BorderThickness = new Thickness(1);
        bdChange.ToolTip = meaning;
        bdChange.Visibility = Visibility.Visible;
    }

    private void ShowVariable(ValueNode? value)
    {
        if (value is null)
            return;

        Show(value.Key, value.DisplayText, value.SchemaKey, value.TypeName, value.Description, []);
    }

    private void ShowArtifact(ArtifactNode? artifact)
    {
        if (artifact is null)
            return;

        Show(artifact.Key, artifact.DisplayText, artifact.SchemaKey, artifact.SchemaKey, artifact.Description, artifact.Versions);
    }

    private void Show(string key, string displayText, string schemaKey, string tooltip, ValueDescription described, ImmutableList<string> versions)
    {
        tbKey.Text = key;

        // The summary when there is one, and the old single line when replaying a journal recorded
        // before descriptions existed. Neither case should leave the row blank.
        tbSummary.Text = string.IsNullOrWhiteSpace(described.Summary) ? displayText : described.Summary;

        ToolTip = string.IsNullOrWhiteSpace(tooltip) ? displayText : tooltip;

        ValueIcon icon = ValueIcons.For(schemaKey);
        pIcon.Data = Geometry.Parse(icon.Glyph);
        pIcon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon.Colour));

        ShowFacts(described);
        ShowBadges(described);
        ShowVersions(versions);
        ShowBody(described);
    }

    private void ShowFacts(ValueDescription described)
    {
        // Joined rather than laid out in columns: at this width a column of names would cost more
        // room than the facts are worth. The inspector is where they get space.
        tbFacts.Text = string.Join("  ·  ", described.Facts.Select(fact => $"{fact.Name} {fact.Value}"));
        tbFacts.Visibility = described.Facts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowBadges(ValueDescription described)
    {
        spBadges.Children.Clear();

        foreach (string badge in described.Badges)
        {
            spBadges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceCard"),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = badge,
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 9
                }
            });
        }
    }

    private void ShowVersions(ImmutableList<string> versions)
    {
        wpVersions.Children.Clear();
        wpVersions.Visibility = versions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        for (int index = 0; index < versions.Count; index++)
        {
            if (index > 0)
            {
                wpVersions.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = (Brush)FindResource("TextFaint"),
                    FontSize = 9,
                    Margin = new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            wpVersions.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceCard"),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 0, 2),

                // The identifier is the tooltip rather than the label: version identifiers are long
                // and the useful thing at a glance is how many there were and which came last.
                ToolTip = versions[index],
                Child = new TextBlock
                {
                    Text = "v" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 9
                }
            });
        }
    }

    private void ShowBody(ValueDescription described)
    {
        body = described.Body;

        if (body is null)
        {
            tbBody.Visibility = Visibility.Collapsed;
            return;
        }

        tbBody.Visibility = Visibility.Visible;

        // The relative path, because that is the one that means something in a build's artifacts. The
        // full path is the tooltip, for opening it here.
        tbBody.Text = "⤓ " + ValueInspection.BodyLine(body);
        tbBody.ToolTip = body.Path;
    }

    /// <summary>Opens the file the value was written to.</summary>
    /// <remarks>
    /// The point of writing values into the run's own output rather than somewhere private is that
    /// they can be opened by whatever already understands them, so this hands the path to the shell
    /// instead of trying to render every form in here.
    /// </remarks>
    private void tbBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Handled here, or it bubbles to the row and opens the inspector as well. Clicking a path is
        // a request for the file, not for a panel about the file.
        if (e is not null)
            e.Handled = true;

        if (body is null || !File.Exists(body.Path))
            return;

        Process.Start(new ProcessStartInfo(body.Path) { UseShellExecute = true })?.Dispose();
    }
}
