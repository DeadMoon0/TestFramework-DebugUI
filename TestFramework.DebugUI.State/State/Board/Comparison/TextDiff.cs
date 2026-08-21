using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>What a line in a diff is.</summary>
public enum DiffLineKind
{
    /// <summary>Present in both, shown for orientation.</summary>
    Context,

    /// <summary>Only in the current run.</summary>
    Added,

    /// <summary>Only in the baseline.</summary>
    Removed,

    /// <summary>A note from the diff itself, such as how many unchanged lines were skipped.</summary>
    Note
}

/// <summary>One line of a rendered diff.</summary>
public sealed record DiffLine
{
    public required DiffLineKind Kind { get; init; }

    public required string Text { get; init; }

    /// <summary>The marker a unified diff puts in front of the line.</summary>
    public string Marker => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        DiffLineKind.Note => string.Empty,
        _ => " "
    };
}

/// <summary>
/// A line-level diff of two pieces of text.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately its own pure function rather than something the inspector does while drawing: the
/// interesting behaviour is which lines are called changed, and that is worth testing without a
/// window. It also has to be bounded, because it is handed rendered previews — indenting a JSON
/// document turns a one-line value into thousands of lines, and a quadratic diff over two of those
/// would freeze the UI.
/// </para>
/// <para>
/// Bounded in two steps. Identical prefixes and suffixes are matched off first, which in the ordinary
/// case — one field of a large document changed — leaves almost nothing to compare. Only what remains
/// goes through the quadratic part, and if that is still too big the diff says so rather than trying.
/// </para>
/// </remarks>
public static class TextDiff
{
    /// <summary>
    /// The largest middle section that will be compared line by line.
    /// </summary>
    /// <remarks>
    /// The table is this squared, so it is the number that decides how much memory a diff can want.
    /// Beyond it the honest answer is that the two differ too widely to line up usefully — a diff that
    /// pairs unrelated lines from two mostly-different documents is harder to read than a plain
    /// statement that they are different.
    /// </remarks>
    public const int MaximumComparedLines = 1_500;

    /// <summary>How many unchanged lines are kept either side of a change.</summary>
    public const int ContextLines = 3;

    /// <summary>Diffs two texts by line.</summary>
    public static ImmutableList<DiffLine> Unified(string? baseline, string? current)
    {
        string[] left = Split(baseline);
        string[] right = Split(current);

        if (left.SequenceEqual(right, StringComparer.Ordinal))
            return [new DiffLine { Kind = DiffLineKind.Note, Text = "The two are identical." }];

        int prefix = CommonPrefix(left, right);
        int suffix = CommonSuffix(left, right, prefix);

        int leftMiddle = left.Length - prefix - suffix;
        int rightMiddle = right.Length - prefix - suffix;

        if (leftMiddle > MaximumComparedLines || rightMiddle > MaximumComparedLines)
        {
            return
            [
                new DiffLine
                {
                    Kind = DiffLineKind.Note,
                    Text = $"Too much differs to line up: {leftMiddle:N0} line(s) before and {rightMiddle:N0} after, past the {MaximumComparedLines:N0}-line limit. Open both values to compare them."
                }
            ];
        }

        List<DiffLine> lines = [];

        // The matched prefix and suffix are context like any other, so the same trimming applies to
        // them; they are simply known to be equal without comparing them.
        AppendAligned(
            lines,
            left, right,
            prefix, suffix,
            Align(left.AsSpan(prefix, leftMiddle).ToArray(), right.AsSpan(prefix, rightMiddle).ToArray()));

        return [.. lines];
    }

    /// <summary>
    /// Pairs up the differing middle by longest common subsequence.
    /// </summary>
    /// <remarks>
    /// LCS rather than a naive line-by-line walk, because an inserted line at the top would otherwise
    /// mark every line below it as changed — which is exactly the case a reader most needs to see
    /// through.
    /// </remarks>
    private static List<DiffLine> Align(string[] left, string[] right)
    {
        int[,] lengths = new int[left.Length + 1, right.Length + 1];

        for (int i = left.Length - 1; i >= 0; i--)
        {
            for (int j = right.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        List<DiffLine> middle = [];
        int x = 0;
        int y = 0;

        while (x < left.Length && y < right.Length)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                middle.Add(new DiffLine { Kind = DiffLineKind.Context, Text = left[x] });
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                middle.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[x] });
                x++;
            }
            else
            {
                middle.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[y] });
                y++;
            }
        }

        while (x < left.Length)
            middle.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[x++] });

        while (y < right.Length)
            middle.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[y++] });

        return middle;
    }

    /// <summary>
    /// Assembles the result, keeping only the context around each change.
    /// </summary>
    /// <remarks>
    /// A value whose four-thousandth line changed should not make a reader scroll past three thousand
    /// nine hundred identical ones. Skipped runs are stated with their count rather than silently
    /// dropped, so the diff never looks shorter than the value is.
    /// </remarks>
    private static void AppendAligned(List<DiffLine> lines, string[] left, string[] right, int prefix, int suffix, List<DiffLine> middle)
    {
        List<DiffLine> all =
        [
            .. left.Take(prefix).Select(text => new DiffLine { Kind = DiffLineKind.Context, Text = text }),
            .. middle,
            .. left.Skip(left.Length - suffix).Select(text => new DiffLine { Kind = DiffLineKind.Context, Text = text })
        ];

        bool[] keep = new bool[all.Count];

        for (int index = 0; index < all.Count; index++)
        {
            if (all[index].Kind is DiffLineKind.Context)
                continue;

            for (int near = Math.Max(0, index - ContextLines); near <= Math.Min(all.Count - 1, index + ContextLines); near++)
                keep[near] = true;
        }

        int skipped = 0;

        for (int index = 0; index < all.Count; index++)
        {
            if (keep[index])
            {
                if (skipped > 0)
                {
                    lines.Add(new DiffLine { Kind = DiffLineKind.Note, Text = $"... {skipped:N0} unchanged line(s)" });
                    skipped = 0;
                }

                lines.Add(all[index]);
                continue;
            }

            skipped++;
        }

        if (skipped > 0)
            lines.Add(new DiffLine { Kind = DiffLineKind.Note, Text = $"... {skipped:N0} unchanged line(s)" });
    }

    private static string[] Split(string? text)
        => string.IsNullOrEmpty(text) ? [] : text.ReplaceLineEndings("\n").Split('\n');

    private static int CommonPrefix(string[] left, string[] right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int count = 0;

        while (count < limit && string.Equals(left[count], right[count], StringComparison.Ordinal))
            count++;

        return count;
    }

    private static int CommonSuffix(string[] left, string[] right, int prefix)
    {
        int limit = Math.Min(left.Length, right.Length) - prefix;
        int count = 0;

        while (count < limit
               && string.Equals(left[left.Length - 1 - count], right[right.Length - 1 - count], StringComparison.Ordinal))
            count++;

        return count;
    }
}
