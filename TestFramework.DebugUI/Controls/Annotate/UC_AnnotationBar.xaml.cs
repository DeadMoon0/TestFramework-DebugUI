using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TestFramework.DebugUI.State.Annotations;

namespace TestFramework.DebugUI.Controls.Annotate;

/// <summary>
/// The instruments for drawing on a run.
/// </summary>
/// <remarks>
/// Chooses; it does not draw. What a stroke looks like and where it lands is the board's business, and keeping
/// that split means the bar can be moved, restyled or replaced without touching a line of the drawing code.
/// </remarks>
public partial class UC_AnnotationBar : UserControl
{
    /// <summary>
    /// The inks on offer, in the order they are shown.
    /// </summary>
    /// <remarks>
    /// Five, deliberately. A palette long enough to tell three people's marks apart and short enough that the
    /// bar stays a bar; and none of them is one of the board's own state colours.
    /// </remarks>
    public static readonly ImmutableList<string> Inks = ["InkWhite", "InkCyan", "InkMagenta", "InkViolet", "InkOrange"];

    /// <summary>The stroke weights on offer, in board units.</summary>
    public static readonly ImmutableList<double> Weights = [2, 4, 8];

    private readonly Dictionary<string, Border> inkSwatches = [];
    private readonly Dictionary<double, Border> weightSwatches = [];

    private bool marksVisible = true;

    /// <summary>Creates the bar.</summary>
    public UC_AnnotationBar()
    {
        InitializeComponent();

        BuildInks();
        BuildWeights();

        btPointer.ToolTip = "Back to the board";
        btPen.ToolTip = "Draw";
        btRectangle.ToolTip = "Box";
        btEllipse.ToolTip = "Ellipse";
        btArrow.ToolTip = "Arrow";
        btText.ToolTip = "Note";
        btUndo.ToolTip = "Undo the last mark";
        btVisible.ToolTip = "Hide the marks";

        Select(null);
        Choose(Inks[1]);
        Weigh(Weights[1]);
    }

    /// <summary>Raised when a tool is chosen, or null for the pointer.</summary>
    public event Action<AnnotationKind?>? ToolChosen;

    /// <summary>Raised when an ink is chosen.</summary>
    public event Action<string>? InkChosen;

    /// <summary>Raised when a weight is chosen.</summary>
    public event Action<double>? WeightChosen;

    /// <summary>Raised when the reader asks to take the last mark back.</summary>
    public event Action? UndoRequested;

    /// <summary>Raised when the reader shows or hides the marks.</summary>
    public event Action<bool>? VisibilityChanged;

    /// <summary>Raised when the reader is finished annotating.</summary>
    public event Action? Closed;

    /// <summary>Gets the tool in use, or null when the board has the mouse back.</summary>
    public AnnotationKind? Tool { get; private set; }

    /// <summary>Gets the chosen ink.</summary>
    public string Ink { get; private set; } = "InkCyan";

    /// <summary>Gets the chosen weight.</summary>
    public double Weight { get; private set; } = 4;

    /// <summary>
    /// Says something small, or stops saying it.
    /// </summary>
    /// <remarks>
    /// For the one thing a reader cannot see for themselves: that the marks on this run were drawn against an
    /// older arrangement of the board, and so may no longer point where they were aimed.
    /// </remarks>
    public void Note(string? note)
    {
        tbNote.Text = note ?? string.Empty;
        tbNote.Visibility = string.IsNullOrWhiteSpace(note) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows the bar, with the pointer chosen so nothing is drawn by accident.</summary>
    public void Open()
    {
        Select(null);
        Visibility = Visibility.Visible;
    }

    private void BuildInks()
    {
        foreach (string ink in Inks)
        {
            Border swatch = new()
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)FindResource(ink),
                Margin = new Thickness(2, 0, 2, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = Readable(ink)
            };

            string chosen = ink;

            swatch.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(chosen); };

            inkSwatches[ink] = swatch;
            spInks.Children.Add(swatch);
        }
    }

    private void BuildWeights()
    {
        foreach (double weight in Weights)
        {
            // The swatch is the stroke it stands for, at the weight it stands for.
            Border line = new()
            {
                Width = 16,
                Height = weight,
                CornerRadius = new CornerRadius(weight / 2),
                Background = (Brush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            };

            Border swatch = new()
            {
                Width = 24,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = line,
                ToolTip = $"{weight:0} units"
            };

            double chosen = weight;

            swatch.MouseLeftButtonUp += (_, e) => { e.Handled = true; Weigh(chosen); };

            weightSwatches[weight] = swatch;
            spWeights.Children.Add(swatch);
        }
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        Select(string.Equals(tag, "Pointer", StringComparison.Ordinal) ? null : Enum.Parse<AnnotationKind>(tag));
    }

    /// <summary>Makes one tool look chosen and tells whoever is listening.</summary>
    private void Select(AnnotationKind? tool)
    {
        Tool = tool;

        Paint(btPointer, pathPointer, tool is null);
        Paint(btPen, pathPen, tool == AnnotationKind.Freehand);
        Paint(btRectangle, pathRectangle, tool == AnnotationKind.Rectangle);
        Paint(btEllipse, pathEllipse, tool == AnnotationKind.Ellipse);
        Paint(btArrow, pathArrow, tool == AnnotationKind.Arrow);
        Paint(btText, pathText, tool == AnnotationKind.Text);

        ToolChosen?.Invoke(tool);
    }

    private void Paint(Button button, Path glyph, bool chosen)
    {
        button.Background = chosen ? (Brush)FindResource("Accent") : Brushes.Transparent;
        glyph.Stroke = (Brush)FindResource(chosen ? "TextPrimary" : "TextSecondary");
    }

    private void Choose(string ink)
    {
        Ink = ink;

        foreach ((string key, Border swatch) in inkSwatches)
        {
            swatch.BorderBrush = string.Equals(key, ink, StringComparison.Ordinal)
                ? (Brush)FindResource("TextPrimary")
                : Brushes.Transparent;
        }

        InkChosen?.Invoke(ink);
    }

    private void Weigh(double weight)
    {
        Weight = weight;

        foreach ((double key, Border swatch) in weightSwatches)
        {
            swatch.Background = key == weight
                ? (Brush)FindResource("SurfaceRaisedHover")
                : Brushes.Transparent;
        }

        WeightChosen?.Invoke(weight);
    }

    private void btUndo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();

    private void btVisible_Click(object sender, RoutedEventArgs e)
    {
        marksVisible = !marksVisible;

        Brush ink = (Brush)FindResource(marksVisible ? "TextSecondary" : "TextFaint");

        pathEye.Stroke = ink;
        ellipseEye.Fill = ink;

        btVisible.ToolTip = marksVisible ? "Hide the marks" : "Show the marks";

        VisibilityChanged?.Invoke(marksVisible);
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    /// <summary>An ink's name without the resource prefix, for a tooltip.</summary>
    private static string Readable(string ink) => ink.StartsWith("Ink", StringComparison.Ordinal) ? ink[3..] : ink;
}
