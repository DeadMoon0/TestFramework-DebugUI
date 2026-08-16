using System;
using System.Collections.Generic;
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
public partial class UC_RunSummary : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates the page and binds it.</summary>
    public UC_RunSummary()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => RunTally.Of(state.ActiveRun))
            .Subscribe(Show));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader closes the page.</summary>
    public event Action? Closed;

    private void Show(RunTally tally)
    {
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

        tbCounts.Text = Counts(tally);

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

    private static string Counts(RunTally tally)
    {
        List<string> lines =
        [
            $"steps       {tally.Complete} complete · {tally.Failed} failed · {tally.Skipped} skipped · {tally.Outstanding} outstanding",
            $"assertions  {tally.AssertionsPassed} held · {tally.AssertionsFailed} did not",
            $"values      {tally.Variables} variable(s) · {tally.Artifacts} artifact(s)"
        ];

        if (tally.Retried > 0)
            lines.Add($"retries     {tally.Retried} step(s) needed more than one attempt");

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

            Border row = new()
            {
                CornerRadius = new CornerRadius(2.5),
                Background = (Brush)FindResource("SurfaceRaised"),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                ToolTip = "Open this step on the board.",
                Child = new StackPanel { Children = { heading, detail } }
            };

            string stage = failure.StageName;
            int stepId = failure.StepId;

            row.MouseLeftButtonUp += (_, _) => MainWindow.Shell.SelectStep(stage, stepId);

            spFailures.Children.Add(row);
        }
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
