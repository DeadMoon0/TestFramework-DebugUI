using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Controls.Annotate;
using TestFramework.DebugUI.Controls.Detail;
using TestFramework.DebugUI.Layout;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Annotations;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;
using TestFramework.DebugUI.State.Runs;


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

    /// <summary>The gap between a card's writing and the picture under it.</summary>
    private const double WidgetMargin = 8;

    /// <summary>How tall the widget area inside a card is drawn.</summary>
    /// <remarks>
    /// The extra height a card is given for having something to show, less the gap above it — so the
    /// picture is exactly the room the layout granted and no more. Derived rather than stated,
    /// because the two were separate numbers meaning one thing: shrinking the card without shrinking
    /// this would push the picture through the bottom of the card it lives in.
    /// </remarks>
    private static readonly double WidgetHeight =
        LayoutOptions.Default.StepHeightWithWidget - LayoutOptions.Default.StepHeight - WidgetMargin;

    /// <summary>The inset of a card's content from its edge.</summary>
    private const double CardPadding = 16;

    /// <summary>The breakpoint marker's width, and how far in from the card's edge it sits.</summary>
    private const double MarkerWidth = 16;
    private const double MarkerInset = 8;

    /// <summary>How much clear space is left between the marker and whatever the heading ends with.</summary>
    private const double MarkerGap = 6;

    /// <summary>
    /// The column a card's heading gives up to the breakpoint marker.
    /// </summary>
    /// <remarks>
    /// Derived rather than chosen, because the marker is not in that grid and cannot push back: it
    /// hangs in the card's own host so that dimming an unrun card cannot dim it. That makes the
    /// heading the only side able to leave room, and a number picked by eye would drift the moment
    /// the marker moved. The card's padding already covers part of the marker's reach; this is the
    /// rest of it, plus the gap.
    /// </remarks>
    private const double MarkerColumn = MarkerWidth + MarkerInset - CardPadding + MarkerGap;

    private readonly CompositeDisposable subscriptions = [];
    private readonly Dictionary<string, StepVisual> stepVisuals = new(StringComparer.Ordinal);

    /// <summary>The coloured stroke of each pipe, kept so it can dim or light as its value appears.</summary>
    private readonly Dictionary<string, Path> pipeVisuals = new(StringComparer.Ordinal);

    private LayoutResult board = LayoutResult.Empty;

    /// <summary>How this run's steps timed against the last run of the test that passed.</summary>
    /// <remarks>
    /// Held rather than read on demand because it arrives late — a journal has to be read for it — and the
    /// board is already drawn by then. Kept out of the geometry deliberately: a step getting slower moves
    /// nothing, so this repaints the card rather than relaying out the run.
    /// </remarks>
    private TimingDiff timing = TimingDiff.None;
    private HashSet<string> brokenChecks = new(StringComparer.Ordinal);
    private VerdictVisual? verdictVisual;
    private Point panOrigin;
    private bool panning;

    /// <summary>Set once a press has moved far enough to be a drag rather than a click.</summary>
    private bool dragging;

    private AnnotationLayer? annotations;
    private readonly AnnotationStore annotationStore = new();
    private string? annotationsJournal;

    /// <summary>Set when a board is waiting for the surface to have a size it can be fitted to.</summary>
    private bool needsFit;

    /// <summary>
    /// The marks this board draws and sets, once the window has given it them.
    /// </summary>
    /// <remarks>
    /// Null until then, and every read of it treats that as "no marks" rather than as an error. A board
    /// with no breakpoints behind it is a board nobody can mark, which is a truthful thing for it to
    /// show; throwing would turn a wiring mistake into a crash while a run is being watched.
    /// </remarks>
    private BreakpointService? breakpoints;

    /// <summary>Creates the board and binds it.</summary>
    public UC_Board()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectActiveRun)
            .Subscribe(Render));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectSelectedStep)
            .Subscribe(_ => RefreshAppearance()));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(ComparisonSelectors.SelectTiming)
            .Subscribe(compared =>
            {
                timing = compared;
                RefreshAppearance();
            }));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectIsEmpty)
            .Select(empty => empty ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        // The same transform object, not a copy of its values. Pan and zoom mutate stZoom and ttPan directly,
        // so sharing the group is what keeps the marks locked to the run without anything having to notice.
        cAnnotations.RenderTransform = cBoard.RenderTransform;

        annotations = new AnnotationLayer(cAnnotations, this) { Author = System.Environment.UserName };
        annotations.Changed += SaveAnnotations;

        // Marks belong to a run, so they are loaded when the run changes rather than when the board redraws -
        // which happens on every event a live run produces.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectSelectedSessionId)
            .Subscribe(_ => LoadAnnotations()));

        // The surface is measured after the run arrives, so this is what actually fits the first
        // board; the attempt in Render is just the case where a size already exists.
        bSurface.SizeChanged += (_, _) =>
        {
            if (needsFit)
                Fit();
        };

        Unloaded += (_, _) =>
        {
            if (breakpoints is not null)
                breakpoints.Changed -= RefreshAppearance;

            subscriptions.Dispose();
        };
    }

    /// <summary>
    /// Gives the board the marks it draws.
    /// </summary>
    /// <remarks>
    /// Which test those marks belong to is the service's own business — it follows the selected run.
    /// The board used to tell it, which meant marks worked only because this one control existed and
    /// happened to subscribe in its constructor.
    /// </remarks>
    public void UseBreakpoints(BreakpointService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (breakpoints is not null)
            breakpoints.Changed -= RefreshAppearance;

        breakpoints = service;
        breakpoints.Changed += RefreshAppearance;

        RefreshAppearance();
    }

    private void Render(RunGraph graph)
    {
        LayoutResult next = RunBoardLayout.Compute(graph);

        // Which checks broke, so a pipe into the verdict can be coloured by its own outcome rather
        // than by the run's. One failed check among five should show as one red pipe, not five.
        //
        // Only checks that were about a named value: a check against a bare value carries no
        // identifier, and one against a step carries the step's name — which would colour a pipe for
        // a variable that happens to share it.
        brokenChecks =
        [
            .. graph.Assertions
                .Where(assertion => !assertion.Succeeded)
                .Where(assertion => assertion.TargetKind is DebugAssertionTargetKind.Variable or DebugAssertionTargetKind.Artifact)
                .Select(assertion => assertion.Target)
        ];

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
    /// context. A board smaller than the window is magnified to use the space, but only up to
    /// <see cref="MaximumFitZoom"/> — past roughly double it stops looking fitted and starts looking
    /// blown up.
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

    /// <summary>
    /// How visible an unset breakpoint marker is.
    /// </summary>
    /// <remarks>
    /// Faint enough that a board of thirty steps is not a board of thirty marks, strong enough that the
    /// spot can be found without already knowing it is there.
    /// </remarks>
    private const double RestingMarkerOpacity = 0.25;

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
    /// <remarks>
    /// Internal rather than private so a card can be built and measured in a test. What goes wrong in
    /// here goes wrong geometrically — two things laid out over each other — and that is invisible to
    /// every assertion that does not lay the card out.
    /// </remarks>
    internal UIElement BuildStep(LayoutNode node)
    {
        Border status = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = (Brush)FindResource("StateNotRun") };
        TextBlock name = new() { Foreground = (Brush)FindResource("TextPrimary"), FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        TextBlock note = new() { Foreground = (Brush)FindResource("TextSecondary"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        TextBlock outputs = new() { Foreground = (Brush)FindResource("TextFaint"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };


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
                new ColumnDefinition { Width = GridLength.Auto },

                // The marker's corner, left empty. It is the only honest way to keep two things apart
                // when one of them is not in this grid to be laid out against.
                new ColumnDefinition { Width = new GridLength(MarkerColumn) }
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
            Background = (Brush)FindResource("SurfaceSunken"),
            Margin = new Thickness(0, WidgetMargin, 0, 0),
            Visibility = Visibility.Collapsed
        };

        StackPanel body = new()
        {
            Margin = new Thickness(CardPadding, 6, CardPadding, 6),

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
            StepSelected?.Invoke(stageName, stepId);
        };

        // Right-click still sets a breakpoint anywhere on the card. The marker is the discoverable way
        // in; this is the fast one, and it costs nothing to keep both.
        box.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            breakpoints?.Toggle(stageName, stepId);
        };

        Border breakpoint = BuildBreakpointMarker(stageName, stepId);

        // The marker sits beside the card rather than inside it, because a card that has not run yet is
        // dimmed to four-tenths — and a breakpoint set on a step that has not run is precisely the case
        // worth being able to see. Dimming is applied to the box; the marker is not in it.
        Grid host = new() { Width = node.Width, Height = node.Height };

        host.Children.Add(box);
        host.Children.Add(breakpoint);

        stepVisuals[node.Id] = new StepVisual(box, status, name, note, outputs, elapsed, breakpoint, widget);
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
            Width = MarkerWidth,
            Height = 11,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, MarkerInset, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Stop the run here."
        };

        marker.MouseLeftButtonUp += (_, e) =>
        {
            // Handled, so toggling a breakpoint does not also select the step underneath. The surface
            // ends its pan on the tunnelling event, so marking this one handled cannot strand it.
            e.Handled = true;
            breakpoints?.Toggle(stageName, stepId);
        };

        marker.MouseEnter += (_, _) => marker.Opacity = 1;
        marker.MouseLeave += (_, _) => marker.Opacity = breakpoints?.IsSet(stageName, stepId) == true ? 1 : RestingMarkerOpacity;

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

        RunGraph graph = StateStore<MainState>.Default.GetValue(state => state.Board.ActiveRun);
        StepSelection? selected = StateStore<MainState>.Default.GetValue(state => state.Board.SelectedStep);

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                if (!stepVisuals.TryGetValue($"step:{stage.Name}/{step.StepId}", out StepVisual? visual))
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
                visual.Box.Opacity = HasRun(step) ? 1 : DormantOpacity;

                ShowBreakpoint(visual.Breakpoint, breakpoints?.IsSet(stage.Name, step.StepId) == true);

                // The halt outranks the selection. A run stopped somewhere is the most important thing on
                // the board and lasts only until it is released, whereas which step a reader last clicked
                // is on the panel to the right anyway. A breakpoint that is merely set is the marker's job
                // now, which is what frees the border to mean "stopped, here".
                visual.Box.BorderBrush = step.IsWaitingAtBreakpoint
                    ? (Brush)FindResource("StateError")
                    : isSelected
                        ? (Brush)FindResource("Accent")
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
            elapsed.Foreground = (Brush)FindResource("TextFaint");
            return;
        }

        StepTiming? compared = timing.ForStep(stageName, step.StepId);

        if (compared is null || !compared.IsInteresting)
        {
            elapsed.Text = DurationText.Compact(duration);
            elapsed.ToolTip = compared?.Then is { } unchanged ? $"About the same as last time: {DurationText.Compact(unchanged)}" : null;
            elapsed.Foreground = (Brush)FindResource("TextFaint");
            return;
        }

        bool slower = compared.Change == StepTimingChange.Slower;

        elapsed.Text = $"{DurationText.Compact(duration)}  {(slower ? "+" : "−")}{DurationText.Compact(compared.Delta.Duration())}";

        // Amber rather than red. A slower step is worth noticing and is not a failure, and red on this board
        // already means the step broke.
        elapsed.Foreground = (Brush)FindResource(slower ? "StateTimeout" : "StateComplete");

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

    /// <summary>
    /// Raised when the reader picks a step, by its stage and its index within it.
    /// </summary>
    /// <remarks>
    /// The board says which step was picked; what showing a step means is the window's business. The
    /// board reached for the controller directly before, which made every card's click handler depend
    /// on a window having been built first.
    /// </remarks>
    public event Action<string, int>? StepSelected;

    /// <summary>Asks for the summary, as clicking the verdict does.</summary>
    /// <remarks>
    /// These three exist so the title bar and the keyboard shortcuts run the same code as each other
    /// rather than their own copies of it. Two paths to one action is how the two stop agreeing.
    /// </remarks>
    public void RequestSummary() => SummaryRequested?.Invoke();

    /// <summary>Scales and centres the board so all of it is on screen.</summary>
    public void FitToWindow() => Fit();

    /// <summary>Chooses what a drag on the board draws, or nothing to give the mouse back to the board.</summary>
    public void SetAnnotationTool(AnnotationKind? tool)
    {
        if (annotations is null)
            return;

        // Anything half-drawn is abandoned when the tool changes, rather than finished as the wrong kind.
        annotations.CancelInProgress();
        annotations.CommitTyping();
        annotations.Tool = tool;

        Cursor = tool is null ? Cursors.Arrow : Cursors.Cross;
    }

    /// <summary>Chooses the ink new marks are made in.</summary>
    public void SetAnnotationInk(string ink)
    {
        if (annotations is not null)
            annotations.Ink = ink;
    }

    /// <summary>Chooses how thick new marks are.</summary>
    public void SetAnnotationWeight(double weight)
    {
        if (annotations is not null)
            annotations.Weight = weight;
    }

    /// <summary>Takes the most recent mark back.</summary>
    public void UndoAnnotation() => annotations?.Undo();

    /// <summary>Shows or hides the marks without forgetting them.</summary>
    public void SetAnnotationsVisible(bool visible) => annotations?.SetVisible(visible);

    /// <summary>
    /// Whether the marks on this run were drawn against a different arrangement of the board.
    /// </summary>
    /// <remarks>
    /// Marks are board coordinates, and the arrangement is a pure function of the run and a set of constants — so
    /// it is stable for a given build and can move between builds. When it has moved, the marks are still shown:
    /// a drawing slightly out of place is worth more than no drawing, as long as nobody is left wondering why an
    /// arrow points at nothing.
    /// </remarks>
    public bool AnnotationsPredateThisLayout()
        => annotations is { Marks.IsEmpty: false } layer && layer.Marks.LayoutVersion != LayoutOptions.Version;

    /// <summary>
    /// Reads the marks for whichever run is selected.
    /// </summary>
    /// <remarks>
    /// A live run has no journal to sit beside, so it cannot be annotated yet — its marks would have nowhere to be
    /// saved, and inventing a place for them would put a drawing somewhere the run never was.
    /// </remarks>
    private void LoadAnnotations()
    {
        if (annotations is null)
            return;

        RunSummary? run = StateStore<MainState>.Default.GetValue(RunsSelectors.SelectedRunOf);

        annotationsJournal = run?.JournalPath;

        if (run is null || string.IsNullOrWhiteSpace(annotationsJournal))
        {
            annotations.Load(new RunAnnotations { SessionId = run?.SessionId ?? string.Empty });
            return;
        }

        annotations.Load(annotationStore.Load(annotationsJournal, run.SessionId));
    }

    /// <summary>
    /// Writes the marks after every change.
    /// </summary>
    /// <remarks>
    /// On each mark rather than on closing the panel. This tool attaches to test hosts that get killed and is
    /// itself sometimes closed abruptly; a drawing held in memory until later is a drawing that gets lost.
    /// </remarks>
    private void SaveAnnotations()
    {
        if (annotations is null || string.IsNullOrWhiteSpace(annotationsJournal))
            return;

        // Stamped with the arrangement it was drawn against, which is the only way a later build can know the
        // board has moved under these coordinates.
        annotationStore.Save(annotationsJournal, annotations.Marks with { LayoutVersion = LayoutOptions.Version });
    }

    /// <summary>Selects the first failed step and pans to it.</summary>
    public void GoToFirstFailure()
    {
        RunGraph graph = StateStore<MainState>.Default.GetValue(state => state.Board.ActiveRun);

        foreach (StageNode stage in graph.Stages)
        {
            foreach (StepNode step in stage.Steps)
            {
                if (step.Lifecycle is DebugLifecycleState.Error or DebugLifecycleState.Timeout)
                {
                    StepSelected?.Invoke(stage.Name, step.StepId);
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
        // Drawing wins, and the event stops here. This is the tunnelling handler, so marking it handled is what
        // stops a card underneath treating the same press as "select me" while a stroke is being drawn over it.
        if (annotations?.Begin(e.GetPosition(cBoard)) == true)
        {
            e.Handled = true;
            bSurface.CaptureMouse();

            return;
        }

        panOrigin = e.GetPosition(this);
        panning = true;
    }

    /// <summary>
    /// Ends a pan, whether or not one had started.
    /// </summary>
    /// <remarks>
    /// Deliberately not conditional on anything. This is the one place the pan is ended, and a release
    /// that fails to reach it leaves the board following the pointer with no button held — which is what
    /// happened while this was a bubbling handler and a card marked the click as its own.
    /// </remarks>
    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (annotations?.IsDrawing == true)
        {
            e.Handled = true;

            annotations.Commit(e.GetPosition(cBoard));
            bSurface.ReleaseMouseCapture();

            return;
        }

        EndPan();
    }

    /// <summary>
    /// Ends a pan when the capture is taken away rather than released.
    /// </summary>
    /// <remarks>
    /// A window losing activation mid-drag, or anything else claiming the mouse, never sends the button
    /// up. Without this the board would be left mid-pan until the next click.
    /// </remarks>
    private void Surface_LostMouseCapture(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        panning = false;

        if (!dragging)
            return;

        dragging = false;
        bSurface.ReleaseMouseCapture();
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (annotations?.IsDrawing == true)
        {
            annotations.Extend(e.GetPosition(cBoard));
            return;
        }

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

    /// <summary>Makes a breakpoint marker read as set or as an invitation to set one.</summary>
    private void ShowBreakpoint(Border marker, bool isSet)
    {
        marker.Background = isSet ? (Brush)FindResource("StateError") : Brushes.Transparent;
        marker.BorderBrush = isSet ? Brushes.Transparent : (Brush)FindResource("TextSecondary");
        marker.Opacity = isSet ? 1 : RestingMarkerOpacity;
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

        target.Height = WidgetHeight;
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
            Foreground = (Brush)FindResource("TextFaint"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 11,
            Margin = new Thickness(10, 8, 10, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = WidgetFaces.TextOf(widget) ?? widget.Description.Summary
        };
    }

    private sealed record StepVisual(
        Border Box,
        Border Status,
        TextBlock Name,
        TextBlock Note,
        TextBlock Outputs,
        TextBlock Elapsed,
        Border Breakpoint,
        Border Widget);

    private sealed record VerdictVisual(Border Box, TextBlock Heading, TextBlock Why);
}
