using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Docking.Tests;

/// <summary>
/// A float lies wholly on the monitor it was put on.
/// </summary>
public class DockBoundsTests
{
    private static readonly DockBounds Monitor = new() { Left = 0, Top = 0, Width = 1920, Height = 1040 };

    [Fact]
    public void AFloatThatAlreadyFitsStaysWhereItIs()
    {
        DockBounds placed = new() { Left = 400, Top = 300, Width = 480, Height = 560 };

        Assert.Equal(placed, placed.Within(Monitor));
    }

    [Fact]
    public void AFloatDroppedNearTheRightEdgeIsPulledBackOntoTheMonitor()
    {
        DockBounds placed = new DockBounds { Left = 1820, Top = 300, Width = 480, Height = 560 }.Within(Monitor);

        Assert.Equal(1920 - 480, placed.Left);
        Assert.Equal(300, placed.Top);
    }

    [Fact]
    public void AFloatDroppedNearTheBottomIsLiftedAboveIt()
    {
        DockBounds placed = new DockBounds { Left = 400, Top = 900, Width = 480, Height = 560 }.Within(Monitor);

        Assert.Equal(1040 - 560, placed.Top);
    }

    [Fact]
    public void AFloatOnAMonitorLeftOfThePrimaryKeepsItsNegativePosition()
    {
        DockBounds left = new() { Left = -1080, Top = -275, Width = 1080, Height = 1920 };

        DockBounds placed = new DockBounds { Left = -500, Top = 100, Width = 480, Height = 560 }.Within(left);

        Assert.Equal(-500, placed.Left);
        Assert.Equal(100, placed.Top);
    }

    [Fact]
    public void AFloatLargerThanTheMonitorIsShrunkToIt()
    {
        DockBounds small = new() { Left = 0, Top = 0, Width = 800, Height = 600 };

        DockBounds placed = new DockBounds { Left = 100, Top = 100, Width = 1000, Height = 900 }.Within(small);

        Assert.Equal(small, placed);
    }
}
