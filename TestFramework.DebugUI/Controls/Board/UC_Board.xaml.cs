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
    private readonly CompositeDisposable subscriptions = [];
    /// <summary>Turns the geometry of a run into the things on screen.</summary>
    private readonly BoardComposer composer;

    /// <summary>What each card says about the run, repainted without anything being moved.</summary>
    private readonly BoardPainter painter;

    private LayoutResult board = LayoutResult.Empty;

    /// <summary>How this run's steps timed against the last run of the test that passed.</summary>
    /// <remarks>
    /// Held rather than read on demand because it arrives late — a journal has to be read for it — and the
    /// board is already drawn by then. Kept out of the geometry deliberately: a step getting slower moves
    /// nothing, so this repaints the card rather than relaying out the run.
    /// </remarks>
    private TimingDiff timing = TimingDiff.None;
    private HashSet<string> brokenChecks = new(StringComparer.Ordinal);
    /// <summary>How far in the board is being looked at, and over which part.</summary>
    private readonly BoardViewport viewport;

    /// <summary>The marks drawn on this run, and where they are kept.</summary>
    private readonly BoardAnnotations annotations;

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

        annotations = new BoardAnnotations(cAnnotations, this);
        viewport = new BoardViewport(bSurface, stZoom, ttPan);

        // Handed the two things a drawn card has to be able to do, because both belong to whoever is
        // watching the board rather than to the drawing of it.
        painter = new BoardPainter(this);

        composer = new BoardComposer(
            this,
            cBoard,
            () => breakpoints,
            (stage, stepId) => StepSelected?.Invoke(stage, stepId),
            () => SummaryRequested?.Invoke());

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectActiveRun)
            .Subscribe(Render));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectSelectedStep)
            .Subscribe(_ => painter.Refresh()));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(ComparisonSelectors.SelectTiming)
            .Subscribe(compared =>
            {
                painter.Timing = compared;
                painter.Refresh();
            }));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(BoardSelectors.SelectIsEmpty)
            .Select(empty => empty ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        // The same transform object, not a copy of its values. Pan and zoom mutate stZoom and ttPan directly,
        // so sharing the group is what keeps the marks locked to the run without anything having to notice.
        cAnnotations.RenderTransform = cBoard.RenderTransform;

        // Marks belong to a run, so they are loaded when the run changes rather than when the board redraws -
        // which happens on every event a live run produces.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(RunsSelectors.SelectSelectedSessionId)
            .Subscribe(_ => annotations.Load()));

        // The surface is measured after the run arrives, so this is what actually fits the first
        // board; the attempt in Render is just the case where a size already exists.
        bSurface.SizeChanged += (_, _) =>
        {
            if (viewport.NeedsFit)
                Fit();
        };

        Unloaded += (_, _) =>
        {
            if (breakpoints is not null)
                breakpoints.Changed -= painter.Refresh;

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
            breakpoints.Changed -= painter.Refresh;

        breakpoints = service;
        painter.Breakpoints = service;
        breakpoints.Changed += painter.Refresh;

        painter.Refresh();
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
            painter.Paints(board, composer.Compose(board, brokenChecks));
        }

        // Fitted once, when a run first appears, and never again — refitting on every change would
        // drag the board out from under someone who had zoomed in on a step to read it.
        //
        // Queued rather than called: the run usually arrives before the surface has been measured,
        // and fitting to a size of zero silently does nothing at all.
        if (firstBoard)
        {
            viewport.NeedsFit = true;
            Fit();
        }

        painter.Refresh();
    }

    /// <summary>Puts the whole run on screen.</summary>
    private void Fit() => viewport.Fit(new Size(board.Width, board.Height));

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

    /// <summary>Chooses what new marks are, or nothing to stop drawing.</summary>
    public void SetAnnotationTool(AnnotationKind? tool) => annotations.Tool(tool);

    /// <summary>Chooses the ink new marks are made in.</summary>
    public void SetAnnotationInk(string ink) => annotations.Ink(ink);

    /// <summary>Chooses how thick new marks are.</summary>
    public void SetAnnotationWeight(double weight) => annotations.Weight(weight);

    /// <summary>Takes the most recent mark back.</summary>
    public void UndoAnnotation() => annotations.Undo();

    /// <summary>Shows or hides the marks without forgetting them.</summary>
    public void SetAnnotationsVisible(bool visible) => annotations.SetVisible(visible);

    /// <summary>Whether the marks on this run were drawn against a different arrangement of the board.</summary>
    public bool AnnotationsPredateThisLayout() => annotations.PredateThisLayout();

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
        // Asked of the arrangement rather than of what was drawn: a step the layout placed is a step
        // on the board, and this is about where to look rather than about what is there.
        LayoutNode? node = board.Nodes.FirstOrDefault(candidate =>
            candidate.Kind == LayoutNodeKind.Step && candidate.StageName == stageName && candidate.StepId == stepId);

        if (node is null)
            return;

        viewport.CentreOn(new Point(node.CentreX, node.CentreY), new Size(ActualWidth, ActualHeight));
    }

    private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        viewport.ZoomAt(e.GetPosition(cBoard), e.Delta);

        e.Handled = true;
    }

    /// <summary>
    /// Decides whether a press is a stroke, a pan or a click on a card.
    /// </summary>
    /// <remarks>
    /// Drawing wins, and the event stops here. This is the tunnelling handler, so marking it handled is what
    /// stops a card underneath treating the same press as "select me" while a stroke is being drawn over it.
    /// </remarks>
    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (annotations.Begin(e.GetPosition(cBoard)))
        {
            e.Handled = true;
            bSurface.CaptureMouse();

            return;
        }

        viewport.Press(e.GetPosition(this));
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (annotations.IsDrawing)
        {
            e.Handled = true;

            annotations.Commit(e.GetPosition(cBoard));
            bSurface.ReleaseMouseCapture();

            return;
        }

        viewport.Release(bSurface);
    }

    /// <summary>
    /// Ends a pan when the capture is taken away rather than released.
    /// </summary>
    /// <remarks>
    /// A window losing activation mid-drag, or anything else claiming the mouse, never sends the button
    /// up. Without this the board would be left mid-pan until the next click.
    /// </remarks>
    private void Surface_LostMouseCapture(object sender, MouseEventArgs e) => viewport.Release(bSurface);

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (annotations.IsDrawing)
        {
            annotations.Extend(e.GetPosition(cBoard));
            return;
        }

        viewport.Move(e.GetPosition(this), (IInputElement)sender);
    }
}
