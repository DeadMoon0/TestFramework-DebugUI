using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// What a widget looks like, for the two surfaces that draw one.
/// </summary>
/// <remarks>
/// <para>
/// The board draws a widget into the face of a card and the step panel draws it into a framed item
/// with buttons, and they legitimately want different chrome: a card's face is scenery a reader scans
/// past, a panel's item is something they select text out of. What they must not decide separately is
/// what is underneath — which form is a picture, what stands in when it cannot be read, and what a
/// widget's readable content actually is. Two copies of that drift the moment a form is added to one
/// of them.
/// </para>
/// <para>
/// The width is the caller's, and deliberately so: a card face is as wide as a step and a panel item
/// is half that, and decoding a screenshot to the wrong one either wastes memory or shows a blur.
/// </para>
/// </remarks>
internal static class WidgetFaces
{
    /// <summary>Whether this widget is something to look at rather than to read.</summary>
    public static bool IsPicture(WidgetNode widget)
        => widget.Description.Preview?.Form == DebugPreviewForm.Image;

    /// <summary>
    /// The widget's picture at about the width it will be drawn, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Null is a real answer rather than a failure: a run shared from another machine, or one whose
    /// output has been cleaned up, has the description without the file. Each caller says so in its
    /// own words, because there is more room to say it in a panel than on a card.
    /// </remarks>
    public static BitmapSource? PictureOf(WidgetNode widget, int width)
        => WidgetImages.Read(RunFiles.Resolve(widget.Description.Body), widget.Description.Body?.ContentHash, width);

    /// <summary>
    /// A picture drawn from its top-left corner, filling the space it is given.
    /// </summary>
    /// <remarks>
    /// Filled rather than fitted, and anchored rather than centred, because these are pictures of
    /// pages: a page starts at its top-left, and letterboxing one inside a card wastes the room the
    /// card was made taller for.
    /// </remarks>
    public static Image Draw(BitmapSource picture)
    {
        Image image = new()
        {
            Source = picture,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        return image;
    }

    /// <summary>
    /// The widget's content as text, or null when it is not something to read.
    /// </summary>
    /// <remarks>
    /// A picture answers null here rather than the hex of a PNG, which is what a form-blind reader of
    /// the preview would produce.
    /// </remarks>
    public static string? TextOf(WidgetNode widget)
        => widget.Description.Preview?.Form is DebugPreviewForm.Text or DebugPreviewForm.Json or DebugPreviewForm.Markup
            ? ValueInspection.PreviewText(widget.Description.Preview)
            : null;
}
