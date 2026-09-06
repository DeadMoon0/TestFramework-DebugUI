using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TestFramework.DebugUI.State.Theming;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Shell;

/// <summary>
/// The blurred shapes behind the application.
/// </summary>
/// <remarks>
/// <para>
/// Told what to draw rather than knowing: the shapes come from <see cref="BackdropPainter"/> and the
/// colours from the theme, so this holds nothing but the two properties that turn a drawing into
/// something on screen.
/// </para>
/// <para>
/// It is also the only part of the theme that is a copy rather than a reference. Every other surface in
/// the window points at a brush the theme owns and follows it for free; a drawing built from those
/// colours has to be built again when they change, which is what <see cref="Show"/> is for.
/// </para>
/// </remarks>
public partial class UC_Background : UserControl
{
    /// <summary>Creates the background, showing nothing until it is given a theme.</summary>
    public UC_Background()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Draws the theme's backdrop.
    /// </summary>
    /// <remarks>
    /// A theme that paints nothing takes the control out of the tree entirely rather than filling it
    /// with a transparent brush. The difference is a blur pass and a cached bitmap the size of the
    /// window, spent on a rectangle nobody can see.
    /// </remarks>
    internal void Show(ThemeBackdrop backdrop, BackdropInk ink)
    {
        ArgumentNullException.ThrowIfNull(backdrop);

        if (backdrop.Recipe == BackdropRecipe.Clear && ink.Ground.A == 0)
        {
            Visibility = Visibility.Collapsed;
            rBackdrop.Fill = null;

            return;
        }

        Visibility = Visibility.Visible;

        rBackdrop.Fill = new DrawingBrush(BackdropPainter.Paint(backdrop.Recipe, ink))
        {
            // Pinned to the space the painter draws in rather than left to the drawing's own bounds:
            // a recipe whose shapes do not reach the edges would otherwise be stretched to fill.
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, BackdropPainter.Width, BackdropPainter.Height)
        };

        rBackdrop.Effect = backdrop.SafeBlur > 0
            ? new BlurEffect
            {
                Radius = backdrop.SafeBlur,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
            : null;
    }
}
