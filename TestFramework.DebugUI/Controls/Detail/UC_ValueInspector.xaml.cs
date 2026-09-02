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
using System.Windows.Documents;
using System.Windows.Media;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Bundles;

using System.Windows.Media.Imaging;

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
    public UC_ValueInspector()
    {
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
            ? StateStore<MainState>.Default
                .Bind(BoardSelectors.SelectArtifact(key))
                .Subscribe(ShowArtifact)
            : StateStore<MainState>.Default
                .Bind(BoardSelectors.SelectVariable(key))
                .Subscribe(ShowVariable));

        subscriptions.Add(StateStore<MainState>.Default
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
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectActiveRun)
            .Subscribe(_ => ShowNewestWidget()));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(ComparisonSelectors.SelectWidgetChange(widget.Name))
            .Subscribe(ShowComparison));

        Visibility = Visibility.Visible;
    }

    /// <summary>Draws the newest widget of the open name, which is the current state of that thing.</summary>
    private void ShowNewestWidget()
    {
        if (shownWidget is null || WidgetOf(StateStore<MainState>.Default.GetValue(state => state.Board.ActiveRun)) is not { } widget)
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

    /// <summary>
    /// Takes the comparison for the open value, and offers a diff when there is one to show.
    /// </summary>
    /// <remarks>
    /// The button appears only for a value that actually differs. Offering it on an unchanged value
    /// would lead a reader to a panel that says "identical", which is a worse way to learn nothing
    /// changed than the absence of a badge already told them.
    /// </remarks>
    private void ShowComparison(ValueChange? change)
    {
        comparison = change;

        bool canDiff = change is { Change: ValueChangeKind.Changed or ValueChangeKind.Added or ValueChangeKind.Removed };

        btDiff.Visibility = canDiff ? Visibility.Visible : Visibility.Collapsed;

        if (!canDiff && showingDiff)
        {
            showingDiff = false;
            ShowValueInsteadOfDiff();
        }
        else if (showingDiff)
        {
            RenderDiff();
        }
    }

    private void btDiff_Click(object sender, RoutedEventArgs e)
    {
        showingDiff = !showingDiff;

        if (showingDiff)
            RenderDiff();
        else
            ShowValueInsteadOfDiff();
    }

    private void ShowValueInsteadOfDiff()
    {
        btDiff.Content = "Diff";
        rtDiff.Visibility = Visibility.Collapsed;
        spPictures.Visibility = Visibility.Collapsed;
        tbPreview.Visibility = Visibility.Visible;

        if (shownKey is not null)
            Reshow();
    }

    /// <summary>Re-renders the open value from current state, without rebinding.</summary>
    private void Reshow()
    {
        MainState state = StateStore<MainState>.Default.GetValue(current => current);

        if (shownWidget is not null)
        {
            ShowNewestWidget();
            return;
        }

        if (shownIsArtifact)
        {
            if (state.Board.ActiveRun.Artifacts.TryGetValue(shownKey!, out ArtifactNode? artifact))
                ShowArtifact(artifact);

            return;
        }

        if (state.Board.ActiveRun.Variables.TryGetValue(shownKey!, out ValueNode? value))
            ShowVariable(value);
    }

    /// <summary>
    /// Draws the unified diff between what the last passing run had and what this run has.
    /// </summary>
    /// <remarks>
    /// Diffed on the rendered previews rather than on the raw text, so the formatting the inspector
    /// already applies - indented JSON, a hex dump for bytes - is what gets compared. Diffing raw
    /// single-line JSON would report one enormous changed line and tell the reader nothing.
    /// </remarks>
    private void RenderDiff()
    {
        if (comparison is not { } change)
            return;

        btDiff.Content = "Value";
        tbPreview.Visibility = Visibility.Collapsed;
        imgPreview.Visibility = Visibility.Collapsed;
        spPictures.Visibility = Visibility.Collapsed;
        rtDiff.Visibility = Visibility.Collapsed;

        tbPreviewLabel.Text = change.Change switch
        {
            ValueChangeKind.Added => "NOT IN THE LAST PASSING RUN",
            ValueChangeKind.Removed => "GONE SINCE THE LAST PASSING RUN",
            _ => "CHANGED SINCE THE LAST PASSING RUN"
        };

        // Pictures are shown, not described. Taken before the text diff rather than after it because
        // a unified diff of two images is not a weaker answer, it is a wrong one: neither carries a
        // line of text, so it compares nothing against nothing and reports them identical.
        if (RenderPictureDiff(change))
            return;

        rtDiff.Visibility = Visibility.Visible;

        FlowDocument document = new()
        {
            PagePadding = new Thickness(0),
            LineHeight = 15,
            FontFamily = rtDiff.FontFamily,
            FontSize = rtDiff.FontSize,

            // Wide and fixed, so lines are never wrapped. Left to size itself inside the scroller the
            // document collapsed to a single character's width and printed the diff vertically, one
            // letter per line.
            PageWidth = 4000
        };

        foreach (DiffLine line in DiffOf(change))
        {
            if (line.Kind == DiffLineKind.Note)
                document.Blocks.AddRange(Note(line));
            else
                document.Blocks.Add(Line(line));
        }

        rtDiff.Document = document;
    }

    /// <summary>How wide each picture of a side-by-side comparison is decoded.</summary>
    /// <remarks>
    /// Half the single-picture width, because two of them share the well. Decoding both at full
    /// width to draw them at half would cost twice the memory to show the same thing.
    /// </remarks>
    private const int ComparedImageWidth = 600;

    /// <summary>
    /// Shows the two pictures side by side, when what changed is a picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No attempt is made to say <em>what</em> in the image differs. The run already knows they
    /// differ — the comparison decided that from the content hash, which is exact — so the only thing
    /// left is to let a reader see both, and the honest way to do that is to show both. A computed
    /// pixel overlay would be a guess dressed as an answer: two screenshots of the same page taken a
    /// second apart differ in a caret, a scrollbar and an animation frame, and highlighting all three
    /// as findings is worse than highlighting nothing.
    /// </para>
    /// <para>
    /// A side that has no picture says so rather than leaving a gap: for something added or removed
    /// the absence <em>is</em> the difference, and an empty half of a panel does not read as
    /// "the last passing run did not have this".
    /// </para>
    /// </remarks>
    /// <param name="change">The comparison to draw.</param>
    /// <returns>Whether this was a picture, and has therefore been drawn.</returns>
    private bool RenderPictureDiff(ValueChange change)
    {
        if (!IsPicture(change.Baseline) && !IsPicture(change.Current))
            return false;

        spPictures.Children.Clear();

        StackPanel pair = new() { Orientation = Orientation.Horizontal };

        pair.Children.Add(Side("LAST PASSING RUN", change.Baseline));
        pair.Children.Add(Side("THIS RUN", change.Current));

        spPictures.Children.Add(pair);

        // The verdict in words underneath, because two thumbnails can look alike at this size while
        // the files are plainly different - and the comparison knows which, by hash.
        spPictures.Children.Add(new TextBlock
        {
            Text = change.Change switch
            {
                ValueChangeKind.Changed => "The two differ: " + Difference(change) + ".",
                ValueChangeKind.Added => "This run produced it; the last passing run did not.",
                ValueChangeKind.Removed => "The last passing run produced it; this run did not.",
                ValueChangeKind.Indeterminate => change.Reason ?? "The two cannot be compared.",
                _ => "The two are the same."
            },
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        spPictures.Visibility = Visibility.Visible;

        return true;
    }

    /// <summary>One run's half of a picture comparison, headed by which run it is.</summary>
    private UIElement Side(string heading, ValueDescription? described)
    {
        StackPanel side = new() { Margin = new Thickness(0, 0, 10, 0) };

        side.Children.Add(new TextBlock
        {
            Text = heading,
            Style = (Style)FindResource("PanelHeading"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        BitmapSource? picture = IsPicture(described)
            ? WidgetImages.Read(RunFiles.Resolve(described!.Body), described.Body?.ContentHash, ComparedImageWidth)
            : null;

        if (picture is not null)
        {
            Image image = new()
            {
                Source = picture,
                Width = ComparedImageWidth / 2.0,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            side.Children.Add(image);

            return side;
        }

        // Two different absences, and a reader acts differently on each: one run never produced this,
        // or it did and the file is not on this machine.
        side.Children.Add(new TextBlock
        {
            Text = described is null
                ? "Not produced by this run."
                : "The picture could not be read from " + (described.Body?.RelativePath ?? "its file") + ".",
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            Width = ComparedImageWidth / 2.0,
            TextWrapping = TextWrapping.Wrap
        });

        return side;
    }

    /// <summary>Whether a described thing is something to look at rather than to read.</summary>
    private static bool IsPicture(ValueDescription? described)
        => described?.Preview?.Form == DebugPreviewForm.Image;

    /// <summary>
    /// One line of content, banded in the colour of what happened to it.
    /// </summary>
    /// <remarks>
    /// A wash behind the whole line rather than only coloured text: scanning a diff is looking for
    /// where the bands are, and a marker plus a tinted glyph asks the reader to read every line to
    /// find out. Unchanged lines get no band at all and a dimmer ink, so they read as the backdrop
    /// they are.
    /// </remarks>
    private Paragraph Line(DiffLine line)
    {
        (string surface, string ink) = line.Kind switch
        {
            DiffLineKind.Added => ("DiffAddedSurface", "DiffAddedText"),
            DiffLineKind.Removed => ("DiffRemovedSurface", "DiffRemovedText"),
            _ => (string.Empty, "DiffContextText")
        };

        Paragraph paragraph = new()
        {
            Margin = new Thickness(0),
            Foreground = (Brush)FindResource(ink)
        };

        if (surface.Length > 0)
            paragraph.Background = (Brush)FindResource(surface);

        paragraph.Inlines.Add(new Run(line.Marker + " " + line.Text));

        return paragraph;
    }

    /// <summary>
    /// How many characters of the diff pane a line of prose may use.
    /// </summary>
    /// <remarks>
    /// The inspector is a fixed 640 wide and the pane is monospaced at 11pt, which leaves about ninety
    /// characters; this sits inside that so a wrapped note never touches the scrollbar.
    /// </remarks>
    private const int NoteColumns = 84;

    /// <summary>
    /// A remark from the diff itself, wrapped so it can be read without scrolling sideways.
    /// </summary>
    /// <remarks>
    /// The document is deliberately far wider than the panel so a line of code is never broken
    /// mid-statement, and that stops prose wrapping too — an explanation of why a diff could not be
    /// shown would run off to the right where nobody reads it. Wrapped here by character count rather
    /// than by the layout engine: the pane is monospaced, so a count is exact and needs no layout pass.
    /// A width bound to the viewport was tried first and rendered nothing, the viewport being zero
    /// until after the first pass.
    /// </remarks>
    private IEnumerable<Block> Note(DiffLine line)
    {
        Brush ink = (Brush)FindResource("TextFaint");

        foreach (string wrapped in ValueInspection.WrapToWidth(line.Text, NoteColumns))
        {
            Paragraph paragraph = new()
            {
                Margin = new Thickness(0),
                FontStyle = FontStyles.Italic,
                Foreground = ink
            };

            paragraph.Inlines.Add(new Run(wrapped));

            yield return paragraph;
        }
    }

    /// <summary>
    /// The lines to show for a change, reconciled with what the comparison concluded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value's preview is not always where its content is. A short string fits in its summary and
    /// carries no separate preview at all, so diffing previews alone compared nothing against nothing
    /// and announced that the two were identical — directly contradicting the badge that had just
    /// called it changed.
    /// </para>
    /// <para>
    /// And even with previews on both sides they can legitimately match while the values differ: a
    /// collection that grew past the point the preview was cut has the same visible beginning. The
    /// comparison knows better, because it also weighed sizes and hashes. Where the two disagree the
    /// comparison wins and the panel says why it cannot show the difference, rather than quietly
    /// implying there is none.
    /// </para>
    /// </remarks>
    private static ImmutableList<DiffLine> DiffOf(ValueChange change)
    {
        ImmutableList<DiffLine> lines = TextDiff.Unified(DiffTextFor(change.Baseline), DiffTextFor(change.Current));

        bool saysIdentical = lines.Count == 1 && lines[0].Kind == DiffLineKind.Note;

        if (!saysIdentical || change.Change == ValueChangeKind.Unchanged)
            return lines;

        return
        [
            new DiffLine
            {
                Kind = DiffLineKind.Note,
                Text = "The part that was sent is the same in both runs, but the values differ - "
                     + Difference(change)
                     + ". Open both files to see where."
            }
        ];
    }

    /// <summary>Whatever of a value's content is available to compare, richest first.</summary>
    private static string DiffTextFor(ValueDescription? described)
    {
        if (described is null)
            return string.Empty;

        string preview = ValueInspection.PreviewText(described.Preview);

        if (!string.IsNullOrEmpty(preview))
            return preview;

        // A short value's content is its summary; there was never a preview to cut.
        if (!string.IsNullOrWhiteSpace(described.Summary))
            return described.Summary;

        return string.Join(Environment.NewLine, described.Facts.Select(fact => $"{fact.Name}: {fact.Value}"));
    }

    /// <summary>States what is known to differ when the visible text does not.</summary>
    private static string Difference(ValueChange change)
    {
        long? before = change.Baseline?.Body?.SizeInBytes ?? change.Baseline?.Preview?.SizeInBytes;
        long? after = change.Current?.Body?.SizeInBytes ?? change.Current?.Preview?.SizeInBytes;

        if (before is { } from && after is { } to && from != to)
            return $"{ValueInspection.Size(from, CultureInfo.CurrentCulture)} before, {ValueInspection.Size(to, CultureInfo.CurrentCulture)} now";

        string baselineHash = change.Baseline?.Body?.ContentHash ?? string.Empty;
        string currentHash = change.Current?.Body?.ContentHash ?? string.Empty;

        if (baselineHash.Length > 0 && currentHash.Length > 0)
            return "their content hashes do not match";

        return "their recorded facts do not match";
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
        pIcon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon.Colour));

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
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            Margin = new Thickness(0, 1, 14, 1)
        };

        TextBlock body = new()
        {
            Text = value,
            Foreground = (Brush)FindResource("TextSecondary"),
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
                    Foreground = (Brush)FindResource("TextFaint"),
                    FontSize = 10,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            wpVersions.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceCard"),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 0, 2),
                ToolTip = versions[index],
                Child = new TextBlock
                {
                    Text = "v" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = (Brush)FindResource("TextSecondary"),
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
            ? WidgetImages.Read(RunFiles.Resolve(described.Body), described.Body?.ContentHash, PreviewImageWidth)
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
    private string? BodyFile() => RunFiles.Resolve(body);


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
