using System.Windows;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>How much of the window the pinned wells have reserved, as fractions.</summary>
/// <remarks>
/// Fractions rather than a <c>Thickness</c> so that nothing in the host has to know how big anything is. The
/// window multiplies them by the area it is giving the board, which is a size it holds anyway.
/// </remarks>
public sealed record DockInsets
{
    /// <summary>Nothing reserved.</summary>
    public static DockInsets None { get; } = new();

    /// <summary>Gets the fraction reserved down the left.</summary>
    public double Left { get; init; }

    /// <summary>Gets the fraction reserved down the right.</summary>
    public double Right { get; init; }

    /// <summary>Gets the fraction reserved across the bottom.</summary>
    public double Bottom { get; init; }

    /// <summary>The margin these reserve inside an area of the given size.</summary>
    public Thickness Against(double width, double height)
        => new(Left * width, 0, Right * width, Bottom * height);
}
