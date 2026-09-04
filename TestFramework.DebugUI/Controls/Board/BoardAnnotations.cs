using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Axiom.State;
using TestFramework.DebugUI.Controls.Annotate;
using TestFramework.DebugUI.Layout;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Annotations;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// The marks a reader draws on a run, and where they are kept.
/// </summary>
/// <remarks>
/// <para>
/// A drawing belongs to a run rather than to the board, so it is read when the selection changes and
/// written on every mark — this tool attaches to test hosts that get killed and is itself sometimes
/// closed abruptly, and a drawing held in memory until later is a drawing that gets lost.
/// </para>
/// <para>
/// Marks are board coordinates, so they are stamped with the arrangement they were drawn against.
/// That is the only way a later build can know the board has moved underneath them.
/// </para>
/// </remarks>
internal sealed class BoardAnnotations
{
    private readonly AnnotationStore store = new();
    private readonly AnnotationLayer layer;
    private readonly FrameworkElement surface;

    private string? journal;

    /// <summary>Draws on one board.</summary>
    /// <param name="canvas">Where the marks are drawn, locked to the board's own transform.</param>
    /// <param name="surface">What the pointer changes shape on while a tool is chosen.</param>
    public BoardAnnotations(Canvas canvas, FrameworkElement surface)
    {
        this.surface = surface;

        layer = new AnnotationLayer(canvas, surface) { Author = System.Environment.UserName };
        layer.Changed += Save;
    }

    /// <summary>Whether a stroke is being drawn right now, which is what makes drawing beat panning.</summary>
    public bool IsDrawing => layer.IsDrawing;

    /// <summary>Starts a stroke, reporting whether a tool was chosen at all.</summary>
    public bool Begin(Point at) => layer.Begin(at);

    /// <summary>Carries a stroke on.</summary>
    public void Extend(Point to) => layer.Extend(to);

    /// <summary>Finishes a stroke.</summary>
    public void Commit(Point at) => layer.Commit(at);

    /// <summary>Chooses what new marks are, or nothing to stop drawing.</summary>
    public void Tool(AnnotationKind? tool)
    {
        // Anything half-drawn is abandoned when the tool changes, rather than finished as the wrong kind.
        layer.CancelInProgress();
        layer.CommitTyping();
        layer.Tool = tool;

        surface.Cursor = tool is null ? Cursors.Arrow : Cursors.Cross;
    }

    /// <summary>Chooses the ink new marks are made in.</summary>
    public void Ink(string ink) => layer.Ink = ink;

    /// <summary>Chooses how thick new marks are.</summary>
    public void Weight(double weight) => layer.Weight = weight;

    /// <summary>Takes the most recent mark back.</summary>
    public void Undo() => layer.Undo();

    /// <summary>Shows or hides the marks without forgetting them.</summary>
    public void SetVisible(bool visible) => layer.SetVisible(visible);

    /// <summary>
    /// Whether the marks on this run were drawn against a different arrangement of the board.
    /// </summary>
    /// <remarks>
    /// The arrangement is a pure function of the run and a set of constants — so it is stable for a given build
    /// and can move between builds. When it has moved, the marks are still shown: a drawing slightly out of place
    /// is worth more than no drawing, as long as nobody is left wondering why an arrow points at nothing.
    /// </remarks>
    public bool PredateThisLayout()
        => layer is { Marks.IsEmpty: false } && layer.Marks.LayoutVersion != LayoutOptions.Version;

    /// <summary>
    /// Reads the marks for whichever run is selected.
    /// </summary>
    /// <remarks>
    /// A live run has no journal to sit beside, so it cannot be annotated yet — its marks would have nowhere to be
    /// saved, and inventing a place for them would put a drawing somewhere the run never was.
    /// </remarks>
    public void Load()
    {
        RunSummary? run = StateStore<MainState>.Default.GetValue(RunsSelectors.SelectedRunOf);

        journal = run?.JournalPath;

        if (run is null || string.IsNullOrWhiteSpace(journal))
        {
            layer.Load(new RunAnnotations { SessionId = run?.SessionId ?? string.Empty });
            return;
        }

        layer.Load(store.Load(journal, run.SessionId));
    }

    /// <summary>Writes the marks after every change.</summary>
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(journal))
            return;

        // Stamped with the arrangement it was drawn against, which is the only way a later build can know the
        // board has moved under these coordinates.
        store.Save(journal, layer.Marks with { LayoutVersion = LayoutOptions.Version });
    }
}
