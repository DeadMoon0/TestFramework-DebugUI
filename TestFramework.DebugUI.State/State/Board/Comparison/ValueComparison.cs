using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State.Board.Comparison;

/// <summary>
/// How one value stands against the same value in an earlier run.
/// </summary>
public enum ValueChangeKind
{
    /// <summary>Present in both runs and carrying the same content.</summary>
    Unchanged,

    /// <summary>Present in both runs and carrying different content.</summary>
    Changed,

    /// <summary>This run produced it and the baseline did not.</summary>
    Added,

    /// <summary>The baseline produced it and this run did not.</summary>
    Removed,

    /// <summary>
    /// Present in both, but the two descriptions cannot be compared.
    /// </summary>
    /// <remarks>
    /// Reported rather than guessed. A value whose content never reached the debugger — no hash, no
    /// preview, nothing but a type name — is not evidence of equality, and calling it unchanged would
    /// be a false negative in the one place a reader is trusting the tool to tell them what moved.
    /// </remarks>
    Indeterminate
}

/// <summary>One value's standing against the baseline run.</summary>
public sealed record ValueChange
{
    /// <summary>The variable or artifact key.</summary>
    public required string Key { get; init; }

    /// <summary>Whether this is a variable or an artifact.</summary>
    public required DebugValueKindTag Kind { get; init; }

    public required ValueChangeKind Change { get; init; }

    /// <summary>What the baseline run had, when it had it.</summary>
    public ValueDescription? Baseline { get; init; }

    /// <summary>What this run has, when it has it.</summary>
    public ValueDescription? Current { get; init; }

    /// <summary>Why the two could not be compared, when they could not.</summary>
    public string? Reason { get; init; }

    /// <summary>Whether this is worth a reader's attention.</summary>
    public bool IsInteresting => Change is not ValueChangeKind.Unchanged;
}

/// <summary>Whether a compared value is a variable or an artifact.</summary>
public enum DebugValueKindTag
{
    Variable,
    Artifact
}

/// <summary>
/// Compares the values a run produced against the values an earlier run produced.
/// </summary>
/// <remarks>
/// <para>
/// The question this answers is "what is different from the last time this worked", so the comparison
/// has to be about <em>content</em>, not about the description that happens to carry it. Two runs
/// describe the same value differently in ways that mean nothing: a file-backed value records the path
/// it was written to, and that path contains the run's own folder, so comparing whole descriptions
/// would report every large value as changed on every run.
/// </para>
/// <para>
/// So the order is: a content hash if both have one, then the rendered preview, then the facts. The
/// first of those that both sides can answer decides it, and if none can the result is
/// <see cref="ValueChangeKind.Indeterminate"/> rather than a guess.
/// </para>
/// </remarks>
public static class ValueComparison
{
    /// <summary>
    /// Compares two runs' values, keyed by name.
    /// </summary>
    /// <param name="baseline">What the earlier run produced.</param>
    /// <param name="current">What this run produced.</param>
    /// <param name="kind">Whether these are variables or artifacts.</param>
    /// <returns>
    /// One entry per key in either run, ordered so the changes a reader came for are first.
    /// </returns>
    public static ImmutableList<ValueChange> Compare(
        IReadOnlyDictionary<string, ValueDescription> baseline,
        IReadOnlyDictionary<string, ValueDescription> current,
        DebugValueKindTag kind)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        IEnumerable<string> keys = baseline.Keys
            .Concat(current.Keys)
            .Distinct(StringComparer.Ordinal);

        return
        [
            .. keys
                .Select(key => For(key, kind, Find(baseline, key), Find(current, key)))

                // Changed first, then added, then removed, then the ones that could not be told
                // apart, then the unchanged bulk. A rail that opens on forty unchanged values buries
                // the two that moved.
                .OrderBy(change => SortRank(change.Change))
                .ThenBy(change => change.Key, StringComparer.Ordinal)
        ];
    }

    /// <summary>Compares one key.</summary>
    public static ValueChange For(string key, DebugValueKindTag kind, ValueDescription? baseline, ValueDescription? current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (baseline is null && current is null)
            throw new ArgumentException($"'{key}' is in neither run, so there is nothing to compare.", nameof(current));

        if (baseline is null)
            return new ValueChange { Key = key, Kind = kind, Change = ValueChangeKind.Added, Current = current };

        if (current is null)
            return new ValueChange { Key = key, Kind = kind, Change = ValueChangeKind.Removed, Baseline = baseline };

        (ValueChangeKind change, string? reason) = CompareContent(baseline, current);

        return new ValueChange
        {
            Key = key,
            Kind = kind,
            Change = change,
            Baseline = baseline,
            Current = current,
            Reason = reason
        };
    }

    /// <summary>
    /// Decides whether two descriptions of the same key carry the same content.
    /// </summary>
    /// <remarks>
    /// Each rung is only used when <em>both</em> sides can answer it. Falling through to a weaker
    /// comparison because one side happens to lack the stronger one would compare two different
    /// things and call the answer equality.
    /// </remarks>
    private static (ValueChangeKind Change, string? Reason) CompareContent(ValueDescription baseline, ValueDescription current)
    {
        // A hash is the only comparison that is true about the whole value rather than about however
        // much of it fitted in a preview.
        string baselineHash = baseline.Body?.ContentHash ?? string.Empty;
        string currentHash = current.Body?.ContentHash ?? string.Empty;

        if (baselineHash.Length > 0 && currentHash.Length > 0)
        {
            return string.Equals(baselineHash, currentHash, StringComparison.OrdinalIgnoreCase)
                ? (ValueChangeKind.Unchanged, null)
                : (ValueChangeKind.Changed, null);
        }

        // One side written to a file and the other not is itself a change in what the value is, and
        // the sizes are comparable even when the hashes are not.
        long? baselineSize = baseline.Body?.SizeInBytes ?? baseline.Preview?.SizeInBytes;
        long? currentSize = current.Body?.SizeInBytes ?? current.Preview?.SizeInBytes;

        if (baselineSize is { } left && currentSize is { } right && left != right)
            return (ValueChangeKind.Changed, null);

        if (baseline.Preview is { } baselinePreview && current.Preview is { } currentPreview)
        {
            if (baselinePreview.Form != currentPreview.Form)
                return (ValueChangeKind.Changed, null);

            if (!string.Equals(baselinePreview.Text, currentPreview.Text, StringComparison.Ordinal))
                return (ValueChangeKind.Changed, null);

            // Equal previews that were both cut short prove only that the values begin the same way.
            // With equal sizes and no hash there is nothing further to appeal to.
            if (baselinePreview.IsTruncated || currentPreview.IsTruncated)
            {
                return (ValueChangeKind.Indeterminate,
                    "Both previews were cut short and neither value carries a content hash, so only their beginnings are known to match.");
            }

            return (ValueChangeKind.Unchanged, null);
        }

        // No hash and no preview on at least one side. The facts are all that is left; the summary is
        // deliberately not used, being prose built for a human rather than a comparison.
        if (baseline.Facts.Count > 0 || current.Facts.Count > 0)
        {
            return baseline.Facts.SequenceEqual(current.Facts)
                ? (ValueChangeKind.Unchanged, null)
                : (ValueChangeKind.Changed, null);
        }

        if (baseline.Shape != current.Shape)
            return (ValueChangeKind.Changed, null);

        return (ValueChangeKind.Indeterminate,
            "Neither run recorded content for this value - no hash, no preview and no facts - so nothing about it can be compared.");
    }

    /// <summary>
    /// Looks a key up, treating an empty description as absent.
    /// </summary>
    /// <remarks>
    /// A key can be declared and carry nothing — a value the run never assigned. Reporting that as
    /// present would turn every such declaration into a spurious "removed" against a run that did
    /// assign it, which is the opposite of what the reader is being told.
    /// </remarks>
    private static ValueDescription? Find(IReadOnlyDictionary<string, ValueDescription> values, string key)
        => values.TryGetValue(key, out ValueDescription? found) && !found.Equals(ValueDescription.Empty)
            ? found
            : null;

    private static int SortRank(ValueChangeKind change) => change switch
    {
        ValueChangeKind.Changed => 0,
        ValueChangeKind.Added => 1,
        ValueChangeKind.Removed => 2,
        ValueChangeKind.Indeterminate => 3,
        _ => 4
    };
}
