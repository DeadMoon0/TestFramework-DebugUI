using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Layout;
using TestFramework.DebugUI.State;


namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// The run drawn as a board.
/// </summary>
/// <remarks>
/// <para>
/// Geometry comes from <see cref="RunBoardLayout"/> and is recomputed from the run whenever it
/// changes; nothing positional is stored. The board is only rebuilt when that geometry actually
/// differs — a step going from running to complete moves nothing, so it repaints rather than
/// relaying out, which is what keeps a busy run from flickering.
/// </para>
/// <para>
/// What this replaces drew a fixed row of hardcoded pipe segments on a 500-pixel canvas.
/// </para>
/// </remarks>
public partial class UC_Board : UserControl
{
    private const double MinimumZoom = 0.2;
    private const double MaximumZoom = 3.0;

    /// <summary>How far Fit is allowed to magnify a board smaller than the window.</summary>
    /// <remarks>
    /// Fit used to stop at 1.0, so a four-step run sat at its drawn size in the middle of a large
    /// screen and used about a quarter of it. Letting it grow uses the space that is there. Capped
    /// well below <see cref="MaximumZoom"/> because past roughly double, a short run stops looking
    /// fitted and starts looking magnified.
    /// </remarks>
    private const double MaximumFitZoom = 2.0;
    private const double CornerRadius = 10;

    /// <summary>How much clear space a fitted board keeps between itself and the window edge.</summary>
    private const double FitMargin = 24;

    /// <summary>The diameter of a connector, matching the ring the pipes were always drawn with.</summary>
    private const double ConnectorSize = 20;

    /// <summary>How far a press has to travel before it pans the board instead of selecting.</summary>
    private const double DragThreshold = 4;

    /// <summary>How deep the recessed strip a connector sits on runs into the card.</summary>
    private const double StripHeight = 16;

    private readonly CompositeDisposable subscriptions = [];
    private readonly Dictionary<string, StepVisual> stepVisuals = new(StringComparer.Ordinal);

    /// <summary>The coloured stroke of each pipe, kept so it can dim or light as its value appears.</summary>
    private readonly Dictionary<string, Path> pipeVisuals = new(StringComparer.Ordinal);

    private LayoutResult board = LayoutResult.Empty;
    private HashSet<string> brokenChecks = new(StringComparer.Ordinal);
    private VerdictVisual? verdictVisual;
    private Point panOrigin;
    private bool panning;

    /// <summary>Set once a press has moved far enough to be a drag rather than a click.</summary>
    private bool dragging;

    /// <summary>Set when a board is waiting for the surface to have a size it can be fitted to.</summary>
    private bool needsFit;

    /// <summary>Creates the board and binds it.</summary>
    public UC_Board()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun)
            .Subscribe(Render));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.SelectedStep)
            .Subscribe(_ => RefreshAppearance()));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Stages.Count == 0)
            .Select(empty => empty ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        // The surface is measured after the run arrives, so this is what actually fits the first
        // board; the attempt in Render is just the case where a size already exists.
        bSurface.SizeChanged += (_, _) =>
        {
            if (needsFit)
                Fit();
        };

        Breakpoints.Changed += RefreshAppearance;
        Unloaded += (_, _) =>
        {
            Breakpoints.Changed -= RefreshAppearance;
            subscriptions.Dispose();
        };
    }

    private void Render(RunGraph graph)
    {
        LayoutResult next = RunBoardLayout.Compute(graph);

        // Which checks broke, so a pipe into the verdict can be coloured by its own outcome rather
        // than by the run's. One failed check among five should show as one red pipe, not five.
        brokenChecks = [.. graph.Assertions.Where(assertion => !assertion.Succeeded).Select(assertion => assertion.Target)];

        // Value equality on the result is what makes this cheap: most events change a step's state
        // without moving anything, and rebuilding the canvas for those would throw away the whole
        // visual tree twenty times a second.
        bool firstBoard = board.Nodes.Count == 0 && next.Nodes.Count > 0;

        if (next != board)
        {
            board = next;
            Rebuild();
        }

        // Fitted once, when a run first appears, and never again — refitting on every change would
        // drag the board out from under someone who had zoomed in on a step to read it.
        //
        // Queued rather than called: the run usually arrives before the surface has been measured,
        // and fitting to a size of zero silently does nothing at all.
        if (firstBoard)
        {
            needsFit = true;
            Fit();
        }

        RefreshAppearance();
    }

    /// <summary>
    /// Scales the board so all of it is on screen, and centres it.
    /// </summary>
    /// <remarks>
    /// A run is far wider and taller than any window, so opening at full size shows two steps and no
    /// context. Never scales past 1:1 — a two-step run blown up to fill the window looks broken.
    /// </remarks>
    private void Fit()
    {
        double viewWidth = bSurface.ActualWidth;
        double viewHeight = bSurface.ActualHeight;

        // A run almost always arrives before the surface has been measured, so the first attempt has
        // nothing to fit to. Leaving the flag set means the size-changed handler finishes the job
        // the moment there is a size — rather than silently doing nothing and leaving the board at
        // full scale, showing two steps.
        if (viewWidth <= 0 || viewHeight <= 0 || board.Width <= 0 || board.Height <= 0)
            return;

        needsFit = false;

        // Measured against the window less a margin, so a fitted board has room to breathe instead
        // of touching all four edges.
        double scale = Math.Clamp(
            Math.Min((viewWidth - (FitMargin * 2)) / board.Width, (viewHeight - (FitMargin * 2)) / board.Height),
            MinimumZoom,
            MaximumFitZoom);

        stZoom.ScaleX = scale;
        stZoom.ScaleY = scale;

        ttPan.X = (viewWidth - (board.Width * scale)) / 2;
        ttPan.Y = (viewHeight - (board.Height * scale)) / 2;
    }

    /// <summary>How strongly something that has not happened yet is drawn.</summary>
    /// <remarks>
    /// Low enough to fall back behind the run, high enough to still be readable — the declared shape
    /// of a timeline is worth seeing, it just should not compete with what is actually happening.
    /// </remarks>
    private const double DormantOpacity = 0.4;

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
        foreach (LayoutEdge edge in board.Edges)
        {
            if (!pipeVisuals.TryGetValue(edge.Id, out Path? stroke))
                continue;

            bool carried = edge.ValueKind == DebugValueKind.Artifact
                ? graph.Artifacts.ContainsKey(edge.Key)
                : graph.Variables.ContainsKey(edge.Key);

            stroke.Opacity = carried ? 1 : DormantOpacity;
        }
    }

    private void Rebuild()
    {
        cBoard.Children.Clear();
        stepVisuals.Clear();
        pipeVisuals.Clear();
        verdictVisual = null;

        cBoard.Width = board.Width;
        cBoard.Height = board.Height;

        // Stage bands first, then pipes, then the boxes, then the connectors on top of them: a
        // connector has to sit over the edge of its box, and a pipe has to run behind both.
        foreach (LayoutNode node in board.Nodes.Where(node => node.Kind == LayoutNodeKind.Stage))
            Place(BuildStage(node), node.X, node.Y);

        foreach (LayoutEdge edge in board.Edges)
        {
            foreach (UIElement stroke in BuildPipe(edge))
                cBoard.Children.Add(stroke);
        }

        foreach (LayoutNode node in board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage))
        {
            UIElement visual = node.Kind == LayoutNodeKind.Verdict ? BuildVerdict(node) : BuildStep(node);

            Place(visual, node.X, node.Y);
        }

        foreach (LayoutPort port in board.Ports)
            Place(BuildConnector(port), port.X - (ConnectorSize / 2), port.Y - (ConnectorSize / 2));
    }

    private void Place(UIElement visual, double x, double y)
    {
        Canvas.SetLeft(visual, x);
        Canvas.SetTop(visual, y);
        cBoard.Children.Add(visual);
    }

    /// <summary>
    /// Builds a stage band: a faint enclosure, not a panel.
    /// </summary>
    /// <remarks>
    /// A stage is context, so it stays at the edge of visibility. Drawn any stronger it competes
    /// with the steps inside it, and the steps are the thing being read.
    /// </remarks>
    private UIElement BuildStage(LayoutNode node) => new Border
    {
        Width = node.Width,
        Height = node.Height,
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
        Child = new TextBlock
        {
            Text = node.StageName.ToUpperInvariant(),
            Foreground = (Brush)FindResource("TextFaint"),
            Opacity = 0.55,
            FontSize = 10,
            Margin = new Thickness(12, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        }
    };

    /// <summary>
    /// Builds the verdict: the box every asserted value flows into.
    /// </summary>
    /// <remarks>
    /// The one card on the board that is not a thing the run did. It is the conclusion drawn from the
    /// pipes arriving at it, which is why it is drawn as their destination rather than as a panel
    /// somewhere else — "why is this run valid" becomes something the eye can follow.
    /// </remarks>
    private UIElement BuildVerdict(LayoutNode node)
    {
        TextBlock heading = new()
        {
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock why = new()
        {
            Foreground = (Brush)FindResource("TextSecondary"),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        Border box = new()
        {
            Width = node.Width,
            Height = node.Height,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("SurfaceCard"),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Open the run's summary.",
            Child = Plated(node, new StackPanel
            {
                Margin = new Thickness(16, 6, 16, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { heading, why }
            })
        };

        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SummaryRequested?.Invoke();
        };

        verdictVisual = new VerdictVisual(box, heading, why);
        return box;
    }

    /// <summary>
    /// Builds a step as a card with a recessed connector strip at each end.
    /// </summary>
    /// <remarks>
    /// Big enough to hold what a reader actually wants — the name, what became of it, how long it
    /// took, what it produced — because a board of small boxes tells you only that things happened
    /// in an order, which is the one thing you already knew.
    /// </remarks>
    private UIElement BuildStep(LayoutNode node)
    {
        Border status = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = (Brush)FindResource("StateNotRun") };
        TextBlock name = new() { Foreground = (Brush)FindResource("TextPrimary"), FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        TextBlock note = new() { Foreground = (Brush)FindResource("TextSecondary"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        TextBlock outputs = new() { Foreground = (Brush)FindResource("TextFaint"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        // The last log line lives in the panel on the right, not on the card. It was the single
        // biggest consumer of card height and the least likely thing to be read at board scale.
        TextBlock log = new();

        // The heading's right-hand end is the emptiest part of a card and the timing is what a reader
        // scans down a column for, so it goes there rather than at the end of the status line where
        // it competes with the state and the attempt count for the same eye.
        TextBlock elapsed = new()
        {
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Grid heading = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        Grid.SetColumn(name, 1);
        Grid.SetColumn(elapsed, 2);

        heading.Children.Add(status);
        heading.Children.Add(name);
        heading.Children.Add(elapsed);

        StackPanel body = new()
        {
            Margin = new Thickness(16, 6, 16, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { heading, note, outputs }
        };

        Border box = new()
        {
            Width = node.Width,
            Height = node.Height,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("SurfaceCard"),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = Plated(node, body)
        };

        string stageName = node.StageName;
        int stepId = node.StepId ?? 0;

        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MainWindow.Shell.SelectStep(stageName, stepId);
        };

        // Right-click sets a breakpoint. Kept off the left button so inspecting a step — by far the
        // commoner action — never accidentally changes what the run will do.
        box.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Breakpoints.Toggle(stageName, stepId);
        };

        stepVisuals[node.Id] = new StepVisual(box, status, name, note, outputs, log, elapsed);
        return box;
    }

    /// <summary>
    /// Seats a card's content between the darker strips its connectors sit on.
    /// </summary>
    /// <remarks>
    /// The connectors are drawn straddling the card's edge, and without something behind them they
    /// float on the same flat surface as the text. A recessed strip gives them a socket to sit in,
    /// which is what makes a card read as something you plug into rather than as a rectangle with
    /// dots on it. A strip is drawn only at an edge that actually has connectors: an empty one would
    /// promise a socket that is not there.
    /// </remarks>
    private UIElement Plated(LayoutNode node, UIElement body)
    {
        bool receives = board.Ports.Any(port => port.NodeId == node.Id && port.IsInput);
        bool sends = board.Ports.Any(port => port.NodeId == node.Id && !port.IsInput);

        Grid card = new();
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(receives ? StripHeight : 0) });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(sends ? StripHeight : 0) });

        if (receives)
            card.Children.Add(Strip(top: true));

        Grid.SetRow(body, 1);
        card.Children.Add(body);

        if (sends)
        {
            UIElement strip = Strip(top: false);
            Grid.SetRow(strip, 2);
            card.Children.Add(strip);
        }

        return card;
    }

    private UIElement Strip(bool top) => new Border
    {
        Background = (Brush)FindResource("SurfaceSunken"),
        CornerRadius = top ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0, 0, 4, 4)
    };

    /// <summary>
    /// Draws a connector: a ring where a value arrives, filled where one leaves.
    /// </summary>
    /// <remarks>
    /// Hollow for receive and solid for send, so the direction of a connection is readable from
    /// either end of it without following the pipe. An unconnected input is dimmed rather than
    /// hidden — the step asked for something nothing supplies, which is worth seeing.
    /// </remarks>
    private UIElement BuildConnector(LayoutPort port)
    {
        // A connector on the verdict is coloured like the pipe arriving at it. It is the same claim
        // at both ends, and a green socket at the end of a red pipe reads as two different things.
        Brush flow = string.Equals(port.NodeId, "verdict", StringComparison.Ordinal)
            ? (Brush)FindResource(brokenChecks.Contains(port.Key) ? "StateError" : "StateComplete")
            : (Brush)FindResource(port.Kind == DebugValueKind.Artifact ? "FlowArtifact" : "FlowVariable");

        Border ring = new()
        {
            Width = ConnectorSize,
            Height = ConnectorSize,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(5),
            BorderBrush = flow,
            Background = port.IsInput ? Brushes.Transparent : flow,
            Opacity = port.IsConnected ? 1 : 0.35,
            ToolTip = $"{port.Key} ({(port.IsInput ? "in" : "out")})"
        };

        return ring;
    }

    /// <summary>
    /// Draws a pipe as a bright core with a soft edge either side.
    /// </summary>
    /// <remarks>
    /// Two strokes rather than one: the wide translucent pass underneath is what lifts the pipe off
    /// the board and keeps it legible where it runs close to another. The colour says what is
    /// flowing — a variable or an artifact — which is the fastest way to read a busy channel.
    /// </remarks>
    private IEnumerable<UIElement> BuildPipe(LayoutEdge edge)
    {
        PipeFigure figure = PipeGeometry.Fillet(edge.Points, CornerRadius);

        PathFigure path = new() { StartPoint = new Point(figure.Start.X, figure.Start.Y), IsClosed = false, IsFilled = false };

        foreach (PipeSegment segment in figure.Segments)
        {
            Point to = new(segment.To.X, segment.To.Y);

            path.Segments.Add(segment.IsCorner
                ? new ArcSegment(to, new Size(segment.Radius, segment.Radius), 0, false,
                    segment.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true)
                : new LineSegment(to, true));
        }

        PathGeometry geometry = new() { Figures = { path } };

        // A pipe into the verdict is carrying a claim, not a value, so it is coloured by whether the
        // claim held. Colouring it by variable-or-artifact there would waste the only place on the
        // board where pass and fail can be seen without reading anything.
        Brush flow = edge.Kind == LayoutEdgeKind.Assertion
            ? (Brush)FindResource(brokenChecks.Contains(edge.Key) ? "StateError" : "StateComplete")
            : (Brush)FindResource(edge.ValueKind == DebugValueKind.Artifact ? "FlowArtifact" : "FlowVariable");

        yield return new Path
        {
            Stroke = (Brush)FindResource("PipeShadow"),
            StrokeThickness = 10,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = geometry
        };

        Path stroke = new()
        {
            Stroke = flow,
            StrokeThickness = 4,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = geometry,
            ToolTip = $"{edge.Key} ({edge.ValueKind.ToString().ToLowerInvariant()})"
        };

        // Kept so it can be dimmed until its value exists. A pipe drawn at full strength before
        // anything has flowed through it claims a flow that has not happened, and on a board where
        // most pipes are still waiting that is most of what the eye sees.
        pipeVisuals[edge.Id] = stroke;

        yield return stroke;
    }

    /// <summary>
    /// Repaints what the geometry does not carry: lifecycle, selection, breakpoints, attempts.
    /// </summary>
    private void RefreshAppearance()
    {
        if (stepVisuals.Count == 0)
            return;

        RunGraph graph = StateStore<MainState>.Default.GetValue(state => state.ActiveRun);
        StepSelection? selected = StateStore<MainState>.Default.GetValue(state => state.SelectedStep);

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                if (!stepVisuals.TryGetValue($"step:{stage.Name}/{step.StepId}", out StepVisual? visual))
                    continue;

                visual.Name.Text = step.DisplayName;
                visual.Note.Text = Note(step);
                visual.Elapsed.Text = step.Duration is { } duration ? Elapsed(duration) : string.Empty;
                visual.Status.Background = BrushFor(step);
                visual.Outputs.Text = Describe(step);
                visual.Log.Text = LastLine(step);

                bool isSelected = selected is not null
                                  && selected.StepId == step.StepId
                                  && string.Equals(selected.StageName, stage.Name, StringComparison.Ordinal);

                // A step that has not run yet is context, not content. Dimming it lets the eye find
                // what is happening now without hunting through everything that is merely declared.
                visual.Box.Opacity = HasRun(step) ? 1 : DormantOpacity;

                visual.Box.BorderBrush = isSelected
                    ? (Brush)FindResource("AccentSelection")
                    : Breakpoints.IsSet(stage.Name, step.StepId)
                        ? (Brush)FindResource("StatePaused")
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
        if (verdictVisual is null)
            return;

        RunTally tally = RunTally.Of(graph);
        bool decided = tally.IsFinished || tally.Failed > 0 || tally.AssertionsFailed > 0;

        verdictVisual.Heading.Text = !decided ? "Checking" : tally.IsValid ? "Valid" : "Not valid";

        verdictVisual.Why.Text = !decided
            ? $"{tally.AssertionsPassed} check(s) held so far."
            : tally.AssertionsFailed > 0
                ? $"{tally.AssertionsFailed} of {tally.AssertionsPassed + tally.AssertionsFailed} check(s) did not hold."
                : tally.Failed > 0
                    ? $"Every check held, but {tally.Failed} step(s) failed."
                    : $"All {tally.AssertionsPassed} check(s) held.";

        verdictVisual.Box.BorderBrush = (Brush)FindResource(
            !decided ? "StateRunning" : tally.IsValid ? "StateComplete" : "StateError");
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
    /// A duration at a scale a reader can compare at a glance.
    /// </summary>
    /// <remarks>
    /// Whole units, and never more than three significant figures: the board is scanned for the step
    /// that stands out, and 1.4 s against 12 ms says that immediately where 1402.318 ms does not.
    /// </remarks>
    private static string Elapsed(TimeSpan duration) => duration.TotalMilliseconds switch
    {
        // Rounding a sub-millisecond step to "0 ms" reads as a step that did not run. Saying it was
        // under a millisecond says the true thing: too fast to have a number worth comparing.
        < 1 => "<1 ms",
        < 1000 => $"{duration.TotalMilliseconds:0} ms",
        < 60_000 => $"{duration.TotalSeconds:0.#} s",
        _ => $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s"
    };

    /// <summary>What the step declared it takes and gives, which is the shape of its connectors.</summary>
    private static string Describe(StepNode step)
    {
        string inputs = step.Inputs.Count == 0 ? "—" : string.Join(", ", step.Inputs.Select(input => input.Key));
        string outputs = step.Outputs.Count == 0 ? "—" : string.Join(", ", step.Outputs.Select(output => output.Key));

        return $"in  {inputs}\nout {outputs}";
    }

    /// <summary>
    /// The most recent thing the step said.
    /// </summary>
    /// <remarks>
    /// One line, not the log: the card is a summary, and the panel on the right is where the whole
    /// log lives. The last line is the useful one — it is what the step was doing when it stopped.
    /// </remarks>
    private static string LastLine(StepNode step)
    {
        LogNode? last = step.Attempts.LastOrDefault()?.Logs.LastOrDefault();
        return last is null ? "> …" : "> " + last.Message;
    }

    private Brush BrushFor(StepNode step)
    {
        if (step.IsWaitingAtBreakpoint)
            return (Brush)FindResource("StatePaused");

        return step.Lifecycle switch
        {
            DebugLifecycleState.Running => (Brush)FindResource("StateRunning"),
            DebugLifecycleState.Complete => (Brush)FindResource("StateComplete"),
            DebugLifecycleState.Error => (Brush)FindResource("StateError"),
            DebugLifecycleState.Timeout => (Brush)FindResource("StateTimeout"),
            DebugLifecycleState.Skipped => (Brush)FindResource("StateSkipped"),
            _ => (Brush)FindResource("StateNotRun")
        };
    }

    /// <summary>
    /// Raised when the reader asks for the run's summary by clicking the verdict drawn on the board.
    /// </summary>
    public event Action? SummaryRequested;

    /// <summary>Asks for the summary, as clicking the verdict does.</summary>
    /// <remarks>
    /// These three exist so the title bar and the keyboard shortcuts run the same code as each other
    /// rather than their own copies of it. Two paths to one action is how the two stop agreeing.
    /// </remarks>
    public void RequestSummary() => SummaryRequested?.Invoke();

    /// <summary>Scales and centres the board so all of it is on screen.</summary>
    public void FitToWindow() => Fit();

    /// <summary>Selects the first failed step and pans to it.</summary>
    public void GoToFirstFailure()
    {
        RunGraph graph = StateStore<MainState>.Default.GetValue(state => state.ActiveRun);

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                if (step.Lifecycle is DebugLifecycleState.Error or DebugLifecycleState.Timeout)
                {
                    MainWindow.Shell.SelectStep(stage.Name, step.StepId);
                    BringIntoView(stage.Name, step.StepId);
                    return;
                }
            }
        }
    }

    /// <summary>Pans so a step is in the middle of the surface.</summary>
    private void BringIntoView(string stageName, int stepId)
    {
        if (!stepVisuals.TryGetValue($"step:{stageName}/{stepId}", out StepVisual? visual))
            return;

        LayoutNode? node = board.Nodes.FirstOrDefault(candidate =>
            candidate.Kind == LayoutNodeKind.Step && candidate.StageName == stageName && candidate.StepId == stepId);

        if (node is null)
            return;

        ttPan.X = (ActualWidth / 2) - (node.CentreX * stZoom.ScaleX);
        ttPan.Y = (ActualHeight / 2) - (node.CentreY * stZoom.ScaleY);
    }

    private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        double zoom = Math.Clamp(stZoom.ScaleX * factor, MinimumZoom, MaximumZoom);

        // Anchored on the pointer, so zooming keeps whatever is under the cursor under the cursor.
        Point pointer = e.GetPosition(cBoard);

        ttPan.X -= pointer.X * (zoom - stZoom.ScaleX);
        ttPan.Y -= pointer.Y * (zoom - stZoom.ScaleY);

        stZoom.ScaleX = zoom;
        stZoom.ScaleY = zoom;

        e.Handled = true;
    }

    /// <summary>
    /// Presses the button without taking the mouse, so a click still reaches what was clicked.
    /// </summary>
    /// <remarks>
    /// Capturing here is what made a step impossible to select. The press bubbles up from the step to
    /// the surface, and once the surface holds the mouse every later event is routed to it — so the
    /// release never reached the step and nothing was ever picked. The capture is taken on the first
    /// real movement instead, which is the moment it is actually needed.
    /// </remarks>
    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        panOrigin = e.GetPosition(this);
        panning = true;
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        panning = false;

        if (dragging)
        {
            dragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!panning)
            return;

        Point now = e.GetPosition(this);

        // Below the threshold this is a click that wobbled, not a drag. Panning from here would move
        // the board a pixel and swallow the selection, which reads as the board ignoring the click.
        if (!dragging)
        {
            if (Math.Abs(now.X - panOrigin.X) < DragThreshold && Math.Abs(now.Y - panOrigin.Y) < DragThreshold)
                return;

            dragging = true;
            ((UIElement)sender).CaptureMouse();
            panOrigin = now;
        }

        ttPan.X += now.X - panOrigin.X;
        ttPan.Y += now.Y - panOrigin.Y;

        panOrigin = now;
    }

    private sealed record StepVisual(Border Box, Border Status, TextBlock Name, TextBlock Note, TextBlock Outputs, TextBlock Log, TextBlock Elapsed);

    private sealed record VerdictVisual(Border Box, TextBlock Heading, TextBlock Why);
}
