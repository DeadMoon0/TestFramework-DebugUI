using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State.Bundles;
using TestFramework.DebugUI.State;

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
public partial class UC_ValueInspector : UserControl
{
    private CompositeDisposable subscriptions = [];
    private ValueBody? body;

    /// <summary>What is on screen, so the diff can be rebuilt when the comparison arrives.</summary>
    private string? shownKey;
    private bool shownIsArtifact;
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

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader closes the inspector.</summary>
    public event Action? Closed;

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

        // Opening a value shows the value. The diff is a deliberate second step: most of the time a
        // reader opening a value wants to read it, not to read what it used to be.
        showingDiff = false;
        btDiff.Content = "Diff";
        rtDiff.Visibility = Visibility.Collapsed;
        tbPreview.Visibility = Visibility.Visible;

        subscriptions.Add(isArtifact
            ? StateStore<MainState>.Default
                .Bind(state => state.ActiveRun.Artifacts.TryGetValue(key, out ArtifactNode? artifact) ? artifact : null)
                .Subscribe(ShowArtifact)
            : StateStore<MainState>.Default
                .Bind(state => state.ActiveRun.Variables.TryGetValue(key, out ValueNode? value) ? value : null)
                .Subscribe(ShowVariable));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => isArtifact
                ? state.ActiveDiff.ChangeForArtifact(key)
                : state.ActiveDiff.ChangeForVariable(key))
            .Subscribe(ShowComparison));

        Visibility = Visibility.Visible;
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
        tbPreview.Visibility = Visibility.Visible;

        if (shownKey is not null)
            Reshow();
    }

    /// <summary>Re-renders the open value from current state, without rebinding.</summary>
    private void Reshow()
    {
        MainState state = StateStore<MainState>.Default.GetValue(current => current);

        if (shownIsArtifact)
        {
            if (state.ActiveRun.Artifacts.TryGetValue(shownKey!, out ArtifactNode? artifact))
                ShowArtifact(artifact);

            return;
        }

        if (state.ActiveRun.Variables.TryGetValue(shownKey!, out ValueNode? value))
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
        rtDiff.Visibility = Visibility.Visible;

        tbPreviewLabel.Text = change.Change switch
        {
            ValueChangeKind.Added => "NOT IN THE LAST PASSING RUN",
            ValueChangeKind.Removed => "GONE SINCE THE LAST PASSING RUN",
            _ => "CHANGED SINCE THE LAST PASSING RUN"
        };

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

        Render(value.Key, value.SchemaKey, value.DisplayText, value.Description, []);
    }

    private void ShowArtifact(ArtifactNode? artifact)
    {
        if (artifact is null)
            return;

        Render(artifact.Key, artifact.SchemaKey, artifact.DisplayText, artifact.Description, artifact.Versions);
    }

    private void Render(string key, string schemaKey, string displayText, ValueDescription described, ImmutableList<string> versions)
    {
        tbKey.Text = key;
        tbSummary.Text = ValueInspection.Headline(described, displayText);

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
            AddFact(fact.Name, fact.Value);
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
        tbPreview.Text = ValueInspection.PreviewText(described.Preview);
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

    /// <summary>
    /// The file this value was written to, wherever it is now.
    /// </summary>
    /// <remarks>
    /// Asked of <see cref="ValueFiles"/> rather than tested with <c>File.Exists</c> on the recorded path, so a
    /// run that arrived from another machine finds its files beside its journal instead of looking empty.
    /// </remarks>
    private string? BodyFile()
        => body is null
            ? null
            : ValueFiles.Resolve(body.Path, body.RelativePath, JournalPath());

    /// <summary>The journal the selected run was replayed from, when it came from disk.</summary>
    private static string? JournalPath()
        => StateStore<MainState>.Default
            .GetValue(state => state.Runs
                .Find(run => string.Equals(run.SessionId, state.SelectedSessionId, StringComparison.Ordinal))?.JournalPath);

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
