using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TestFramework.DebugUI.Copying;

/// <summary>
/// Makes a text field copyable.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="TextBlock"/> cannot be selected. Everything this window shows is one — a test's qualified
/// name, a file reference, a failure message, a variable's contents — so all of it was text you could read
/// and not take. That is fine for a label and useless for a value, and this tool is mostly values.
/// </para>
/// <para>
/// Attached rather than a control wrapping each field, and drawn in the adorner layer rather than in the
/// field's own panel, so switching a field on costs one attribute and changes no layout anywhere. Dozens of
/// fields with a button reserving space beside each would have re-laid-out every panel in the application.
/// </para>
/// <para>
/// Shown on hover. A button permanently beside forty fields is forty buttons; on hover it is one, over the
/// thing the pointer is already on.
/// </para>
/// </remarks>
public static class Copyable
{
    /// <summary>
    /// What to copy, when it is not simply the field's own text.
    /// </summary>
    /// <remarks>
    /// For fields showing a shortened form of something longer. The board's title shows a test's method
    /// name; what a reader wants on the clipboard is the qualified name they can paste into a filter.
    /// </remarks>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(Copyable),
        new PropertyMetadata(default(string)));

    /// <summary>Whether this field offers a copy button.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(Copyable),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Sets what a field copies.</summary>
    public static void SetText(DependencyObject element, string? value)
        => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(TextProperty, value);

    /// <summary>Gets what a field copies.</summary>
    public static string? GetText(DependencyObject element)
        => (string?)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(TextProperty);

    /// <summary>Turns the copy button on for a field.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value)
        => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(IsEnabledProperty, value);

    /// <summary>Whether a field has its copy button turned on.</summary>
    public static bool GetIsEnabled(DependencyObject element)
        => (bool)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(IsEnabledProperty);

    /// <summary>
    /// Turns the copy button on for several fields at once.
    /// </summary>
    /// <remarks>
    /// Called from a panel's constructor rather than set in its markup, which is where this application
    /// already does the same kind of wiring — the tooltips carrying keyboard gestures are set the same way,
    /// so the list of what a panel offers reads in one place instead of being scattered over its markup.
    /// </remarks>
    public static void Enable(params DependencyObject[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        foreach (DependencyObject field in fields)
            SetIsEnabled(field, true);
    }

    /// <summary>
    /// What a field would copy right now.
    /// </summary>
    /// <remarks>
    /// The override first, then the field's own text. Public because it is the whole contract — what comes
    /// out of a field is worth being able to check without a window.
    /// </remarks>
    public static string? TextOf(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return Clipboards.Clean(GetText(element)) ?? Clipboards.Clean(element is TextBlock block ? block.Text : null);
    }

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement field)
            return;

        if (e.NewValue is not true)
        {
            field.MouseEnter -= Enter;
            return;
        }

        // Text hit-tests only where its glyphs are, so hovering the gap between two words would lose the
        // button again. A transparent background makes the field's whole box the target.
        if (field is TextBlock { Background: null } block)
            block.Background = Brushes.Transparent;

        field.MouseEnter += Enter;
    }

    /// <summary>
    /// Puts the button up. Taking it down again is the adorner's own business.
    /// </summary>
    /// <remarks>
    /// Removing it from the field's <c>MouseLeave</c> was tried first and made the button unusable: moving the
    /// pointer onto the button is a pointer leaving the field, so the button was withdrawn from under the
    /// click that was about to happen. The adorner watches both itself and the field instead.
    /// </remarks>
    private static void Enter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement field || TextOf(field) is null)
            return;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(field);

        if (layer is null || Find(layer, field) is not null)
            return;

        layer.Add(new CopyAdorner(field));
    }

    private static Adorner? Find(AdornerLayer layer, UIElement field)
    {
        Adorner[]? existing = layer.GetAdorners(field);

        return existing is { Length: > 0 } ? existing[0] : null;
    }
}

/// <summary>
/// The copy button that appears over a field.
/// </summary>
/// <remarks>
/// Positioned just past the field's right edge so it never covers the text it would copy, and confirms with
/// a tick — the clipboard is invisible, so a copy with no acknowledgement is indistinguishable from a button
/// that does nothing.
/// </remarks>
internal sealed class CopyAdorner : Adorner
{
    private static readonly TimeSpan Acknowledgement = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// How long the button survives the pointer leaving.
    /// </summary>
    /// <remarks>
    /// Long enough to cross the gap between the text and the button without losing it, short enough that it
    /// is gone before anyone wonders why it is still there.
    /// </remarks>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);

    private readonly VisualCollection visuals;
    private readonly Button button;
    private readonly Path glyph;
    private readonly FrameworkElement field;
    private readonly DispatcherTimer close;

    private DispatcherTimer? revert;

    internal CopyAdorner(FrameworkElement adorned)
        : base(adorned)
    {
        field = adorned;
        visuals = new VisualCollection(this);

        glyph = new Path
        {
            Data = (Geometry)adorned.FindResource("IconCopy"),
            Stroke = (Brush)adorned.FindResource("TextSecondary"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 18,
            Height = 18,
            Stretch = Stretch.None
        };

        button = new Button
        {
            Style = (Style)adorned.FindResource("IconButton"),
            Background = (Brush)adorned.FindResource("SurfaceRaised"),
            Cursor = Cursors.Hand,
            ToolTip = "Copy",
            Content = glyph
        };

        button.Click += OnClick;

        visuals.Add(button);

        // The pointer moving between the text and the button leaves one and enters the other, so neither
        // leaving on its own means anything. Only being over neither of them, for a moment, does.
        close = new DispatcherTimer { Interval = Grace };
        close.Tick += (_, _) =>
        {
            if (!field.IsMouseOver && !IsMouseOver)
                Detach();
        };

        field.MouseEnter += OnEnter;
        field.MouseLeave += OnLeave;
        field.Unloaded += OnUnloaded;

        MouseEnter += OnEnter;
        MouseLeave += OnLeave;
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => visuals.Count;

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index) => visuals[index];

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        button.Measure(constraint);
        return base.MeasureOverride(constraint);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size wanted = button.DesiredSize;

        // Inside the field's own right edge. Outside it was tried first and is wrong here: these fields
        // stretch to their panel's width, so "just past the right edge" is past the panel too, and the
        // button was clipped away by the scroll viewer. Inside costs a few characters of the field's widest
        // line while the pointer is on it, which is a trade worth making for a button that is actually there.
        double top = finalSize.Height > 40 ? 2 : (finalSize.Height - wanted.Height) / 2;
        double left = Math.Max(0, finalSize.Width - wanted.Width - 2);

        button.Arrange(new Rect(new Point(left, top), wanted));

        return finalSize;
    }

    private void OnEnter(object sender, MouseEventArgs e) => close.Stop();

    private void OnLeave(object sender, MouseEventArgs e)
    {
        close.Stop();
        close.Start();
    }

    /// <summary>Goes away when the panel holding the field does, rather than outliving it in the layer.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Detach()
    {
        close.Stop();
        revert?.Stop();

        field.MouseEnter -= OnEnter;
        field.MouseLeave -= OnLeave;
        field.Unloaded -= OnUnloaded;

        AdornerLayer.GetAdornerLayer(field)?.Remove(this);
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (!Clipboards.Set(Copyable.TextOf(field)))
            return;

        glyph.Data = (Geometry)field.FindResource("IconTick");
        glyph.Stroke = (Brush)field.FindResource("StateComplete");

        revert?.Stop();
        revert = new DispatcherTimer { Interval = Acknowledgement };
        revert.Tick += (_, _) =>
        {
            revert?.Stop();
            revert = null;

            glyph.Data = (Geometry)field.FindResource("IconCopy");
            glyph.Stroke = (Brush)field.FindResource("TextSecondary");
        };
        revert.Start();
    }
}
