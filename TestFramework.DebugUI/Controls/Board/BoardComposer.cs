using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Layout;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// Turns the geometry of a run into the things on screen.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here reads the run's state. It is handed an arrangement - which is a pure function of the
/// run - and it builds a card, a band, a connector or a pipe for every part of it. What each of those
/// then says about the run is <see cref="BoardPainter"/>'s, and keeping the two apart is what lets a
/// step change colour twenty times a second without the visual tree being thrown away.
/// </para>
/// <para>
/// The order things are added in is load-bearing: stage bands first, then pipes, then the boxes, then
/// the connectors on top of them. A connector has to sit over the edge of its box, and a pipe has to
/// run behind both.
/// </para>
/// </remarks>
internal sealed class BoardComposer
{
    private readonly FrameworkElement resources;
    private readonly Canvas canvas;
    private readonly Func<BreakpointService?> breakpoints;
    private readonly Action<string, int> stepSelected;
    private readonly Action summaryRequested;

    /// <summary>Which checks failed, for the run being composed.</summary>
    private IReadOnlySet<string> brokenChecks = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Composes onto one canvas.</summary>
    /// <param name="resources">Where brushes and geometry are found; the board itself.</param>
    /// <param name="canvas">What is drawn into.</param>
    /// <param name="breakpoints">The marks, asked for rather than held: the window supplies them later.</param>
    /// <param name="stepSelected">What a click on a card means.</param>
    /// <param name="summaryRequested">What a click on the verdict means.</param>
    public BoardComposer(
        FrameworkElement resources,
        Canvas canvas,
        Func<BreakpointService?> breakpoints,
        Action<string, int> stepSelected,
        Action summaryRequested)
    {
        this.resources = resources;
        this.canvas = canvas;
        this.breakpoints = breakpoints;
        this.stepSelected = stepSelected;
        this.summaryRequested = summaryRequested;
    }

    /// <summary>Draws a whole board, and hands back what it drew.</summary>
    /// <param name="board">The geometry, which is the only thing that decides what goes where.</param>
    /// <param name="brokenChecks">Which checks failed, so a pipe can be coloured by its own outcome.</param>
    /// <returns>Everything that can later be repainted without being rebuilt.</returns>
    public BoardVisuals Compose(LayoutResult board, IReadOnlySet<string> brokenChecks)
    {
        BoardVisuals visuals = new();

        this.brokenChecks = brokenChecks;

        canvas.Children.Clear();

        canvas.Width = board.Width;
        canvas.Height = board.Height;

        // Stage bands first, then pipes, then the boxes, then the connectors on top of them: a
        // connector has to sit over the edge of its box, and a pipe has to run behind both.
        foreach (LayoutNode node in board.Nodes.Where(node => node.Kind == LayoutNodeKind.Stage))
            Place(BuildStage(node), node.X, node.Y);

        foreach (LayoutEdge edge in board.Edges)
        {
            foreach (UIElement stroke in BuildPipe(visuals, edge))
                canvas.Children.Add(stroke);
        }

        foreach (LayoutNode node in board.Nodes.Where(node => node.Kind != LayoutNodeKind.Stage))
        {
            UIElement visual = node.Kind == LayoutNodeKind.Verdict ? BuildVerdict(visuals, board, node) : BuildStep(visuals, board, node);

            Place(visual, node.X, node.Y);
        }

        foreach (LayoutPort port in board.Ports)
            Place(BuildConnector(port), port.X - (BoardMetrics.ConnectorSize / 2), port.Y - (BoardMetrics.ConnectorSize / 2));

        return visuals;
    }

    private void Place(UIElement visual, double x, double y)
    {
        Canvas.SetLeft(visual, x);
        Canvas.SetTop(visual, y);
        canvas.Children.Add(visual);
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
            Foreground = (Brush)resources.FindResource("TextFaint"),
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
    private UIElement BuildVerdict(BoardVisuals visuals, LayoutResult board, LayoutNode node)
    {
        TextBlock heading = new()
        {
            Foreground = (Brush)resources.FindResource("TextPrimary"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock why = new()
        {
            Foreground = (Brush)resources.FindResource("TextSecondary"),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        Border box = new()
        {
            Width = node.Width,
            Height = node.Height,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)resources.FindResource("SurfaceCard"),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Open the run's summary.",
            Child = Plated(board, node, new StackPanel
            {
                Margin = new Thickness(16, 6, 16, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { heading, why }
            })
        };

        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            summaryRequested();
        };

        visuals.Verdict = new VerdictVisual(box, heading, why);
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
    /// <remarks>
    /// Internal rather than private so a card can be built and measured in a test. What goes wrong in
    /// here goes wrong geometrically — two things laid out over each other — and that is invisible to
    /// every assertion that does not lay the card out.
    /// </remarks>
    internal UIElement BuildStep(BoardVisuals visuals, LayoutResult board, LayoutNode node)
    {
        Border status = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = (Brush)resources.FindResource("StateNotRun") };
        TextBlock name = new() { Foreground = (Brush)resources.FindResource("TextPrimary"), FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        TextBlock note = new() { Foreground = (Brush)resources.FindResource("TextSecondary"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        TextBlock outputs = new() { Foreground = (Brush)resources.FindResource("TextFaint"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };


        // The heading's right-hand end is the emptiest part of a card and the timing is what a reader
        // scans down a column for, so it goes there rather than at the end of the status line where
        // it competes with the state and the attempt count for the same eye.
        TextBlock elapsed = new()
        {
            Foreground = (Brush)resources.FindResource("TextFaint"),
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
                new ColumnDefinition { Width = GridLength.Auto },

                // The marker's corner, left empty. It is the only honest way to keep two things apart
                // when one of them is not in this grid to be laid out against.
                new ColumnDefinition { Width = new GridLength(BoardMetrics.MarkerColumn) }
            }
        };

        Grid.SetColumn(name, 1);
        Grid.SetColumn(elapsed, 2);

        heading.Children.Add(status);
        heading.Children.Add(name);
        heading.Children.Add(elapsed);

        // Where the step shows what it did. Empty and collapsed on a board whose steps drew nothing,
        // so an ordinary run keeps the short card; on a board that asked for room, this is the part
        // of the card a reader actually looks at and the writing above it is the caption.
        Border widget = new()
        {
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = (Brush)resources.FindResource("SurfaceSunken"),
            Margin = new Thickness(0, BoardMetrics.WidgetMargin, 0, 0),
            Visibility = Visibility.Collapsed
        };

        StackPanel body = new()
        {
            Margin = new Thickness(BoardMetrics.CardPadding, 6, BoardMetrics.CardPadding, 6),

            // Centred on a short card, where the writing is all there is; anchored to the top on a
            // card sized for a widget, so a step that drew nothing reads as a card with room to
            // spare rather than as a line of text floating in the middle of one.
            VerticalAlignment = node.Height > LayoutOptions.Default.StepHeight
                ? VerticalAlignment.Top
                : VerticalAlignment.Center,
            Children = { heading, note, widget, outputs }
        };

        Border box = new()
        {
            CornerRadius = new CornerRadius(4),
            Background = (Brush)resources.FindResource("SurfaceCard"),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = Plated(board, node, body)
        };

        string stageName = node.StageName;
        int stepId = node.StepId ?? 0;

        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            stepSelected(stageName, stepId);
        };

        // Right-click still sets a breakpoint anywhere on the card. The marker is the discoverable way
        // in; this is the fast one, and it costs nothing to keep both.
        box.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            breakpoints()?.Toggle(stageName, stepId);
        };

        Border breakpoint = BuildBreakpointMarker(stageName, stepId);

        // The marker sits beside the card rather than inside it, because a card that has not run yet is
        // dimmed to four-tenths — and a breakpoint set on a step that has not run is precisely the case
        // worth being able to see. Dimming is applied to the box; the marker is not in it.
        Grid host = new() { Width = node.Width, Height = node.Height };

        host.Children.Add(box);
        host.Children.Add(breakpoint);

        visuals.Steps[node.Id] = new StepVisual(box, status, name, note, outputs, elapsed, breakpoint, widget);
        return host;
    }

    /// <summary>
    /// Builds the breakpoint marker for one step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In the card's top-right corner, filled red when set, and a faint outline when not — the outline is
    /// what makes the spot discoverable at all. Before this, a breakpoint was an amber card border set by a
    /// right-click nobody would guess at, which made the tool's most useful feature its least findable.
    /// </para>
    /// <para>
    /// A rounded rectangle rather than a circle. Every other round mark on this board is a connector, and a
    /// red circle in the corner of a card read as one more port; a stub of a rectangle reads as a marker
    /// laid on the card. It is also the only way to set a breakpoint from the board now — the panel's
    /// "Toggle breakpoint" button said the same thing in words, a long way from the step it applied to.
    /// </para>
    /// </remarks>
    private Border BuildBreakpointMarker(string stageName, int stepId)
    {
        Border marker = new()
        {
            Width = BoardMetrics.MarkerWidth,
            Height = 11,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, BoardMetrics.MarkerInset, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Stop the run here."
        };

        marker.MouseLeftButtonUp += (_, e) =>
        {
            // Handled, so toggling a breakpoint does not also select the step underneath. The surface
            // ends its pan on the tunnelling event, so marking this one handled cannot strand it.
            e.Handled = true;
            breakpoints()?.Toggle(stageName, stepId);
        };

        marker.MouseEnter += (_, _) => marker.Opacity = 1;
        marker.MouseLeave += (_, _) => marker.Opacity = breakpoints()?.IsSet(stageName, stepId) == true ? 1 : BoardMetrics.RestingMarkerOpacity;

        return marker;
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
    private UIElement Plated(LayoutResult board, LayoutNode node, UIElement body)
    {
        bool receives = board.Ports.Any(port => port.NodeId == node.Id && port.IsInput);
        bool sends = board.Ports.Any(port => port.NodeId == node.Id && !port.IsInput);

        Grid card = new();
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(receives ? BoardMetrics.StripHeight : 0) });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(sends ? BoardMetrics.StripHeight : 0) });

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
        Background = (Brush)resources.FindResource("SurfaceSunken"),
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
            ? (Brush)resources.FindResource(brokenChecks.Contains(port.Key) ? "StateError" : "StateComplete")
            : (Brush)resources.FindResource(port.Kind == DebugValueKind.Artifact ? "FlowArtifact" : "FlowVariable");

        Border ring = new()
        {
            Width = BoardMetrics.ConnectorSize,
            Height = BoardMetrics.ConnectorSize,
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
    private IEnumerable<UIElement> BuildPipe(BoardVisuals visuals, LayoutEdge edge)
    {
        PipeFigure figure = PipeGeometry.Fillet(edge.Points, BoardMetrics.CornerRadius);

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
            ? (Brush)resources.FindResource(brokenChecks.Contains(edge.Key) ? "StateError" : "StateComplete")
            : (Brush)resources.FindResource(edge.ValueKind == DebugValueKind.Artifact ? "FlowArtifact" : "FlowVariable");

        yield return new Path
        {
            Stroke = (Brush)resources.FindResource("PipeShadow"),
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
        visuals.Pipes[edge.Id] = stroke;

        yield return stroke;
    }
}
