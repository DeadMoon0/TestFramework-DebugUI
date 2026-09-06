using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TestFramework.DebugUI.State.Theming;

namespace TestFramework.DebugUI.Theme;

/// <summary>The four colours every backdrop is drawn from.</summary>
/// <remarks>
/// Four and no more, on purpose. A recipe that could reach for any colour would look like it belonged
/// to the theme it was written for and borrowed by every other one; a recipe that can only reach for
/// these comes out in whatever theme is asking.
/// </remarks>
internal readonly record struct BackdropInk(Color Ground, Color Near, Color Far, Color Glow);

/// <summary>
/// Draws what sits behind the whole window.
/// </summary>
/// <remarks>
/// <para>
/// Honest, and deliberately so: a recipe, four colours and a size always give the same drawing. It
/// reads no resources and owns no state, which is what lets the control above it treat a backdrop as a
/// value it can throw away and ask for again whenever the theme changes.
/// </para>
/// <para>
/// Everything is laid out in <see cref="Width"/> by <see cref="Height"/> and stretched to the window
/// by the control, so a recipe never has to know how big the window is. The proportions are roughly
/// the window's own, so the shapes arrive on screen close to the way they were drawn rather than
/// stretched into ovals.
/// </para>
/// <para>
/// The result is frozen. That is the opposite of the palette brushes, which must stay unfrozen because
/// a theme change moves their colours in place — a backdrop is not edited, it is replaced, so there is
/// nothing to gain by leaving it changeable and a render pass to gain by not.
/// </para>
/// </remarks>
internal static class BackdropPainter
{
    /// <summary>The width every recipe is drawn in.</summary>
    public const double Width = 480;

    /// <summary>The height every recipe is drawn in.</summary>
    public const double Height = 280;

    private static readonly Rect Area = new(0, 0, Width, Height);

    /// <summary>
    /// Draws one backdrop.
    /// </summary>
    /// <remarks>
    /// The ground goes down first in every recipe, including the ones that draw nothing on top of it.
    /// In a see-through theme that ground is transparent, which is exactly how such a theme is
    /// expressed — and it is still what shows when Windows declines to draw the blur behind the window.
    /// </remarks>
    public static Drawing Paint(BackdropRecipe recipe, BackdropInk ink)
    {
        DrawingGroup group = new();

        Fill(group, new RectangleGeometry(Area), ink.Ground);

        switch (recipe)
        {
            case BackdropRecipe.Clear:
            case BackdropRecipe.Flat:
                break;

            case BackdropRecipe.Hexfield: Hexfield(group, ink); break;
            case BackdropRecipe.Orbits: Orbits(group, ink); break;
            case BackdropRecipe.Scatter: Scatter(group, ink); break;
            case BackdropRecipe.Lattice: Lattice(group, ink); break;
            case BackdropRecipe.Arcs: Arcs(group, ink); break;
            case BackdropRecipe.Dunes: Dunes(group, ink); break;

            default: Ridges(group, ink); break;
        }

        group.Freeze();

        return group;
    }

    // ---------------------------------------------------------------- the recipes

    /// <summary>A honeycomb with cells missing and a few lit.</summary>
    private static void Hexfield(DrawingGroup group, BackdropInk ink)
    {
        const double Radius = 34;

        Lamp(group, ink, 0.22, 0.18, 0.62, 1);
        Lamp(group, ink, 0.88, 0.82, 0.46, 0.5);

        double across = Math.Sqrt(3) * Radius;
        double down = 1.5 * Radius;

        for (int row = -1; (row - 1) * down < Height; row++)
        {
            for (int column = -1; (column - 1) * across < Width; column++)
            {
                double depth = Noise(column, row);

                if (depth < 0.17)
                    continue;

                Point centre = new(
                    (column * across) + ((row & 1) == 1 ? across / 2 : 0),
                    row * down);

                Geometry cell = Hexagon(centre, Radius * 0.9);

                Fill(group, cell, Tone(ink, depth));

                if (depth > 0.87)
                    Fill(group, cell, Fade(ink.Glow, 0.6));
            }
        }
    }

    /// <summary>Rings around a centre just off the frame, with loose discs below.</summary>
    private static void Orbits(DrawingGroup group, BackdropInk ink)
    {
        Lamp(group, ink, 0.74, 0.24, 0.66, 1);

        Point centre = new(Width * 0.76, Height * 0.20);

        for (int i = 10; i >= 0; i--)
        {
            Colour ring = i == 3 ? Fade(ink.Glow, 0.5) : Tone(ink, i / 10.0);

            Stroke(group, Disc(centre, 26 + (i * 25)), ring, 6 + ((i % 3) * 7));
        }

        Fill(group, Disc(centre, 22), Fade(ink.Glow, 0.95));

        (double X, double Y, double R, double Depth)[] moons =
        [
            (0.14, 0.74, 32, 0.30),
            (0.40, 0.90, 20, 0.55),
            (0.02, 0.32, 15, 0.75)
        ];

        foreach ((double x, double y, double r, double depth) in moons)
            Fill(group, Disc(new Point(x * Width, y * Height), r), Tone(ink, depth));
    }

    /// <summary>Discs sorted back to front, a few of them outlines.</summary>
    private static void Scatter(DrawingGroup group, BackdropInk ink)
    {
        Lamp(group, ink, 0.28, 0.26, 0.60, 1);
        Lamp(group, ink, 0.82, 0.76, 0.48, 0.55);

        List<(Point At, double R, double Kind)> discs = [];

        for (int i = 0; i < 30; i++)
        {
            discs.Add((
                new Point(Noise(i, 1) * Width, Noise(i, 2) * Height),
                12 + (Noise(i, 3) * 52),
                Noise(i, 4)));
        }

        // Largest first, so the big soft ones read as distance and the small ones sit in front of them.
        discs.Sort((left, right) => right.R.CompareTo(left.R));

        foreach ((Point at, double r, double kind) in discs)
        {
            Geometry disc = Disc(at, r);

            if (kind > 0.84)
                Fill(group, disc, Fade(ink.Glow, 0.55));
            else if (kind < 0.20)
                Stroke(group, disc, Tone(ink, 0.2), 6);
            else
                Fill(group, disc, Tone(ink, 1 - (r / 64)));
        }
    }

    /// <summary>An isometric grid with lit nodes: the board's own shape, behind the board.</summary>
    private static void Lattice(DrawingGroup group, BackdropInk ink)
    {
        const double Step = 46;

        Lamp(group, ink, 0.5, 0.14, 0.72, 1);

        double run = Height / Math.Tan(Math.PI / 3);

        for (int k = -8; k <= 20; k++)
        {
            double thickness = (k + 8) % 4 == 0 ? 6 : 3;
            Colour line = Tone(ink, 0.22 + (((k + 8) % 5) / 7.0));

            Stroke(group, Line(0, k * Step * 0.5, Width, k * Step * 0.5), line, thickness);
            Stroke(group, Line(k * Step, 0, (k * Step) + run, Height), line, thickness);
            Stroke(group, Line(k * Step, 0, (k * Step) - run, Height), line, thickness);
        }

        for (int row = 0; row * Step * 0.5 <= Height; row++)
        {
            for (int column = -1; column * Step <= Width + Step; column++)
            {
                if (Noise(column, row) < 0.78)
                    continue;

                Point node = new(
                    (column * Step) + ((row & 1) == 1 ? Step / 2 : 0),
                    row * Step * 0.5);

                Fill(group, Disc(node, 8), Fade(ink.Glow, 0.9));
            }
        }
    }

    /// <summary>Nested sweeps out of one corner.</summary>
    private static void Arcs(DrawingGroup group, BackdropInk ink)
    {
        Lamp(group, ink, 0.10, 0.94, 0.82, 1);

        Point centre = new(-Width * 0.05, Height * 1.05);

        for (int i = 10; i >= 0; i--)
        {
            double radius = 44 + (i * 34);
            double from = -Math.PI / 2 + (0.05 * i);
            double to = -0.04 * i;

            Colour sweep = i % 4 == 1 ? Fade(ink.Glow, 0.6) : Tone(ink, i / 10.0);

            Stroke(group, Arc(centre, radius, from, to), sweep, 9 + ((i % 3) * 9));
        }
    }

    /// <summary>Fewer, rounder bands, and a low sun sitting in them.</summary>
    private static void Dunes(DrawingGroup group, BackdropInk ink)
    {
        Lamp(group, ink, 0.62, 0.58, 0.70, 1);

        for (int b = 0; b < 7; b++)
        {
            double t = b / 6.0;

            Fill(
                group,
                Band(Height * (0.30 + (t * 0.58)), [16 * (1 - (t * 0.45)), 9 * (1 - (t * 0.45))], [0.9, 2.1], 1.9 * b),
                Tone(ink, Math.Pow(t, 0.8)));
        }
    }

    /// <summary>Layered ridges: the shape the tool shipped with, before it had a choice.</summary>
    private static void Ridges(DrawingGroup group, BackdropInk ink)
    {
        Lamp(group, ink, 0.78, 0.10, 0.72, 1);

        for (int b = 0; b < 11; b++)
        {
            double t = b / 10.0;

            Fill(
                group,
                Band(
                    Height * (0.14 + (t * 0.78)),
                    [18 * (1 - (t * 0.4)), 10 * (1 - (t * 0.4)), 5 * (1 - (t * 0.4))],
                    [1.7, 3.3, 5.1],
                    0.7 * b),
                Tone(ink, Math.Pow(t, 0.85)));
        }
    }

    // ---------------------------------------------------------------- shapes

    /// <summary>
    /// A band of summed sines, filled to the bottom of the frame.
    /// </summary>
    /// <remarks>
    /// Three waves rather than one. A single sine reads as a drawing of a wave; summing a few at
    /// unrelated frequencies gives a line with no visible period, which is what makes a stack of them
    /// read as landscape rather than as a pattern.
    /// </remarks>
    private static Geometry Band(double baseline, double[] amplitudes, double[] frequencies, double phase)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(0, At(0)), isFilled: true, isClosed: true);

            for (double x = 3; x <= Width; x += 3)
                context.LineTo(new Point(x, At(x)), isStroked: false, isSmoothJoin: true);

            context.LineTo(new Point(Width, Height), isStroked: false, isSmoothJoin: false);
            context.LineTo(new Point(0, Height), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();

        return geometry;

        double At(double x)
        {
            double y = baseline;

            for (int k = 0; k < frequencies.Length; k++)
                y += amplitudes[k] * Math.Sin((x / Width * Math.PI * 2 * frequencies[k]) + (phase * (k + 1.4)));

            return y;
        }
    }

    private static Geometry Hexagon(Point centre, double radius)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(Corner(0), isFilled: true, isClosed: true);

            for (int k = 1; k < 6; k++)
                context.LineTo(Corner(k), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();

        return geometry;

        Point Corner(int k)
        {
            double angle = ((60 * k) - 90) * Math.PI / 180;

            return new Point(centre.X + (Math.Cos(angle) * radius), centre.Y + (Math.Sin(angle) * radius));
        }
    }

    private static Geometry Disc(Point centre, double radius)
    {
        EllipseGeometry geometry = new(centre, radius, radius);
        geometry.Freeze();

        return geometry;
    }

    private static Geometry Line(double x1, double y1, double x2, double y2)
    {
        LineGeometry geometry = new(new Point(x1, y1), new Point(x2, y2));
        geometry.Freeze();

        return geometry;
    }

    private static Geometry Arc(Point centre, double radius, double from, double to)
    {
        Point start = new(centre.X + (Math.Cos(from) * radius), centre.Y + (Math.Sin(from) * radius));
        Point end = new(centre.X + (Math.Cos(to) * radius), centre.Y + (Math.Sin(to) * radius));

        PathGeometry geometry = new();
        PathFigure figure = new() { StartPoint = start, IsClosed = false, IsFilled = false };

        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = Math.Abs(to - from) > Math.PI
        });

        geometry.Figures.Add(figure);
        geometry.Freeze();

        return geometry;
    }

    // ---------------------------------------------------------------- ink

    /// <summary>A colour somewhere along the near-to-far ramp, alpha included.</summary>
    /// <remarks>
    /// Alpha is interpolated with the rest, which is what makes a recipe work under a see-through
    /// theme: the ramp's ends are translucent there, so the shapes come out floating over the desktop
    /// instead of hiding it.
    /// </remarks>
    private static Colour Tone(BackdropInk ink, double t)
    {
        double clamped = Math.Clamp(t, 0, 1);

        return new Colour(Color.FromArgb(
            Mix(ink.Near.A, ink.Far.A, clamped),
            Mix(ink.Near.R, ink.Far.R, clamped),
            Mix(ink.Near.G, ink.Far.G, clamped),
            Mix(ink.Near.B, ink.Far.B, clamped)));
    }

    private static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));

    private static Colour Fade(Color colour, double factor)
        => new(Color.FromArgb((byte)Math.Round(colour.A * Math.Clamp(factor, 0, 1)), colour.R, colour.G, colour.B));

    /// <summary>
    /// The one light source in the backdrop, as a soft ellipse of the glow colour.
    /// </summary>
    /// <remarks>
    /// A radial gradient confined to its own ellipse rather than a wash over the whole frame: the wash
    /// version tints the ground everywhere, and a ground that is everywhere slightly the accent colour
    /// is the thing that makes a backdrop look like a gradient somebody bought.
    /// </remarks>
    private static void Lamp(DrawingGroup group, BackdropInk ink, double x, double y, double radius, double strength)
    {
        if (ink.Glow.A == 0 || strength <= 0)
            return;

        RadialGradientBrush brush = new()
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            [
                new GradientStop(Fade(ink.Glow, strength).Value, 0),
                new GradientStop(Fade(ink.Glow, strength * 0.38).Value, 0.5),
                new GradientStop(Fade(ink.Glow, 0).Value, 1)
            ]
        };

        brush.Freeze();

        double r = radius * Width;

        group.Children.Add(new GeometryDrawing(brush, pen: null, Disc(new Point(x * Width, y * Height), r)));
    }

    private static void Fill(DrawingGroup group, Geometry geometry, Colour colour)
    {
        if (colour.Value.A == 0)
            return;

        group.Children.Add(new GeometryDrawing(colour.Brush, pen: null, geometry));
    }

    private static void Stroke(DrawingGroup group, Geometry geometry, Colour colour, double thickness)
    {
        if (colour.Value.A == 0)
            return;

        Pen pen = new(colour.Brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        group.Children.Add(new GeometryDrawing(brush: null, pen, geometry));
    }

    /// <summary>
    /// A colour and the frozen brush for it.
    /// </summary>
    /// <remarks>
    /// The pairing exists so the recipes can read as geometry rather than as brush construction. It is
    /// deliberately not one of the palette's brushes: those are shared and mutable so a theme change
    /// moves them, and handing one to a drawing that is about to be frozen would freeze it too.
    /// </remarks>
    private readonly struct Colour(Color value)
    {
        public Color Value { get; } = value;

        /// <summary>So a recipe can hand a palette colour straight to <see cref="Fill"/>.</summary>
        public static implicit operator Colour(Color value) => new(value);

        public SolidColorBrush Brush
        {
            get
            {
                SolidColorBrush brush = new(Value);
                brush.Freeze();

                return brush;
            }
        }
    }

    /// <summary>
    /// The same value for the same pair of numbers, every time.
    /// </summary>
    /// <remarks>
    /// A hash rather than a random number generator, so a backdrop is a function of its recipe and
    /// nothing else. Two windows on the same theme draw the same field, a screenshot taken today
    /// matches one taken tomorrow, and the drawing can be asserted.
    /// </remarks>
    private static double Noise(int i, int j)
    {
        double value = Math.Sin((i * 127.1) + (j * 311.7)) * 43758.5453;

        return value - Math.Floor(value);
    }
}
