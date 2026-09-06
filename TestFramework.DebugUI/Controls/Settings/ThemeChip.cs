using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Settings;

/// <summary>
/// One theme, as something to look at and click.
/// </summary>
/// <remarks>
/// <para>
/// A picture of the theme rather than its name and a swatch. The backdrop in the preview is the real
/// one, drawn by the same painter the window uses, in the same four colours — so the difference between
/// Hexfield and Orbits, or between a theme that paints something and one that paints nothing, is
/// visible before it is chosen rather than after.
/// </para>
/// <para>
/// Every colour in a chip comes from the theme it is showing, never from the theme that is currently
/// on. A row of previews all wearing the current accent would be a row of identical pictures.
/// </para>
/// </remarks>
internal static class ThemeChip
{
    private const double PreviewWidth = 62;
    private const double PreviewHeight = 40;

    /// <summary>How far a chip fades when its theme cannot be delivered. Faded, not hidden: it is still an offer.</summary>
    private const double DimmedWhenUnavailable = 0.4;

    /// <summary>Builds the chip for one theme.</summary>
    /// <param name="theme">The theme to show.</param>
    /// <param name="chosen">Whether it is the one currently on.</param>
    /// <param name="owner">Where the chip's own colours — its border, its label — are looked up.</param>
    /// <param name="block">
    /// Why the compositor is not blurring, if it is not. Only a theme that shows what is behind it
    /// cares: the rest paint their own ground and look the same either way.
    /// </param>
    public static Button Build(ThemeDefinition theme, bool chosen, FrameworkElement owner, BlurBlock block = BlurBlock.None)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(owner);

        bool unavailable = theme.ShowsWhatIsBehind && block != BlurBlock.None;

        StackPanel content = new() { Orientation = Orientation.Vertical };

        content.Children.Add(Preview(theme));
        content.Children.Add(new TextBlock
        {
            Text = theme.Name,
            Foreground = (Brush)owner.FindResource(chosen ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary),
            FontSize = 10,
            FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = PreviewWidth,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        Button chip = new()
        {
            Content = content,
            Cursor = unavailable ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(5),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)owner.FindResource(chosen ? ThemeKeys.Accent : ThemeKeys.PanelEdge),
            Background = (Brush)owner.FindResource(chosen ? ThemeKeys.SurfaceRaised : ThemeKeys.SurfaceSunken),
            Template = (ControlTemplate)owner.FindResource(ThemeKeys.ThemeChip),
            IsEnabled = !unavailable,
            Opacity = unavailable ? DimmedWhenUnavailable : 1,
            ToolTip = unavailable
                ? BlurSupport.Explain(theme.Name, block)
                : theme.IsCustom ? $"{theme.Name} — from {theme.Id}.json" : theme.Name
        };

        // The whole point of dimming the chip is the explanation behind it, and a disabled control does
        // not show its tooltip unless it is told to. Without this the reader is left with a theme they
        // cannot click and nothing saying why.
        ToolTipService.SetShowOnDisabled(chip, true);

        return chip;
    }

    /// <summary>
    /// A miniature of the window in that theme: its tint, its backdrop, a card and its accent.
    /// </summary>
    /// <remarks>
    /// The tint goes down first because it is what a see-through theme has instead of a backdrop —
    /// without it, every Clear theme would preview as an empty rectangle and they would all look the
    /// same as each other.
    /// </remarks>
    private static UIElement Preview(ThemeDefinition theme)
    {
        Grid layers = new()
        {
            Width = PreviewWidth,
            Height = PreviewHeight,
            ClipToBounds = true,
            Background = Fill(theme, ThemeKeys.WindowTint)
        };

        layers.Children.Add(Backdrop(theme));

        // A card with the accent on it: the two things every surface in the window is made of.
        layers.Children.Add(new Border
        {
            Width = 34,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = Fill(theme, ThemeKeys.SurfaceCard),
            BorderThickness = new Thickness(1),
            BorderBrush = Fill(theme, ThemeKeys.PanelEdge),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Fill(theme, ThemeKeys.Accent),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            }
        });

        return new Border
        {
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = layers
        };
    }

    private static UIElement Backdrop(ThemeDefinition theme)
    {
        BackdropInk ink = new(
            Colour(theme, ThemeKeys.BackdropBase),
            Colour(theme, ThemeKeys.BackdropNear),
            Colour(theme, ThemeKeys.BackdropFar),
            Colour(theme, ThemeKeys.BackdropGlow));

        Rectangle backdrop = new()
        {
            Fill = new DrawingBrush(BackdropPainter.Paint(theme.Backdrop.Recipe, ink))
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(BackdropPainter.Box(theme.Backdrop.Recipe))
            }
        };

        // The window's blur, to the same scale as the picture. Using the window's radius here would
        // reduce every backdrop to a single smear, and then all ten previews would look alike.
        double radius = theme.Backdrop.SafeBlur * PreviewWidth / BackdropPainter.Box(theme.Backdrop.Recipe).Width;

        if (radius > 0.2)
            backdrop.Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian };

        return backdrop;
    }

    private static SolidColorBrush Fill(ThemeDefinition theme, string key)
    {
        SolidColorBrush brush = new(Colour(theme, key));
        brush.Freeze();

        return brush;
    }

    private static Color Colour(ThemeDefinition theme, string key)
        => ThemeApplier.ToColor(theme.Colour(key));
}
