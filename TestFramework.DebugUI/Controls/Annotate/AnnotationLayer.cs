using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TestFramework.DebugUI.State.Annotations;

namespace TestFramework.DebugUI.Controls.Annotate;

/// <summary>
/// Draws the marks on a run, and turns a drag into one.
/// </summary>
/// <remarks>
/// <para>
/// Its own class rather than more of the board, which is already the largest control here. The board owns the
/// run; this owns what somebody drew on it. The only thing they share is a coordinate space.
/// </para>
/// <para>
/// That space is the board's, and the layer's canvas carries the board's own transform — the same object, not a
/// copy — so panning and zooming move the marks with the run for nothing. Marks are stored in those coordinates
/// too, which is why a drawing survives a window resize, a zoom and a reopen.
/// </para>
/// <para>
/// Every mark is drawn twice: a dark halo underneath, then the ink. Over a board of bright pipes a thin coloured
/// line is hard to follow, and the halo is also what makes human ink read as something laid on top rather than as
/// another thing the framework is telling you.
/// </para>
/// </remarks>
internal sealed class AnnotationLayer
{
    /// <summary>How much wider than the ink the halo is drawn, in board units.</summary>
    private const double Halo = 3;

    private readonly Canvas canvas;
    private readonly FrameworkElement resources;

    private readonly List<UIElement> committed = [];

    private Annotation? drawing;
    private List<AnnotationPoint> stroke = [];
    private List<UIElement> preview = [];
    private TextBox? typing;
    private AnnotationPoint typingAt;

    /// <summary>Creates a layer over a canvas.</summary>
    internal AnnotationLayer(Canvas canvas, FrameworkElement resources)
    {
        this.canvas = canvas;
        this.resources = resources;
    }

    /// <summary>Raised when the set of marks changed and is worth saving.</summary>
    internal event Action? Changed;

    /// <summary>Gets or sets the marks this layer is showing.</summary>
    internal RunAnnotations Marks { get; private set; } = new() { SessionId = string.Empty };

    /// <summary>Gets or sets the tool in use, or null when nothing is being drawn.</summary>
    internal AnnotationKind? Tool { get; set; }

    /// <summary>Gets or sets the ink new marks are made in.</summary>
    internal string Ink { get; set; } = "InkCyan";

    /// <summary>Gets or sets how thick new marks are.</summary>
    internal double Weight { get; set; } = 4;

    /// <summary>Gets or sets who is drawing.</summary>
    internal string? Author { get; set; }

    /// <summary>Gets a value indicating whether a drag is currently making a mark.</summary>
    internal bool IsDrawing => drawing is not null;

    /// <summary>Shows a run's marks, replacing whatever was shown before.</summary>
    internal void Load(RunAnnotations annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        Discard();

        Marks = annotations;

        Redraw();
    }

    /// <summary>Shows or hides every mark without forgetting any of them.</summary>
    internal void SetVisible(bool visible)
        => canvas.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Starts a mark, and says whether it took the gesture.
    /// </summary>
    /// <remarks>
    /// A note is placed rather than dragged, so it is finished here: a box appears, the reader types, and the mark
    /// exists when they are done. Everything else needs a drag to have a size.
    /// </remarks>
    internal bool Begin(Point at)
    {
        if (Tool is not { } tool)
            return false;

        CommitTyping();

        AnnotationPoint start = new(at.X, at.Y);

        if (tool == AnnotationKind.Text)
        {
            BeginTyping(start);
            return true;
        }

        stroke = [start];

        drawing = new Annotation
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = tool,
            Ink = Ink,
            Thickness = Weight,
            Points = [start],
            Author = Author,
            AtUtc = DateTimeOffset.UtcNow
        };

        return true;
    }

    /// <summary>Carries a mark along with the pointer.</summary>
    internal void Extend(Point at)
    {
        if (drawing is null)
            return;

        AnnotationPoint point = new(at.X, at.Y);

        if (drawing.Kind == AnnotationKind.Freehand)
            stroke.Add(point);
        else
            stroke = [stroke[0], point];

        ShowPreview(drawing with { Points = [.. stroke] });
    }

    /// <summary>
    /// Finishes a mark, or throws it away if it never became one.
    /// </summary>
    /// <remarks>
    /// A click with a shape tool is a click, not a rectangle of no size, and a stroke of one point is a dot
    /// nobody meant. Both are discarded rather than saved as marks that cannot be seen or removed.
    /// </remarks>
    internal void Commit(Point at)
    {
        if (drawing is null)
            return;

        Extend(at);

        Annotation? mark = Finished();

        ClearPreview();
        drawing = null;
        stroke = [];

        if (mark is null)
            return;

        Marks = Marks.With(mark);

        Add(mark);
        Changed?.Invoke();
    }

    /// <summary>Abandons whatever was being drawn.</summary>
    internal void CancelInProgress()
    {
        ClearPreview();

        drawing = null;
        stroke = [];
    }

    /// <summary>Takes the most recent mark back.</summary>
    internal void Undo()
    {
        CommitTyping();

        if (Marks.IsEmpty)
            return;

        Marks = Marks.WithoutLast();

        Redraw();
        Changed?.Invoke();
    }

    /// <summary>Finishes any note being typed, which is what makes clicking elsewhere commit it.</summary>
    internal void CommitTyping()
    {
        if (typing is null)
            return;

        string text = typing.Text;

        canvas.Children.Remove(typing);
        typing = null;

        if (string.IsNullOrWhiteSpace(text))
            return;

        Annotation mark = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = AnnotationKind.Text,
            Ink = Ink,
            Thickness = Weight,
            Points = [typingAt],
            Text = text.Trim(),
            Author = Author,
            AtUtc = DateTimeOffset.UtcNow
        };

        Marks = Marks.With(mark);

        Add(mark);
        Changed?.Invoke();
    }

    /// <summary>The mark as it now stands, or nothing when the gesture was too small to mean anything.</summary>
    private Annotation? Finished()
    {
        if (drawing is null)
            return null;

        ImmutableList<AnnotationPoint> points = [.. stroke];

        if (drawing.Kind == AnnotationKind.Freehand)
        {
            // Simplified before it is stored, not just before it is drawn. The points a pointer reports are
            // mostly the tremor of a hand, and keeping them would put kilobytes of noise in the file and hand the
            // same noise to whoever the run is shared with.
            ImmutableList<AnnotationPoint> kept = Smoothing.Simplify(points);

            return kept.Count < 2 ? null : drawing with { Points = kept };
        }

        if (points.Count < 2 || Distance(points[0], points[^1]) < 4)
            return null;

        return drawing with { Points = points };
    }

    private void BeginTyping(AnnotationPoint at)
    {
        typingAt = at;

        typing = new TextBox
        {
            MinWidth = 160,
            Background = (Brush)resources.FindResource("SurfaceOverlay"),
            Foreground = (Brush)resources.FindResource(Ink),
            BorderBrush = (Brush)resources.FindResource(Ink),
            BorderThickness = new Thickness(1),
            FontSize = 14 + Weight,
            Padding = new Thickness(4, 2, 4, 2),
            AcceptsReturn = false
        };

        // Enter finishes; Escape abandons. Both are what a text box on a canvas is expected to do, and neither
        // should reach the window's shortcuts and re-run the test.
        typing.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitTyping();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;

                canvas.Children.Remove(typing);
                typing = null;
            }
        };

        Canvas.SetLeft(typing, at.X);
        Canvas.SetTop(typing, at.Y);

        canvas.Children.Add(typing);

        typing.Focus();
    }

    private void ShowPreview(Annotation mark)
    {
        ClearPreview();

        preview = Build(mark);

        foreach (UIElement element in preview)
            canvas.Children.Add(element);
    }

    private void ClearPreview()
    {
        foreach (UIElement element in preview)
            canvas.Children.Remove(element);

        preview = [];
    }

    private void Redraw()
    {
        foreach (UIElement element in committed)
            canvas.Children.Remove(element);

        committed.Clear();

        foreach (Annotation mark in Marks.Marks)
            Add(mark);
    }

    private void Add(Annotation mark)
    {
        foreach (UIElement element in Build(mark))
        {
            committed.Add(element);
            canvas.Children.Add(element);
        }
    }

    private void Discard()
    {
        CancelInProgress();

        if (typing is not null)
        {
            canvas.Children.Remove(typing);
            typing = null;
        }

        foreach (UIElement element in committed)
            canvas.Children.Remove(element);

        committed.Clear();
    }

    /// <summary>The shapes that draw one mark: the halo, then the ink over it.</summary>
    private List<UIElement> Build(Annotation mark)
    {
        if (mark.Kind == AnnotationKind.Text)
            return [Words(mark)];

        Geometry geometry = Outline(mark);

        if (geometry.IsEmpty())
            return [];

        return
        [
            Stroked(geometry, (Brush)resources.FindResource("InkHalo"), mark.Thickness + Halo),
            Stroked(geometry, (Brush)resources.FindResource(mark.Ink), mark.Thickness)
        ];
    }

    private static Path Stroked(Geometry geometry, Brush brush, double thickness) => new()
    {
        Data = geometry,
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,

        // The board underneath stays clickable: a mark is something to look at, not something in the way.
        IsHitTestVisible = false
    };

    private UIElement Words(Annotation mark)
    {
        TextBlock text = new()
        {
            Text = mark.Text ?? string.Empty,
            Foreground = (Brush)resources.FindResource(mark.Ink),
            FontSize = 14 + mark.Thickness,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,

            // The halo, for words: a dark glow rather than a second copy of the text.
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 6,
                ShadowDepth = 0,
                Opacity = 0.9,
                Color = Colors.Black
            }
        };

        AnnotationPoint at = mark.Points.Count > 0 ? mark.Points[0] : new AnnotationPoint(0, 0);

        Canvas.SetLeft(text, at.X);
        Canvas.SetTop(text, at.Y);

        return text;
    }

    /// <summary>The geometry for a mark, in board coordinates.</summary>
    private static Geometry Outline(Annotation mark)
    {
        ImmutableList<AnnotationPoint> points = mark.Points;

        if (points.Count < 2)
            return Geometry.Empty;

        return mark.Kind switch
        {
            AnnotationKind.Freehand => Curve(points),
            AnnotationKind.Rectangle => new RectangleGeometry(Box(points[0], points[^1])),
            AnnotationKind.Ellipse => new EllipseGeometry(Box(points[0], points[^1])),
            AnnotationKind.Arrow => ArrowFrom(points[0], points[^1], mark.Thickness),
            _ => Geometry.Empty
        };
    }

    /// <summary>
    /// A smooth line through the points a hand actually visited.
    /// </summary>
    /// <remarks>
    /// Simplified first and then curved. Drawing the raw samples gives a visibly jagged line even though the
    /// points are a pixel apart, because each one carries its own small error in a different direction.
    /// </remarks>
    private static Geometry Curve(ImmutableList<AnnotationPoint> points)
    {
        ImmutableList<AnnotationPoint> kept = Smoothing.Simplify(points);

        if (kept.Count < 2)
            return Geometry.Empty;

        PathFigure figure = new() { StartPoint = new Point(kept[0].X, kept[0].Y), IsClosed = false, IsFilled = false };

        foreach (CurveSegment segment in Smoothing.Curve(kept))
        {
            figure.Segments.Add(new BezierSegment(
                new Point(segment.FirstControl.X, segment.FirstControl.Y),
                new Point(segment.SecondControl.X, segment.SecondControl.Y),
                new Point(segment.End.X, segment.End.Y),
                isStroked: true));
        }

        PathGeometry geometry = new();

        geometry.Figures.Add(figure);

        return geometry;
    }

    private static Rect Box(AnnotationPoint from, AnnotationPoint to)
        => new(
            Math.Min(from.X, to.X),
            Math.Min(from.Y, to.Y),
            Math.Abs(to.X - from.X),
            Math.Abs(to.Y - from.Y));

    /// <summary>A line with a head, sized from the stroke so a thick arrow does not get a pin-sized tip.</summary>
    private static Geometry ArrowFrom(AnnotationPoint from, AnnotationPoint to, double thickness)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length < double.Epsilon)
            return Geometry.Empty;

        double angle = Math.Atan2(dy, dx);
        double head = Math.Max(12, thickness * 3.5);
        const double Spread = 0.42;

        GeometryGroup group = new();

        group.Children.Add(new LineGeometry(new Point(from.X, from.Y), new Point(to.X, to.Y)));

        group.Children.Add(new LineGeometry(
            new Point(to.X, to.Y),
            new Point(to.X - (head * Math.Cos(angle - Spread)), to.Y - (head * Math.Sin(angle - Spread)))));

        group.Children.Add(new LineGeometry(
            new Point(to.X, to.Y),
            new Point(to.X - (head * Math.Cos(angle + Spread)), to.Y - (head * Math.Sin(angle + Spread)))));

        return group;
    }

    private static double Distance(AnnotationPoint a, AnnotationPoint b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}
