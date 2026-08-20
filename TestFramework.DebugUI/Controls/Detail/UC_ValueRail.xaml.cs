using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// Every value the run produced: its variables and its artifacts, in one list.
/// </summary>
/// <remarks>
/// <para>
/// Variables had no home at all once the board stopped drawing labelled boxes on its pipes, so a run
/// could assign twenty of them and show none. Artifacts had a rail to themselves, which put the
/// framework's own distinction in front of a reader who only wants to know what the run produced.
/// </para>
/// <para>
/// Artifacts lead because they have a lifecycle and a version history to answer for; a variable is
/// its current value and nothing else.
/// </para>
/// </remarks>
public partial class UC_ValueRail : UserControl, IDisposable
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates the rail and binds it.</summary>
    public UC_ValueRail()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun)
            .Select(Rows)
            .BindToCollection(spValues.Children, row => row.Id, row => Build(row)));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun)
            .Select(run => run.Artifacts.Count + run.Variables.Count == 0 ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun)
            .Select(Counts)
            .BindToDependencyProperty(tbCounts, TextBlock.TextProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveDiff)
            .Subscribe(ShowBaseline));

    }

    /// <summary>Raised when the reader asks to see one value in full.</summary>
    public event Action<string, bool>? ValueOpened;

    private UC_ValueItem Build(ValueRow row)
    {
        UC_ValueItem item = new(row.Key, row.IsArtifact);

        item.Opened += (key, isArtifact) => ValueOpened?.Invoke(key, isArtifact);

        return item;
    }

    /// <summary>
    /// Says which run the badges are measured against, or why none could be.
    /// </summary>
    /// <remarks>
    /// The baseline is named by its start time rather than its session id. A reader recognises the run
    /// they watched five minutes ago by when it ran; a GUID identifies it to the tool and to nobody
    /// else.
    /// </remarks>
    private void ShowBaseline(ValueDiff diff)
    {
        if (diff is null || (diff.Baseline is null && diff.Unavailable is null))
        {
            tbBaseline.Visibility = Visibility.Collapsed;
            return;
        }

        tbBaseline.Visibility = Visibility.Visible;

        if (diff.Baseline is not { } baseline)
        {
            tbBaseline.Text = "No comparison: " + diff.Unavailable;
            tbBaseline.Foreground = (Brush)FindResource("TextFaint");
            return;
        }

        string against = $"vs the last passing run, {baseline.StartedAtUtc.ToLocalTime():HH:mm:ss}";

        int changed = diff.ChangedCount;

        // The count is the point of the line. "Nothing changed" is a result worth stating outright,
        // because the alternative reading of an unbadged rail is that the comparison never ran.
        tbBaseline.Text = changed == 0
            ? against + " - nothing changed"
            : $"{against} - {changed} value{(changed == 1 ? "" : "s")} differ{(changed == 1 ? "s" : "")}";

        tbBaseline.Foreground = (Brush)FindResource(changed == 0 ? "TextFaint" : "StateTimeout");
    }

    private static IEnumerable<ValueRow> Rows(RunGraph run)
        =>
        [
            .. run.Artifacts.Values
                .OrderBy(artifact => artifact.Key, StringComparer.Ordinal)
                .Select(artifact => new ValueRow(artifact.Key, true)),
            .. run.Variables.Values
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new ValueRow(value.Key, false))
        ];

    private static string Counts(RunGraph run)
    {
        if (run.Artifacts.Count == 0 && run.Variables.Count == 0)
            return string.Empty;

        // Both counts, even when one is zero, so "no artifacts" is stated rather than left to be
        // inferred from an absence.
        return $"{run.Artifacts.Count} artifacts · {run.Variables.Count} variables";
    }

    /// <summary>
    /// One row's identity.
    /// </summary>
    /// <remarks>
    /// Keyed with the kind included, because a variable and an artifact are allowed to share a name
    /// and the collection binding would otherwise treat them as the same row.
    /// </remarks>
    private readonly record struct ValueRow(string Key, bool IsArtifact)
    {
        internal string Id => (IsArtifact ? "artifact:" : "variable:") + Key;
    }

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
