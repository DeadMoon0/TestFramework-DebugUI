using System;
using System.IO;
using System.Windows.Media.Imaging;
using TestFramework.DebugUI;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers turning a widget's file into something the window can draw.
/// </summary>
/// <remarks>
/// The rules worth pinning are the ones that fail quietly or expensively: a file left open against a
/// folder the user is free to delete, a four-megapixel screenshot decoded whole for a 28-pixel card,
/// and a missing picture taking the panel down with it.
/// </remarks>
public sealed class WidgetImagesTests : IDisposable
{
    /// <summary>A 64×36 PNG, so decoding to a smaller width is observable.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAAAkCAIAAABlnnySAAAAJUlEQVR4nO3BAQ0AAADCoPdPbQ8HFAAAAAAAAAAAAAAAAAAA8G0hAAABs1u3ZwAAAABJRU5ErkJggg==");

    private readonly string directory = Path.Combine(Path.GetTempPath(), "tf-widget-images-" + Guid.NewGuid().ToString("N"));

    public WidgetImagesTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void APictureIsDecodedAtAboutTheSizeItWillBeDrawn()
    {
        // A board of screenshots decoded at full resolution costs more memory than the runs they
        // came from. The card draws 28 points; nothing needs the other four megapixels.
        string path = Write("page.png");

        BitmapSource? small = WidgetImages.Read(path, "hash-small", width: 16);

        Assert.NotNull(small);
        Assert.Equal(16, small!.PixelWidth);
    }

    [Fact]
    public void TheSamePictureAtTheSameSizeIsDecodedOnce()
    {
        string path = Write("page.png");

        BitmapSource? first = WidgetImages.Read(path, "hash-cached", width: 32);
        BitmapSource? again = WidgetImages.Read(path, "hash-cached", width: 32);

        Assert.NotNull(first);
        Assert.Same(first, again);
    }

    [Fact]
    public void TheSamePictureAtTwoSizesIsTwoPictures()
    {
        // The card and the filmstrip want different sizes of the same file, and handing the card the
        // filmstrip's copy would either waste it or blur it.
        string path = Write("page.png");

        BitmapSource? small = WidgetImages.Read(path, "hash-sizes", width: 16);
        BitmapSource? large = WidgetImages.Read(path, "hash-sizes", width: 48);

        Assert.NotEqual(small!.PixelWidth, large!.PixelWidth);
    }

    [Fact]
    public void ADecodedPictureIsFrozenSoItCanBeDrawnFromAnyThread()
    {
        // Decoding does not happen on the UI thread, and an unfrozen bitmap belongs to whichever
        // thread made it — which is a cross-thread exception at the moment it is drawn.
        BitmapSource? picture = WidgetImages.Read(Write("page.png"), "hash-frozen", width: 16);

        Assert.True(picture!.IsFrozen);
    }

    [Fact]
    public void ReadingAPictureDoesNotHoldItsFileOpen()
    {
        // These files live in the user's own run output. A viewer holding them open is a viewer that
        // stops the folder being deleted or an import replacing it.
        string path = Write("page.png");

        Assert.NotNull(WidgetImages.Read(path, "hash-handle", width: 16));

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void APictureThatIsNotThereIsNotAnError()
    {
        // A shared run whose files did not travel, or output somebody cleaned up. The panel shows a
        // tile saying so; it does not take the window down.
        Assert.Null(WidgetImages.Read(Path.Combine(directory, "missing.png"), "hash-missing", width: 16));
        Assert.Null(WidgetImages.Read(null, "hash-null", width: 16));
    }

    [Fact]
    public void AFileThatIsNotAPictureIsNotAnError()
    {
        string path = Path.Combine(directory, "broken.png");
        File.WriteAllText(path, "this is not a PNG");

        Assert.Null(WidgetImages.Read(path, "hash-broken", width: 16));
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
}
