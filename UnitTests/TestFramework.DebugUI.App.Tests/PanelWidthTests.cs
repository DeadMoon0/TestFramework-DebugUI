using TestFramework.DebugUI.Controls.Detail;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the bounds on the step panel's width.
/// </summary>
/// <remarks>
/// The drag itself needs a window; what a drag is allowed to arrive at does not, and that is the part that has
/// to hold for a width read out of a settings file written on somebody else's screen.
/// </remarks>
public class PanelWidthTests
{
    [Fact]
    public void ANeverDraggedPanelOpensAtTheDefault()
    {
        // Which is what a fresh settings file holds: zero, meaning nobody has expressed a preference.
        Assert.Equal(PanelWidth.Default, PanelWidth.Clamp(0, 1500));
        Assert.Equal(PanelWidth.Default, PanelWidth.Clamp(double.NaN, 1500));
    }

    [Fact]
    public void ThePanelStaysWideEnoughToReadAStackTraceIn()
    {
        Assert.Equal(PanelWidth.Minimum, PanelWidth.Clamp(40, 1500));
    }

    [Fact]
    public void TheBoardKeepsRoomWhateverThePanelIsDraggedTo()
    {
        // Dragged to the far side of a 1000-wide window, the panel stops with the reserve still showing: the
        // board is the application, and a panel that can cover all of it can hide what it describes.
        Assert.Equal(1000 - PanelWidth.Reserved, PanelWidth.Clamp(2000, 1000));
    }

    [Fact]
    public void ThePanelDoesNotGrowPastBeingAPanel()
    {
        Assert.Equal(PanelWidth.Maximum, PanelWidth.Clamp(4000, 4000));
    }

    [Fact]
    public void AWindowTooNarrowForBothGivesUpTheReserveRatherThanTheMinimum()
    {
        // 380 wide: honouring the reserve would leave 180, which is a panel nobody can read. Readable and
        // covering most of the board beats neither.
        Assert.Equal(PanelWidth.Minimum, PanelWidth.Clamp(PanelWidth.Default, 380));
    }

    [Fact]
    public void AWidthAppliedBeforeTheWindowIsMeasuredKeepsItsFixedBounds()
    {
        // Applied in the constructor, before there is a laid-out window to measure against.
        Assert.Equal(500, PanelWidth.Clamp(500, 0));
        Assert.Equal(PanelWidth.Maximum, PanelWidth.Clamp(2000, double.NaN));
    }

    [Fact]
    public void AWidthAlreadyAllowedComesBackUntouched()
    {
        Assert.Equal(520, PanelWidth.Clamp(520, 1500));
    }
}
