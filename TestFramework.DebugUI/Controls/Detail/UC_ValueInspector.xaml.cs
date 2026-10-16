using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Transport;

using System.Windows.Media.Imaging;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// One value, in full: its facts laid out, and as much of its content as was sent.
/// </summary>
/// <remarks>
/// <para>
/// The rail has room for a line. This has room for the rest — which matters because the preview is
/// the only part of a large value that travels, and until now nothing in the UI could show it at all.
/// </para>
/// <para>
/// Bound to the key rather than handed a snapshot, so a value that changes while the inspector is
/// open changes on screen. A snapshot would quietly become a lie during a live run.
/// </para>
/// </remarks>
public partial class UC_ValueInspector : UserControl, IDisposable
{
    /// <summary>How wide a picture is decoded for the inspector's well.</summary>
    /// <remarks>
    /// The well is around 600 device-independent pixels and the tool runs per-monitor DPI aware, so
    /// this leaves room to be drawn on a high-density display without decoding a 4K screenshot whole.
    /// </remarks>
    private const int PreviewImageWidth = 1200;

    private CompositeDisposable subscriptions = [];

    /// <summary>The store the open value is followed through.</summary>
    private readonly StateStore<MainState> store;

    private ValueBody? body;

    /// <summary>What is on screen, so the diff can be rebuilt when the comparison arrives.</summary>
    private string? shownKey;
    private bool shownIsArtifact;

    /// <summary>
    /// Set when what is open is a widget rather than a value.
    /// </summary>
    /// <remarks>
    /// A third thing this panel can show, and the one the picture comparison exists for. It is not
    /// in the run's variables or artifacts, so it is followed through the graph rather than through
    /// the value selectors - by name, taking the newest of that name, which is what the panel showed
    /// when it was opened.
    /// </remarks>
    private string? shownWidget;
    private ValueChange? comparison;
    private bool showingDiff;

    /// <summary>Creates the inspector, closed and bound to nothing.</summary>
    /// <param name="shell">The store the open value is followed through, and what it may ask the shell to do.</param>
    public UC_ValueInspector(ShellContext shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        store = shell.Store;

        InitializeComponent();

        // The file the rest of the value is in, and nothing else. Its path is long, exact, and wanted
        // somewhere other than here; the panel's own title is not - it names the thing you are already
        // looking at - and the preview below is a read-only text box that selects the ordinary way.
        Copyable.Enable(tbBody);

    }

    /// <summary>Shows a value, following it for as long as the inspector stays open.</summary>
    /// <param name="key">The value's identifier.</param>
    /// <param name="isArtifact">Whether the value is an artifact rather than a variable.</param>
    public void Show(string key, bool isArtifact)
    {
        // Rebound rather than added to: opening a second value while the first is on screen must
        // replace it, not leave two subscriptions writing to the same controls.
        subscriptions.Dispose();
        subscriptions = [];

        shownKey = key;
        shownIsArtifact = isArtifact;
        shownWidget = null;

        // Opening a value shows the value. The diff is a deliberate second step: most of the time a
        // reader opening a value wants to read it, not to read what it used to be.
        showingDiff = false;
        btDiff.Content = "Diff";
        rtDiff.Visibility = Visibility.Collapsed;
        tbPreview.Visibility = Visibility.Visible;

        subscriptions.Add(isArtifact
            ? store
                .Bind(BoardSelectors.SelectArtifact(key))
                .Subscribe(ShowArtifact)
            : store
                .Bind(BoardSelectors.SelectVariable(key))
                .Subscribe(ShowVariable));

        subscriptions.Add(store
            .Bind(isArtifact
                ? ComparisonSelectors.SelectArtifactChange(key)
                : ComparisonSelectors.SelectVariableChange(key))
            .Subscribe(ShowComparison));

        Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows a widget, following the run for as long as the inspector stays open.
    /// </summary>
    /// <remarks>
    /// The same panel rather than one of its own, because everything it has to say about a widget it
    /// already says about a value: the facts, the content, the file it was written to, and how it
    /// stands against the last passing run. What differs is only that the content is a picture, and
    /// the well was already able to draw one.
    /// </remarks>
    /// <param name="widget">The widget to show.</param>
    public void ShowWidget(WidgetNode widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        subscriptions.Dispose();
        subscriptions = [];

        shownKey = widget.Name;
        shownIsArtifact = false;
        shownWidget = widget.Name;

        showingDiff = false;
        btDiff.Content = "Diff";
        rtDiff.Visibility = Visibility.Collapsed;
        spPictures.Visibility = Visibility.Collapsed;
        tbPreview.Visibility = Visibility.Visible;

        // Followed through the graph rather than through a value selector, because a widget is in
        // neither store. A live run can publish a newer one under the same name - a second look at a
        // page - and the panel showing the older one would be showing the past without saying so.
        subscriptions.Add(store
            .Bind(BoardSelectors.SelectActiveRun)
            .Subscribe(_ => ShowNewestWidget()));

        subscriptions.Add(store
            .Bind(ComparisonSelectors.SelectWidgetChange(widget.Name))
            .Subscribe(ShowComparison));

        Visibility = Visibility.Visible;
    }

    /// <summary>Draws the newest widget of the open name, which is the current state of that thing.</summary>
    private void ShowNewestWidget()
    {
        if (shownWidget is null || WidgetOf(store.GetValue(state => state.Board.ActiveRun)) is not { } widget)
            return;

        Render(widget.Name, widget.Kind, widget.Description, []);
    }

    /// <summary>The newest widget carrying the open name, or null when the run has none.</summary>
    private WidgetNode? WidgetOf(RunGraph graph)
    {
        WidgetNode? newest = null;

        foreach (WidgetNode candidate in graph.Widgets)
        {
            if (string.Equals(candidate.Name, shownWidget, StringComparison.Ordinal))
                newest = candidate;
        }

        return newest;
    }

    private void ShowVariable(ValueNode? value)
    {
        if (value is null)
            return;

        Render(value.Key, value.SchemaKey, value.Description, []);
    }

    private void ShowArtifact(ArtifactNode? artifact)
    {
        if (artifact is null)
            return;

        Render(artifact.Key, artifact.SchemaKey, artifact.Description, artifact.Versions);
    }

    private void Render(string key, string schemaKey, ValueDescription described, ImmutableList<string> versions)
    {
        tbKey.Text = key;
        tbSummary.Text = described.Summary;

        ValueIcon icon = ValueIcons.For(schemaKey);
        pIcon.Data = Geometry.Parse(icon.Glyph);
        pIcon.Stroke = (Brush)FindResource(icon.Colour);

        RenderFacts(described, schemaKey);
        RenderVersions(versions);
        RenderPreview(described);
        RenderBody(described);
    }

    /// <summary>The facts as rows, name beside value.</summary>
    private void RenderFacts(ValueDescription described, string schemaKey)
    {
        gFacts.Children.Clear();
        gFacts.RowDefinitions.Clear();

        foreach (ValueFact fact in ValueInspection.FactsOf(described, schemaKey))
            AddFact(fact.Name, fact.Text);
    }

    private void AddFact(string name, string value)
    {
        int row = gFacts.RowDefinitions.Count;
        gFacts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock label = new()
        {
            Text = name,
            Foreground = (Brush)FindResource(ThemeKeys.TextFaint),
            FontSize = 11,
            Margin = new Thickness(0, 1, 14, 1)
        };

        TextBlock body = new()
        {
            Text = value,
            Foreground = (Brush)FindResource(ThemeKeys.TextSecondary),
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = value
        };

        Grid.SetRow(label, row);
        Grid.SetRow(body, row);
        Grid.SetColumn(body, 1);

        gFacts.Children.Add(label);
        gFacts.Children.Add(body);
    }

    private void RenderVersions(ImmutableList<string> versions)
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
                    Foreground = (Brush)FindResource(ThemeKeys.TextFaint),
                    FontSize = 10,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            wpVersions.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource(ThemeKeys.SurfaceCard),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 0, 2),
                ToolTip = versions[index],
                Child = new TextBlock
                {
                    Text = "v" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = (Brush)FindResource(ThemeKeys.TextSecondary),
                    FontSize = 10
                }
            });
        }
    }

    /// <summary>The content itself, headed by an honest statement of how much of it arrived.</summary>
    private void RenderPreview(ValueDescription described)
    {
        // A live value updating while the diff is open must not replace it with the plain preview:
        // the reader asked a different question and is still reading the answer.
        if (showingDiff)
            return;

        tbPreviewLabel.Text = ValueInspection.PreviewHeading(described.Preview);

        // A picture is drawn; everything else is read. The two share the well, so exactly one of them
        // is up at a time — and the diff, which is the third, has already returned above.
        BitmapSource? picture = described.Preview?.Form == DebugPreviewForm.Image
            ? WidgetImages.Read(RunFiles.Resolve(described.Body, store), described.Body?.ContentHash, PreviewImageWidth)
            : null;

        imgPreview.Source = picture;
        imgPreview.Visibility = picture is null ? Visibility.Collapsed : Visibility.Visible;
        tbPreview.Visibility = picture is null ? Visibility.Visible : Visibility.Collapsed;

        // Said rather than shown blank: a picture whose file is gone — an import that lost it, a run
        // output someone cleaned up — is a different thing from a value with no content.
        tbPreview.Text = described.Preview?.Form == DebugPreviewForm.Image
            ? picture is null ? "The picture could not be read from " + (described.Body?.RelativePath ?? "its file") + "." : string.Empty
            : ValueInspection.PreviewText(described.Preview);
    }

    private void RenderBody(ValueDescription described)
    {
        body = described.Body;

        gBody.Visibility = body is null ? Visibility.Collapsed : Visibility.Visible;

        if (body is null)
            return;

        tbBody.Text = ValueInspection.BodyLine(body);
        tbBody.ToolTip = body.Path;
        btOpen.IsEnabled = BodyFile() is not null;
    }

    /// <summary>Opens the file the whole value was written to.</summary>
    /// <remarks>
    /// Handed to the shell rather than rendered here: the point of writing values into the run's own
    /// output is that whatever already understands the format can open them.
    /// </remarks>
    private void btOpen_Click(object sender, RoutedEventArgs e)
    {
        if (BodyFile() is not { } path)
            return;

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>The file this value was written to, wherever it is now.</summary>
    private string? BodyFile() => RunFiles.Resolve(body, store);

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
