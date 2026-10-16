using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;

using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

// Setting the open value against the last passing run's, as a unified diff of the rendered previews.
public partial class UC_ValueInspector
{
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
        MainState state = store.GetValue(current => current);

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

        foreach (DiffLine line in ValueChangeText.Of(change, CultureInfo.CurrentCulture))
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
            DiffLineKind.Added => (ThemeKeys.DiffAddedSurface, ThemeKeys.DiffAddedText),
            DiffLineKind.Removed => (ThemeKeys.DiffRemovedSurface, ThemeKeys.DiffRemovedText),
            _ => (string.Empty, ThemeKeys.DiffContextText)
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
        Brush ink = (Brush)FindResource(ThemeKeys.TextFaint);

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
}
