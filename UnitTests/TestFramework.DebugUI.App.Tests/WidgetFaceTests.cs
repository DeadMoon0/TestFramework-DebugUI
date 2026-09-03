using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Controls.Detail;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers what a widget actually looks like once it is drawn.
/// </summary>
/// <remarks>
/// The board and the step panel draw the same widget at different sizes and with different chrome,
/// and they share the decisions underneath. What is checked here is that each form reaches the right
/// treatment — because the failure mode is silent: a picture that cannot be read draws nothing at
/// all, and a panel showing nothing looks exactly like a step that produced nothing.
/// </remarks>
public sealed class WidgetFaceTests : IDisposable
{
    /// <summary>A 64×36 PNG, so a decoded picture is a real one.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAAAkCAIAAABlnnySAAAAJUlEQVR4nO3BAQ0AAADCoPdPbQ8HFAAAAAAAAAAAAAAAAAAA8G0hAAABs1u3ZwAAAABJRU5ErkJggg==");

    private readonly string directory = Path.Combine(Path.GetTempPath(), "tf-widget-faces-" + Guid.NewGuid().ToString("N"));

    public WidgetFaceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void APictureIsDrawn()
    {
        string path = Write("page.png");

        Wpf.Run(() =>
        {
            UC_Widget frame = new();
            frame.Show(Picture(path, "drawn-hash"));

            // Nothing in WPF exists in the visual tree until it has been laid out; a control that
            // was only constructed has no children to find.
            Wpf.Layout(frame, 300, 400);

            Image image = Assert.Single(Wpf.Descendants<Image>(frame));

            Assert.NotNull(image.Source);
        });
    }

    [Fact]
    public void SomethingReadableIsDrawnAsTextTheReaderCanSelect()
    {
        // A TextBox rather than a TextBlock, and that is the point of the case: somebody looking at a
        // console log in a panel wants to take a line out of it.
        Wpf.Run(() =>
        {
            UC_Widget frame = new();
            frame.Show(Readable("listening on :8080"));

            // Nothing in WPF exists in the visual tree until it has been laid out; a control that
            // was only constructed has no children to find.
            Wpf.Layout(frame, 300, 400);

            TextBox box = Assert.Single(Wpf.Descendants<TextBox>(frame));

            Assert.Equal("listening on :8080", box.Text);
            Assert.True(box.IsReadOnly);
        });
    }

    [Fact]
    public void APictureThatIsNotOnThisMachineSaysSoRatherThanDrawingNothing()
    {
        // What a run shared from somebody else's machine looks like, and what a run whose output has
        // been cleaned up looks like. An empty frame would read as "this step produced nothing",
        // which is the one thing it does not mean.
        Wpf.Run(() =>
        {
            UC_Widget frame = new();
            frame.Show(Picture(Path.Combine(directory, "gone.png"), "missing-hash"));

            // Nothing in WPF exists in the visual tree until it has been laid out; a control that
            // was only constructed has no children to find.
            Wpf.Layout(frame, 300, 400);

            Assert.Empty(Wpf.Descendants<Image>(frame));

            Assert.Contains(
                Wpf.Descendants<TextBlock>(frame),
                block => block.Text.Contains("not on this machine", StringComparison.Ordinal));
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test is not a test failure.
        }
    }

    private string Write(string name)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, Png);
        return path;
    }

    private static WidgetNode Picture(string path, string hash) => new()
    {
        Kind = WidgetKinds.Screenshot,
        Name = "confirmation",
        OccurredAtUtc = DateTimeOffset.UnixEpoch,
        Description = new ValueDescription
        {
            Summary = "the confirmation page",
            Shape = DebugValueShape.Binary,
            Preview = new ValuePreview { Form = DebugPreviewForm.Image, Text = string.Empty, IsTruncated = true },
            Body = new ValueBody
            {
                Path = path,
                RelativePath = "widgets/" + Path.GetFileName(path),
                SizeInBytes = 120,
                ContentHash = hash
            }
        }
    };

    private static WidgetNode Readable(string text) => new()
    {
        Kind = WidgetKinds.LogStream,
        Name = "console",
        OccurredAtUtc = DateTimeOffset.UnixEpoch,
        Description = new ValueDescription
        {
            Summary = "what the page complained about",
            Shape = DebugValueShape.Text,
            Preview = new ValuePreview { Form = DebugPreviewForm.Text, Text = text }
        }
    };
}
