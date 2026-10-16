using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// The text diff the value inspector shows for a changed value, and what it says when the text cannot show the change.
/// </summary>
public static class ValueChangeText
{
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
    /// <param name="change">The value, as it was and as it is.</param>
    /// <param name="culture">How sizes are written.</param>
    /// <returns>The lines the inspector shows.</returns>
    public static ImmutableList<DiffLine> Of(ValueChange change, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(culture);

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
                     + Difference(change, culture)
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
    public static string Difference(ValueChange change, CultureInfo culture)
    {
        long? before = change.Baseline?.Body?.SizeInBytes ?? change.Baseline?.Preview?.SizeInBytes;
        long? after = change.Current?.Body?.SizeInBytes ?? change.Current?.Preview?.SizeInBytes;

        if (before is { } from && after is { } to && from != to)
            return $"{ValueInspection.Size(from, culture)} before, {ValueInspection.Size(to, culture)} now";

        string baselineHash = change.Baseline?.Body?.ContentHash ?? string.Empty;
        string currentHash = change.Current?.Body?.ContentHash ?? string.Empty;

        if (baselineHash.Length > 0 && currentHash.Length > 0)
            return "their content hashes do not match";

        return "their recorded facts do not match";
    }
}
