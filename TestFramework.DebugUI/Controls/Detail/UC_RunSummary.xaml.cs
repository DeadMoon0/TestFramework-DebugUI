using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Axiom.State;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// Whether the run can be trusted, and what to look at if not.
/// </summary>
/// <remarks>
/// Counted by <see cref="RunTally"/> rather than here, so this page and the verdict drawn on the
/// board cannot give different answers to the same question.
/// </remarks>
public partial class UC_RunSummary : UserControl, IDisposable
{
    /// <summary>
    /// How many moved steps this page names.
    /// </summary>
    /// <remarks>
    /// The answer to "why was this run slow" is two or three steps. A table of every step that drifted is a
    /// table nobody finishes reading, and the board colours all of them anyway.
    /// </remarks>
    private const int MostMoversShown = 5;

    private readonly CompositeDisposable subscriptions = [];

    private RunTally tally = RunTally.Empty;
    private long? recorded;

    /// <summary>Creates the page and binds it.</summary>
    public UC_RunSummary()
    {
        InitializeComponent();

        // No copy buttons here. The verdict is one word and the counts are five numbers - quicker to
        // read out than to reach for a button, and a button beside each of them was clutter over the one
        // page in the tool that exists to be read at a glance.

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => RunTally.Of(state.ActiveRun))
            .Subscribe(Show));

        // Bound separately from the tally: the tally counts checks, and this page also has room to name the
        // ones that did not hold.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Assertions)
            .Subscribe(ShowChecks));

        // Arrives after the board, because a baseline means reading an earlier run's journal. Bound on its
        // own so the page fills in when it lands rather than waiting for it.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveTiming)
            .Subscribe(ShowTiming));

        // How big the recording is, which the tally cannot know: it counts what the graph holds, and this is
        // what the producer wrote. Counted into the sidecar as the run went, so reading it costs nothing.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs
                .Find(run => string.Equals(run.SessionId, state.SelectedSessionId, StringComparison.Ordinal))?.EventCount)
            .Subscribe(count =>
            {
                recorded = count;
                tbCounts.Text = Counts(tally, recorded);
            }));

    }


    private void Show(RunTally counted)
    {
        tally = counted;

        bool decided = tally.IsFinished || tally.Failed > 0;

        tbVerdict.Text = !decided ? "Still running" : tally.IsValid ? "Valid" : "Not valid";

        // A run nobody checked is reported as unproven rather than as a pass. Calling it valid
        // because nothing threw is the most flattering reading of the evidence, not the honest one.
        tbVerdictWhy.Text = Why(tally, decided);

        bVerdict.Background = new SolidColorBrush(
            !decided ? Color.FromArgb(0x28, 0x46, 0x80, 0xFC)
            : !tally.IsValid ? Color.FromArgb(0x28, 0xFC, 0x46, 0x46)
            : tally.HasAssertions ? Color.FromArgb(0x28, 0x62, 0xC9, 0x8F)
            : Color.FromArgb(0x28, 0xFC, 0xAF, 0x62));

        tbCounts.Text = Counts(tally, recorded);

        ShowFailures(tally);
    }

    private static string Why(RunTally tally, bool decided)
    {
        if (!decided)
            return $"{tally.Outstanding} of {tally.Steps} steps still to finish.";

        if (tally.Failed > 0 && tally.AssertionsFailed > 0)
            return $"{tally.Failed} step(s) failed and {tally.AssertionsFailed} assertion(s) did not hold.";

        if (tally.Failed > 0)
            return $"{tally.Failed} of {tally.Steps} steps failed.";

        if (tally.AssertionsFailed > 0)
            return $"Every step passed, but {tally.AssertionsFailed} assertion(s) did not hold.";

        return tally.HasAssertions
            ? $"Every step passed and all {tally.AssertionsPassed} assertion(s) held."
            : "Every step passed, but the run asserted nothing — so nothing was actually proven.";
    }

    private static string Counts(RunTally tally, long? recorded)
    {
        List<string> lines =
        [
            $"steps       {tally.Complete} complete · {tally.Failed} failed · {tally.Skipped} skipped · {tally.Outstanding} outstanding",
            $"assertions  {tally.AssertionsPassed} held · {tally.AssertionsFailed} did not",
            $"values      {tally.Variables} variable(s) · {tally.Artifacts} artifact(s)"
        ];

        if (tally.Retried > 0)
            lines.Add($"retries     {tally.Retried} step(s) needed more than one attempt");

        // Only for a run read from disk. A live run is still being written into the file this number is
        // counted in, so anything shown here mid-run would be the count as of whenever it was last read.
        if (recorded is long events)
            lines.Add($"recorded    {events.ToString("N0", CultureInfo.CurrentCulture)} event(s) on disk");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Lists the failures, each one a way back to the step it came from.
    /// </summary>
    private void ShowFailures(RunTally tally)
    {
        spFailures.Children.Clear();
        tbFailuresLabel.Visibility = tally.Failures.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (StepFailure failure in tally.Failures)
        {
            TextBlock heading = new()
            {
                Text = failure.DisplayName,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            TextBlock detail = new()
            {
                Text = failure.Detail is null
                    ? $"{failure.StageName} · step {failure.StepId.ToString(CultureInfo.InvariantCulture)}"
                    : $"{failure.Detail.ExceptionType}: {failure.Detail.Message}",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            StackPanel body = new() { Children = { heading, detail } };

            // The bottom of the chain, when there is one. A summary that lists five failures all reading
            // "One or more errors occurred" has told the reader only that five things broke; the innermost
            // exception is what distinguishes them, and it is a line rather than a panel.
            if (failure.Detail?.InnerExceptions is { Count: > 0 } chain)
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"↳ {chain[^1].ExceptionType}: {chain[^1].Message}",
                    Foreground = (Brush)FindResource("TextFaint"),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            Border row = new()
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceRaised"),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                ToolTip = "Open this step on the board.",
                Child = body
            };

            string stage = failure.StageName;
            int stepId = failure.StepId;

            row.MouseLeftButtonUp += (_, _) => MainWindow.Shell.SelectStep(stage, stepId);

            spFailures.Children.Add(row);
        }
    }

    /// <summary>
    /// Names the checks that did not hold.
    /// </summary>
    /// <remarks>
    /// The check as it was written, then the value that was actually there. Both come from the run as facts —
    /// the name with its typed arguments, and the observed value described — so this is a rendering of data
    /// rather than a sentence the framework assembled and this page reprinted.
    /// </remarks>
    private void ShowChecks(ImmutableList<AssertionNode> assertions)
    {
        spChecks.Children.Clear();

        AssertionNode[] broken = [.. assertions.Where(assertion => !assertion.Succeeded)];

        tbChecksLabel.Visibility = broken.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (AssertionNode assertion in broken)
        {
            TextBlock heading = new()
            {
                Text = $"{assertion.Subject} · {assertion.Render()}",
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            TextBlock detail = new()
            {
                Text = $"was {assertion.Actual.Summary}",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            spChecks.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceRaised"),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Child = new StackPanel { Children = { heading, detail } }
            });
        }
    }

    /// <summary>
    /// Says where the run's time went, against the last run of this test that passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shown only when something actually moved. A run that took the time it always takes has nothing to say
    /// here, and a section reading "the same as last time" on every passing run is a section people learn to
    /// skip — which costs it their attention on the run where it matters.
    /// </para>
    /// <para>
    /// Steps ordered by how much time moved rather than by ratio, and capped: the answer to "why was this run
    /// slow" is two or three steps, not a table of forty.
    /// </para>
    /// </remarks>
    private void ShowTiming(TimingDiff compared)
    {
        spTiming.Children.Clear();

        ImmutableList<StepTiming> movers = compared.BiggestMovers(MostMoversShown);
        bool worthShowing = compared.HasBaseline && (movers.Count > 0 || compared.RunMovedMaterially);

        tbTimingLabel.Visibility = worthShowing ? Visibility.Visible : Visibility.Collapsed;
        tbTimingHeadline.Visibility = worthShowing ? Visibility.Visible : Visibility.Collapsed;

        if (!worthShowing)
            return;

        tbTimingHeadline.Text = Headline(compared);

        foreach (StepTiming step in movers)
        {
            bool slower = step.Change == StepTimingChange.Slower;

            TextBlock heading = new()
            {
                Text = step.DisplayName,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            TextBlock detail = new()
            {
                Text = step.Ratio is { } ratio
                    ? $"{Took(step.Then ?? TimeSpan.Zero)} → {Took(step.Now ?? TimeSpan.Zero)}   ({ratio:0.#}×)"
                    : $"{Took(step.Then ?? TimeSpan.Zero)} → {Took(step.Now ?? TimeSpan.Zero)}",

                // Amber for slower, green for quicker. Never red: a step taking longer is worth seeing and
                // is not a failure, and red on this page means something broke.
                Foreground = (Brush)FindResource(slower ? "StateTimeout" : "StateComplete"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            };

            Border row = new()
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)FindResource("SurfaceRaised"),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                ToolTip = "Open this step on the board.",
                Child = new StackPanel { Children = { heading, detail } }
            };

            string stage = step.StageName;
            int stepId = step.StepId;

            row.MouseLeftButtonUp += (_, _) => MainWindow.Shell.SelectStep(stage, stepId);

            spTiming.Children.Add(row);
        }
    }

    /// <summary>
    /// The one sentence about the run as a whole.
    /// </summary>
    /// <remarks>
    /// The run's own wall clock, not the sum of the steps that moved: steps run in parallel layers, so adding
    /// up the movers would claim a number the run never took.
    /// </remarks>
    private static string Headline(TimingDiff compared)
    {
        string against = compared.Baseline is { } baseline
            ? $"when it last passed, on {baseline.StartedAtUtc.ToLocalTime():d MMM HH:mm}"
            : "when it last passed";

        // A live run has no end yet, so there is no total to compare — but its finished steps do have times,
        // and saying how many of them moved is still an answer.
        if (compared.Now is null || compared.Then is null)
        {
            return compared.SlowerCount > 0
                ? $"{compared.SlowerCount} step(s) took longer than {against}."
                : $"{compared.FasterCount} step(s) were quicker than {against}.";
        }

        string span = $"{Took(compared.Then.Value)} → {Took(compared.Now.Value)}";

        if (!compared.RunMovedMaterially)
            return $"Overall the run took about the same ({span}), but some of it moved.";

        string detail = compared.Ratio is { } ratio ? $"({ratio:0.#}×, {span})" : $"({span})";

        return compared.Delta > TimeSpan.Zero
            ? $"This run took {Took(compared.Delta)} longer than {against} {detail}."
            : $"This run was {Took(compared.Delta.Duration())} quicker than {against} {detail}.";
    }

    /// <summary>A duration as a reader says it, in the same shape the run list uses.</summary>
    private static string Took(TimeSpan took)
        => took < TimeSpan.FromSeconds(1) ? $"{took.TotalMilliseconds:F0} ms"
            : took < TimeSpan.FromMinutes(1) ? $"{took.TotalSeconds:F1} s"
            : $"{(int)took.TotalMinutes}m {took.Seconds}s";


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
