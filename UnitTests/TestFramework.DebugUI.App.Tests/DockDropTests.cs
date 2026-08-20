using System;
using System.Collections.Generic;
using System.Windows;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers where a dragged panel lands.
/// </summary>
/// <remarks>
/// The geometry of a drop is the part of dragging that is worth testing and the part that cannot be tested by
/// dragging: a pointer at a position either resolves to the right well and the right index or it does not, and
/// checking that by hand means finding one wrong answer out of thousands of positions. Pure arithmetic over a
/// list of rectangles, so all of it is assertable without a window.
/// </remarks>
public class DockDropTests
{
    private static readonly Size Host = new(1000, 800);

    [Fact]
    public void DroppingOnTheTopHalfOfACardGoesAboveIt()
    {
        // The gesture that decides which of two panels is on top, which is the reader's call and not the tool's.
        DockDrop? drop = DockDrop.Resolve(new Point(100, 60), Host, Rail(), PanelId.Step);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Left, drop.Side);
        Assert.Equal(0, drop.Index);
        Assert.True(drop.IsInsertion);
    }

    [Fact]
    public void DroppingOnTheBottomHalfOfACardGoesBelowIt()
    {
        DockDrop? drop = DockDrop.Resolve(new Point(100, 340), Host, Rail(), PanelId.Step);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Left, drop.Side);
        Assert.Equal(1, drop.Index);
        Assert.True(drop.IsInsertion);
    }

    [Fact]
    public void DroppingBelowTheSecondCardGoesLast()
    {
        DockDrop? drop = DockDrop.Resolve(new Point(100, 700), Host, Rail(), PanelId.Step);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Left, drop.Side);
        Assert.Equal(2, drop.Index);
    }

    [Fact]
    public void AnInsertionIsHintedAsALineRatherThanAnArea()
    {
        // A filled region would claim the panel was about to cover the card it is only going above.
        DockDrop? drop = DockDrop.Resolve(new Point(100, 340), Host, Rail(), PanelId.Step);

        Assert.NotNull(drop);
        Assert.True(drop.IsInsertion);
        Assert.True(drop.Hint.Height < 8);
        Assert.Equal(200, drop.Hint.Width);
    }

    [Fact]
    public void ACardCannotBeDroppedEitherSideOfItself()
    {
        // Both halves of a card are, for the panel already in it, a move that changes nothing. Reported as no
        // drop so that no hint appears where releasing would do nothing.
        Assert.Null(DockDrop.Resolve(new Point(100, 60), Host, Rail(), PanelId.Runs));
        Assert.Null(DockDrop.Resolve(new Point(100, 340), Host, Rail(), PanelId.Runs));
    }

    [Fact]
    public void ACardStillTakesADropAimedAtItsNeighbour()
    {
        // Dragging the top card below the bottom one is a real move, so it is offered even though the drag
        // started in the same well.
        DockDrop? drop = DockDrop.Resolve(new Point(100, 700), Host, Rail(), PanelId.Runs);

        Assert.NotNull(drop);
        Assert.Equal(2, drop.Index);
    }

    [Fact]
    public void TheEdgesAnswerWhereNoCardIs()
    {
        // What makes an empty side reachable: there is no card there to aim at, so the band is the target.
        Assert.Equal(DockSide.Left, DockDrop.Resolve(new Point(40, 400), Host, [], PanelId.Runs)!.Side);
        Assert.Equal(DockSide.Right, DockDrop.Resolve(new Point(960, 400), Host, [], PanelId.Runs)!.Side);
        Assert.Equal(DockSide.Bottom, DockDrop.Resolve(new Point(500, 760), Host, [], PanelId.Runs)!.Side);
        Assert.Equal(DockSide.Center, DockDrop.Resolve(new Point(500, 300), Host, [], PanelId.Runs)!.Side);
    }

    [Fact]
    public void AnEdgeDropGoesAfterWhateverIsAlreadyThere()
    {
        // The end of the well, not the front: a panel thrown at a side is not asking to displace what is there.
        DockDrop? drop = DockDrop.Resolve(new Point(960, 400), Host, Rail(), PanelId.Runs);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Right, drop.Side);
        Assert.Equal(0, drop.Index);
    }

    [Fact]
    public void ADropOnTheCentreJoinsItsStripRatherThanSplittingIt()
    {
        // The centre is tabbed, so there is no above or below in it to choose.
        List<DockCard> centre =
        [
            new DockCard { Panel = PanelId.Home, Side = DockSide.Center, Index = 0, Bounds = new Rect(200, 0, 600, 700) }
        ];

        DockDrop? drop = DockDrop.Resolve(new Point(500, 300), Host, centre, PanelId.Step);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Center, drop.Side);
        Assert.False(drop.IsInsertion);
    }

    [Fact]
    public void TheBottomBandBeatsTheCentreCardThatCoversIt()
    {
        // Found by dragging, not by reading. The centre card fills the canvas, so with the bottom well empty it
        // covers the band as well — and a drop along the bottom silently became another tab in the centre. The
        // bands are asked before the centre precisely so an empty side stays reachable.
        List<DockCard> centre =
        [
            new DockCard { Panel = PanelId.Home, Side = DockSide.Center, Index = 0, Bounds = new Rect(0, 0, 1000, 800) }
        ];

        DockDrop? drop = DockDrop.Resolve(new Point(500, 700), Host, centre, PanelId.Values);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Bottom, drop.Side);
    }

    [Fact]
    public void ARailCardStillBeatsTheBandItSitsIn()
    {
        // The other half of that ordering: a rail is consulted first, so choosing above or below inside it keeps
        // working right up against the window edge.
        DockDrop? drop = DockDrop.Resolve(new Point(100, 700), Host, Rail(), PanelId.Step);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Left, drop.Side);
        Assert.True(drop.IsInsertion);
    }

    [Fact]
    public void AHostWithNoSizeYetHasNowhereToDropAnything()
    {
        // A window mid-open, or one being restored. Answering with a side would place a panel against a measurement
        // that does not exist.
        Assert.Null(DockDrop.Resolve(new Point(0, 0), new Size(0, 0), [], PanelId.Runs));
    }

    /// <summary>Two cards stacked down the left, as the default arrangement draws them.</summary>
    private static List<DockCard> Rail() =>
    [
        new DockCard { Panel = PanelId.Runs, Side = DockSide.Left, Index = 0, Bounds = new Rect(0, 0, 200, 400) },
        new DockCard { Panel = PanelId.Values, Side = DockSide.Left, Index = 1, Bounds = new Rect(0, 400, 200, 400) }
    ];
}
