using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI;

/// <summary>
/// Turns a widget's file into something WPF can draw, once per picture.
/// </summary>
/// <remarks>
/// <para>
/// Three things have to be right or a board full of screenshots costs more than the run it is
/// showing. The bitmap is decoded to the size it will be drawn at rather than at full resolution,
/// because a card thumbnail does not need four megapixels. It is loaded and the file handle closed
/// immediately, because these files live in the user's run output and a viewer holding them open is
/// a viewer that stops the folder being deleted. And it is frozen, because decoding happens off the
/// UI thread and an unfrozen bitmap belongs to whichever thread made it.
/// </para>
/// <para>
/// Keyed by content hash rather than by path: the same picture is drawn on a card, in a filmstrip and
/// in the inspector, and after a shared run is imported it is drawn from a different path than the one
/// recorded. The hash is what says those are one image.
/// </para>
/// </remarks>
internal static class WidgetImages
{
    /// <summary>
    /// How many decoded pictures are kept.
    /// </summary>
    /// <remarks>
    /// A bound rather than a hope: watch mode leaves the tool open for days, and a run of a long
    /// browser suite has a widget per action. Past this the least recently asked for is dropped and
    /// decoded again if it is wanted, which costs a file read rather than correctness.
    /// </remarks>
    private const int Capacity = 200;

    private static readonly object Gate = new();

    /// <summary>The decoded pictures, and the order they were last asked for.</summary>
    private static readonly Dictionary<string, BitmapSource> Decoded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Recency = new();

    /// <summary>
    /// Reads one widget's picture at about the size it will be drawn.
    /// </summary>
    /// <param name="path">The file, already resolved to somewhere that exists.</param>
    /// <param name="contentHash">What identifies the picture, whatever path it arrived by.</param>
    /// <param name="width">
    /// Roughly how wide it will be drawn, in pixels. Decoding smaller than this shows; decoding much
    /// larger costs memory nothing looks at.
    /// </param>
    /// <returns>The picture, or <see langword="null"/> when it could not be read.</returns>
    /// <remarks>
    /// Nothing here throws. A missing or unreadable widget is a tile that says so, and a debugger
    /// that fell over because a screenshot was half-written would be failing at its one job.
    /// </remarks>
    public static BitmapSource? Read(string? path, string? contentHash, int width)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string key = string.IsNullOrWhiteSpace(contentHash) ? path : $"{contentHash}@{width}";

        lock (Gate)
        {
            if (Decoded.TryGetValue(key, out BitmapSource? cached))
            {
                Touch(key);
                return cached;
            }
        }

        BitmapSource? decoded = Decode(path, width);

        if (decoded is null)
            return null;

        lock (Gate)
        {
            Decoded[key] = decoded;
            Touch(key);
            Evict();
        }

        return decoded;
    }

    private static BitmapSource? Decode(string path, int width)
    {
        try
        {
            // Over a stream rather than a UriSource, and OnLoad rather than the default: WPF would
            // otherwise keep the file open for as long as the image is alive, against a folder the
            // user is free to delete or an import is about to replace.
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.DecodePixelWidth = width;
            image.StreamSource = file;
            image.EndInit();

            // Decoding may happen off the UI thread, and an unfrozen bitmap belongs to the thread
            // that made it. Frozen, it can be drawn anywhere and never copied again.
            image.Freeze();

            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Write($"Reading the widget at {path}", exception);
            return null;
        }
    }

    /// <summary>Moves a key to the front of the recency list.</summary>
    private static void Touch(string key)
    {
        Recency.Remove(key);
        Recency.AddFirst(key);
    }

    private static void Evict()
    {
        while (Recency.Count > Capacity && Recency.Last is { } oldest)
        {
            Decoded.Remove(oldest.Value);
            Recency.RemoveLast();
        }
    }
}
