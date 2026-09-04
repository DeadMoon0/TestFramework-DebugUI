using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Copying;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// One widget a step produced, drawn in a frame of its own.
/// </summary>
/// <remarks>
/// <para>
/// A step can show anything: the page it was looking at, the document it generated, what a container
/// printed. What they have in common is not their content — it is that they belong to a step, have a
/// name, and can be opened. That is what this frame carries, and why the middle of it is left
/// entirely to whatever kind the widget turned out to be.
/// </para>
/// <para>
/// Which renderer the middle gets is chosen from the described form rather than from the kind, so a
/// kind this build has never heard of still draws its content if it is a picture or text. An unknown
/// kind is a label; an unknown <em>form</em> is the only thing that falls back to naming the file.
/// </para>
/// </remarks>
public partial class UC_Widget : UserControl
{
    /// <summary>How wide a widget is drawn, and how wide its picture is decoded.</summary>
    /// <remarks>
    /// Wide enough that a page's own layout survives — a heading, a row of controls, a column of
    /// items — which is the width at which a thumbnail stops being a coloured rectangle and starts
    /// being the page it came from.
    /// </remarks>
    public const int FrameWidth = 300;

    private const int ContentHeight = 170;

    private WidgetNode? shown;

    /// <summary>Creates an empty frame.</summary>
    public UC_Widget()
    {
        InitializeComponent();

        Width = FrameWidth;
    }

    /// <summary>Raised when the reader asks to see this widget on its own.</summary>
    public event Action<WidgetNode>? Expanded;

    /// <summary>Draws a widget.</summary>
    public void Show(WidgetNode widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        shown = widget;

        tbTitle.Text = widget.Attempt is > 1 ? $"{widget.Name} · attempt {widget.Attempt}" : widget.Name;

        // The producer's own line, and only when it says something the name does not.
        string summary = widget.Description.Summary;
        bool worthSaying = summary.Length > 0 && !string.Equals(summary, widget.Name, StringComparison.Ordinal);

        tbSummary.Text = worthSaying ? summary : string.Empty;
        tbSummary.Visibility = worthSaying ? Visibility.Visible : Visibility.Collapsed;

        tbFoot.Text = Foot(widget);

        bContent.Height = ContentHeight;
        bContent.Child = Middle(widget);

        bool openable = RunFiles.Resolve(widget.Description.Body) is not null;

        btOpen.IsEnabled = openable;
        btCopy.IsEnabled = widget.Description.Body is not null;
    }

    /// <summary>
    /// What the middle of the frame shows.
    /// </summary>
    /// <remarks>
    /// A picture is drawn, text is read, and anything else says what it is. Deliberately a short list:
    /// every renderer here is one every consumer of a widget has to have, so the vocabulary stays
    /// small enough that a new kind is a label rather than a new way of drawing.
    /// </remarks>
    private UIElement Middle(WidgetNode widget)
    {
        // Twice the frame's width, so the picture still reads on a high-density display. The
        // decisions around it are shared with the board, which draws the same widget at card width.
        if (WidgetFaces.IsPicture(widget))
        {
            return WidgetFaces.PictureOf(widget, FrameWidth * 2) is { } picture
                ? WidgetFaces.Draw(picture)
                : Missing("the picture is not on this machine");
        }

        return WidgetFaces.TextOf(widget) is { } text
            ? Lines(text)
            : Missing(widget.Description.Body?.RelativePath ?? "nothing to show");
    }

    private UIElement Lines(string text) => new TextBox
    {
        Text = text,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = (Brush)FindResource(ThemeKeys.TextSecondary),
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
        FontSize = 10.5,
        Padding = new Thickness(8, 6, 6, 6),
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private UIElement Missing(string why) => new TextBlock
    {
        Style = (Style)FindResource(ThemeKeys.MutedText),
        FontSize = 10.5,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(12, 0, 12, 0),
        Text = why
    };

    /// <summary>What kind of thing this is and how big it was.</summary>
    private static string Foot(WidgetNode widget)
    {
        string kind = widget.Kind.StartsWith("tf.widget.", StringComparison.Ordinal)
            ? widget.Kind["tf.widget.".Length..]
            : widget.Kind;

        return widget.Description.Body is { } body
            ? $"{kind} · {ValueInspection.Size(body.SizeInBytes, CultureInfo.CurrentCulture)}"
            : kind;
    }

    private void btOpen_Click(object sender, RoutedEventArgs e)
    {
        if (shown is not null && RunFiles.Resolve(shown.Description.Body) is { } path)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    private void btCopy_Click(object sender, RoutedEventArgs e)
    {
        if (shown?.Description.Body is { } body)
            Clipboards.Set(RunFiles.Resolve(body) ?? body.RelativePath);
    }

    private void btExpand_Click(object sender, RoutedEventArgs e) => Expand();

    private void bContent_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => Expand();

    private void Expand()
    {
        if (shown is not null)
            Expanded?.Invoke(shown);
    }
}
