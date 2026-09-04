using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Controls.Detail;
using TestFramework.DebugUI.Layout;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// What the cards on a board say, as against where they are.
/// </summary>
/// <remarks>
/// <para>
/// Everything here changes without moving anything: a step goes from running to complete, a value
/// arrives and lights its pipe, a breakpoint is set, a screenshot appears. Rebuilding the board for
/// any of those would throw the visual tree away twenty times a second on a busy run, so this reaches
/// the elements <see cref="BoardComposer"/> made and repaints them in place.
/// </para>
/// <para>
/// It holds nothing about the run. What it needs - the timings against the last passing run, which
/// checks broke, which step is picked - is read when it paints, because all of it arrives later and
/// separately than the board does.
/// </para>
/// </remarks>
internal sealed class BoardPainter
{
    private readonly FrameworkElement resources;

    private BoardVisuals visuals = BoardVisuals.Empty;

    /// <summary>The arrangement the visuals were built from, which is what the pipes are walked from.</summary>
    private LayoutResult Board { get; set; } = LayoutResult.Empty;

    /// <summary>Repaints one board.</summary>
    /// <param name="resources">Where brushes are found; the board itself.</param>
    public BoardPainter(FrameworkElement resources)
    {
        this.resources = resources;
    }

    /// <summary>How this run's steps timed against the last run of the test that passed.</summary>
    /// <remarks>
    /// Held rather than read on demand because it arrives late - a journal has to be read for it - and the
    /// board is already drawn by then. Kept out of the geometry deliberately: a step getting slower moves
    /// nothing, so this repaints the card rather than relaying out the run.
    /// </remarks>
    public TimingDiff Timing { get; set; } = TimingDiff.None;

    /// <summary>The marks, which decide how a card's corner is drawn.</summary>
    public BreakpointService? Breakpoints { get; set; }

    /// <summary>Takes the elements a fresh board was built from.</summary>
    public void Paints(LayoutResult board, BoardVisuals drawn)
    {
        Board = board;
        visuals = drawn;
    }

    /// <summary>Whether a step has started, which is what decides if it is drawn as live.</summary>
    private static bool HasRun(StepNode step)
        => step.Lifecycle != DebugLifecycleState.Initialized || step.Attempts.Count > 0;

    /// <summary>Dims every pipe whose value has not been produced yet.</summary>
    /// <remarks>
    /// A pipe is lit by its value existing rather than by its producer's state: a value can be
    /// written by a step that then fails, and the flow did happen. The verdict's pipes are left
    /// alone — they are coloured by whether the check held, which is not a question of flow.
    /// </remarks>
    private void RefreshPipes(RunGraph graph)
    {
        foreach (LayoutEdge edge in Board.Edges)
        {
            if (!visuals.Pipes.TryGetValue(edge.Id, out Path? stroke))
                continue;

            bool carried = edge.ValueKind == DebugValueKind.Artifact
                ? graph.Artifacts.ContainsKey(edge.Key)
                : graph.Variables.ContainsKey(edge.Key);

            stroke.Opacity = carried ? 1 : BoardMetrics.DormantOpacity;
        }
    }

    /// <summary>
    /// Repaints what the geometry does not carry: lifecycle, selection, breakpoints, attempts.
    /// </summary>
    public void Refresh()
    {
        if (visuals.Steps.Count == 0)
            return;

        RunGraph graph = StateStore<MainState>.Default.GetValue(state => state.Board.ActiveRun);
        StepSelection? selected = StateStore<MainState>.Default.GetValue(state => state.Board.SelectedStep);

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                if (!visuals.Steps.TryGetValue($"step:{stage.Name}/{step.StepId}", out StepVisual? visual))
                    continue;

                visual.Name.Text = step.DisplayName;
                visual.Note.Text = Note(step);
                ShowElapsed(visual.Elapsed, stage.Name, step);
                ShowWidget(visual.Widget, graph, stage.Name, step);
                visual.Status.Background = BrushFor(step);
                visual.Outputs.Text = Describe(step);

                bool isSelected = selected is not null
                                  && selected.StepId == step.StepId
                                  && string.Equals(selected.StageName, stage.Name, StringComparison.Ordinal);

                // A step that has not run yet is context, not content. Dimming it lets the eye find
                // what is happening now without hunting through everything that is merely declared.
                visual.Box.Opacity = HasRun(step) ? 1 : BoardMetrics.DormantOpacity;

                ShowBreakpoint(visual.Breakpoint, Breakpoints?.IsSet(stage.Name, step.StepId) == true);

                // The halt outranks the selection. A run stopped somewhere is the most important thing on
                // the board and lasts only until it is released, whereas which step a reader last clicked
                // is on the panel to the right anyway. A breakpoint that is merely set is the marker's job
                // now, which is what frees the border to mean "stopped, here".
                visual.Box.BorderBrush = step.IsWaitingAtBreakpoint
                    ? (Brush)resources.FindResource(ThemeKeys.StateError)
                    : isSelected
                        ? (Brush)resources.FindResource(ThemeKeys.Accent)
                        : Brushes.Transparent;
            }
        }

        RefreshVerdict(graph);
        RefreshPipes(graph);
    }

    /// <summary>
    /// Repaints the verdict from the run as a whole.
    /// </summary>
    /// <remarks>
    /// Counted by <see cref="RunTally"/>, the same function the summary page uses, so the box on the
    /// board and the page behind it cannot give different answers to the same question.
    /// </remarks>
    private void RefreshVerdict(RunGraph graph)
    {
        if (visuals.Verdict is null)
            return;

        RunTally tally = RunTally.Of(graph);
        bool decided = tally.IsFinished || tally.Failed > 0 || tally.AssertionsFailed > 0;

        visuals.Verdict.Heading.Text = !decided ? "Checking" : tally.IsValid ? "Valid" : "Not valid";

        visuals.Verdict.Why.Text = !decided
            ? $"{tally.AssertionsPassed} check(s) held so far."
            : tally.AssertionsFailed > 0
                ? $"{tally.AssertionsFailed} of {tally.AssertionsPassed + tally.AssertionsFailed} check(s) did not hold."
                : tally.Failed > 0
                    ? $"Every check held, but {tally.Failed} step(s) failed."
                    : $"All {tally.AssertionsPassed} check(s) held.";

        visuals.Verdict.Box.BorderBrush = (Brush)resources.FindResource(
            !decided ? ThemeKeys.StateRunning : tally.IsValid ? ThemeKeys.StateComplete : ThemeKeys.StateError);
    }

    /// <summary>The line under a step's name: whatever is worth knowing without opening it.</summary>
    private static string Note(StepNode step)
    {
        if (step.IsWaitingAtBreakpoint)
            return "waiting at breakpoint";

        string state = step.Lifecycle.ToString().ToLowerInvariant();

        if (step.Attempts.LastOrDefault()?.Failure is DebugFailureDetail failure)
            return $"{state} · {failure.ExceptionType}";

        if (step.Attempts.Count > 1)
            return $"{state} · attempt {step.Attempts.Count}";

        return state;
    }

    /// <summary>
    /// Puts a step's time on its card, and says how that compares with the last time this test passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison is spent on the number that is already there rather than on a badge of its own. A
    /// reader scanning the column for the step that cost the run its time is reading these numbers anyway;
    /// colouring one amber and writing <c>+2.1 s</c> after it answers "is that normal", which is the question
    /// the bare number cannot.
    /// </para>
    /// <para>
    /// The delta rather than the ratio, because the delta is the actionable half: eight seconds appearing in
    /// one step is where the run went, whether that step was twice or twenty times slower. The ratio and the
    /// old number are on the pointer for whoever wants them.
    /// </para>
    /// </remarks>
    private void ShowElapsed(TextBlock elapsed, string stageName, StepNode step)
    {
        if (step.Duration is not { } duration)
        {
            elapsed.Text = string.Empty;
            elapsed.ToolTip = null;
            elapsed.Foreground = (Brush)resources.FindResource(ThemeKeys.TextFaint);
            return;
        }

        StepTiming? compared = Timing.ForStep(stageName, step.StepId);

        if (compared is null || !compared.IsInteresting)
        {
            elapsed.Text = DurationText.Compact(duration);
            elapsed.ToolTip = compared?.Then is { } unchanged ? $"About the same as last time: {DurationText.Compact(unchanged)}" : null;
            elapsed.Foreground = (Brush)resources.FindResource(ThemeKeys.TextFaint);
            return;
        }

        bool slower = compared.Change == StepTimingChange.Slower;

        elapsed.Text = $"{DurationText.Compact(duration)}  {(slower ? "+" : "−")}{DurationText.Compact(compared.Delta.Duration())}";

        // Amber rather than red. A slower step is worth noticing and is not a failure, and red on this board
        // already means the step broke.
        elapsed.Foreground = (Brush)resources.FindResource(slower ? ThemeKeys.StateTimeout : ThemeKeys.StateComplete);

        elapsed.ToolTip = compared.Ratio is { } ratio
            ? $"Was {DurationText.Compact(compared.Then ?? TimeSpan.Zero)} when this test last passed — {ratio:0.#}× that now."
            : $"Was {DurationText.Compact(compared.Then ?? TimeSpan.Zero)} when this test last passed.";
    }

    /// <summary>
    /// A duration at a scale a reader can compare at a glance.
    /// </summary>
    /// <remarks>
    /// Whole units, and never more than three significant figures: the board is scanned for the step
    /// that stands out, and 1.4 s against 12 ms says that immediately where 1402.318 ms does not.
    /// </remarks>
    /// <summary>What the step declared it takes and gives, which is the shape of its connectors.</summary>
    private static string Describe(StepNode step)
    {
        string inputs = step.Inputs.Count == 0 ? "—" : string.Join(", ", step.Inputs.Select(input => input.Key));
        string outputs = step.Outputs.Count == 0 ? "—" : string.Join(", ", step.Outputs.Select(output => output.Key));

        return $"in  {inputs}\nout {outputs}";
    }


    private Brush BrushFor(StepNode step)
    {
        if (step.IsWaitingAtBreakpoint)
            return (Brush)resources.FindResource(ThemeKeys.StatePaused);

        return step.Lifecycle switch
        {
            DebugLifecycleState.Running => (Brush)resources.FindResource(ThemeKeys.StateRunning),
            DebugLifecycleState.Complete => (Brush)resources.FindResource(ThemeKeys.StateComplete),
            DebugLifecycleState.Error => (Brush)resources.FindResource(ThemeKeys.StateError),
            DebugLifecycleState.Timeout => (Brush)resources.FindResource(ThemeKeys.StateTimeout),
            DebugLifecycleState.Skipped => (Brush)resources.FindResource(ThemeKeys.StateSkipped),
            _ => (Brush)resources.FindResource(ThemeKeys.StateNotRun)
        };
    }


    /// <summary>Makes a breakpoint marker read as set or as an invitation to set one.</summary>
    private void ShowBreakpoint(Border marker, bool isSet)
    {
        marker.Background = isSet ? (Brush)resources.FindResource(ThemeKeys.StateError) : Brushes.Transparent;
        marker.BorderBrush = isSet ? Brushes.Transparent : (Brush)resources.FindResource(ThemeKeys.TextSecondary);
        marker.Opacity = isSet ? 1 : BoardMetrics.RestingMarkerOpacity;
    }

    /// <summary>
    /// Puts the last picture a step took on its card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last one rather than all of them: a card has room for an affordance, and what it is saying
    /// is "this step saw something, and it looked like this". The whole sequence is a click away in
    /// the step panel.
    /// </para>
    /// <para>
    /// Assigned here rather than built with the card, because a widget arrives long after the board
    /// was laid out and changes no geometry — so it repaints rather than rebuilding, which is the
    /// same rule everything else on a card follows.
    /// </para>
    /// </remarks>
    private void ShowWidget(Border target, RunGraph graph, string stageName, StepNode step)
    {
        WidgetNode? last = null;

        foreach (WidgetNode widget in graph.Widgets)
        {
            if (widget.BelongsTo(stageName, step.StepId))
                last = widget;
        }

        // Rebuilt only when the step is showing something new. The graph is replaced on every event a
        // live run produces, and re-decoding a picture per log line would make the board cost more
        // than the run it is drawing.
        string signature = last?.Description.Body?.ContentHash ?? last?.Name ?? string.Empty;

        if (string.Equals(signature, target.Tag as string, StringComparison.Ordinal))
            return;

        target.Tag = signature;

        if (last is null)
        {
            target.Child = null;
            target.Visibility = Visibility.Collapsed;
            return;
        }

        target.Height = BoardMetrics.WidgetHeight;
        target.Visibility = Visibility.Visible;
        target.Child = WidgetFace(last);
    }

    /// <summary>
    /// What a widget looks like inside a card.
    /// </summary>
    /// <remarks>
    /// The same two renderers the step panel uses, without its frame: on a board the card already is
    /// the frame — it carries the name, the state, the timing and the connectors — so drawing a second
    /// one inside it would be a box in a box saying the same thing twice.
    /// </remarks>
    private UIElement WidgetFace(WidgetNode widget)
    {
        // A card face at a card's width. What counts as a picture, and what a widget's readable
        // content is, are WidgetFaces' answers rather than this control's - the panel asks the same
        // two questions of the same widget.
        if (WidgetFaces.IsPicture(widget) && WidgetFaces.PictureOf(widget, (int)LayoutOptions.Default.StepWidth) is { } picture)
            return WidgetFaces.Draw(picture);

        // The summary is the fallback for both a form with nothing to read and a picture whose file
        // is gone: on a card there is no room to explain, and a name is better than a blank.
        return new TextBlock
        {
            Foreground = (Brush)resources.FindResource(ThemeKeys.TextFaint),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 11,
            Margin = new Thickness(10, 8, 10, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = WidgetFaces.TextOf(widget) ?? widget.Description.Summary
        };
    }
}
