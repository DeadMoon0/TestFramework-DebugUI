using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Board.Comparison;

using System.Windows.Media.Imaging;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Detail;

// Setting two pictures side by side when what changed is something to look at.
public partial class UC_ValueInspector
{
    /// <summary>How wide each picture of a side-by-side comparison is decoded.</summary>
    /// <remarks>
    /// Half the single-picture width, because two of them share the well. Decoding both at full
    /// width to draw them at half would cost twice the memory to show the same thing.
    /// </remarks>
    private const int ComparedImageWidth = 600;

    /// <summary>
    /// Shows the two pictures side by side, when what changed is a picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No attempt is made to say <em>what</em> in the image differs. The run already knows they
    /// differ — the comparison decided that from the content hash, which is exact — so the only thing
    /// left is to let a reader see both, and the honest way to do that is to show both. A computed
    /// pixel overlay would be a guess dressed as an answer: two screenshots of the same page taken a
    /// second apart differ in a caret, a scrollbar and an animation frame, and highlighting all three
    /// as findings is worse than highlighting nothing.
    /// </para>
    /// <para>
    /// A side that has no picture says so rather than leaving a gap: for something added or removed
    /// the absence <em>is</em> the difference, and an empty half of a panel does not read as
    /// "the last passing run did not have this".
    /// </para>
    /// </remarks>
    /// <param name="change">The comparison to draw.</param>
    /// <returns>Whether this was a picture, and has therefore been drawn.</returns>
    private bool RenderPictureDiff(ValueChange change)
    {
        if (!IsPicture(change.Baseline) && !IsPicture(change.Current))
            return false;

        spPictures.Children.Clear();

        StackPanel pair = new() { Orientation = Orientation.Horizontal };

        pair.Children.Add(Side("LAST PASSING RUN", change.Baseline));
        pair.Children.Add(Side("THIS RUN", change.Current));

        spPictures.Children.Add(pair);

        // The verdict in words underneath, because two thumbnails can look alike at this size while
        // the files are plainly different - and the comparison knows which, by hash.
        spPictures.Children.Add(new TextBlock
        {
            Text = change.Change switch
            {
                ValueChangeKind.Changed => "The two differ: " + ValueChangeText.Difference(change, CultureInfo.CurrentCulture) + ".",
                ValueChangeKind.Added => "This run produced it; the last passing run did not.",
                ValueChangeKind.Removed => "The last passing run produced it; this run did not.",
                ValueChangeKind.Indeterminate => change.Reason ?? "The two cannot be compared.",
                _ => "The two are the same."
            },
            Foreground = (Brush)FindResource(ThemeKeys.TextFaint),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        spPictures.Visibility = Visibility.Visible;

        return true;
    }

    /// <summary>One run's half of a picture comparison, headed by which run it is.</summary>
    private UIElement Side(string heading, ValueDescription? described)
    {
        StackPanel side = new() { Margin = new Thickness(0, 0, 10, 0) };

        side.Children.Add(new TextBlock
        {
            Text = heading,
            Style = (Style)FindResource(ThemeKeys.PanelHeading),
            Margin = new Thickness(0, 0, 0, 4)
        });

        BitmapSource? picture = IsPicture(described)
            ? WidgetImages.Read(RunFiles.Resolve(described!.Body, store), described.Body?.ContentHash, ComparedImageWidth)
            : null;

        if (picture is not null)
        {
            Image image = new()
            {
                Source = picture,
                Width = ComparedImageWidth / 2.0,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            side.Children.Add(image);

            return side;
        }

        // Two different absences, and a reader acts differently on each: one run never produced this,
        // or it did and the file is not on this machine.
        side.Children.Add(new TextBlock
        {
            Text = described is null
                ? "Not produced by this run."
                : "The picture could not be read from " + (described.Body?.RelativePath ?? "its file") + ".",
            Foreground = (Brush)FindResource(ThemeKeys.TextFaint),
            FontSize = 11,
            Width = ComparedImageWidth / 2.0,
            TextWrapping = TextWrapping.Wrap
        });

        return side;
    }

    /// <summary>Whether a described thing is something to look at rather than to read.</summary>
    private static bool IsPicture(ValueDescription? described)
        => described?.Preview?.Form == DebugPreviewForm.Image;
}
