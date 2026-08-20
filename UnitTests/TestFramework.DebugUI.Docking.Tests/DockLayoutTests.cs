using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Docking.Tests;

/// <summary>
/// Covers the arrangement of panels, and the one rule everything drawn from it depends on.
/// </summary>
/// <remarks>
/// The whole point of the arrangement being a value rather than a visual tree is that these can be asserted
/// without opening a window. A docking system whose invariants are only ever exercised by dragging is one whose
/// invariants are not actually checked.
/// </remarks>
public class DockLayoutTests
{
    [Fact]
    public void EveryPanelIsSomewhereAndNowhereTwice()
    {
        // The invariant the host, the activity bar and the settings file all assume. Asserted on the default
        // arrangement because that is the one every reader starts from.
        AssertEveryPanelAppearsOnce(DockLayout.Default);
    }

    [Fact]
    public void TheDefaultIsTheArrangementTheToolAlreadyHad()
    {
        // Somebody upgrading should find their window as they left it, not a rebuilt one. The tree and the
        // values down the left, the step panel right, the runs page over the canvas.
        DockLayout layout = DockLayout.Default;

        Assert.Equal([PanelId.Runs, PanelId.Values], layout.At(DockSide.Left).Panels);
        Assert.Equal([PanelId.Step], layout.At(DockSide.Right).Panels);
        Assert.Equal([PanelId.Home], layout.At(DockSide.Center).Panels);
        Assert.True(layout.At(DockSide.Bottom).IsEmpty);

        // Opened on demand, exactly as they are today.
        Assert.False(layout.IsOpen(PanelId.Summary));
        Assert.False(layout.IsOpen(PanelId.Inspector));
    }

    [Fact]
    public void MovingAPanelTakesItOutOfWhereItWas()
    {
        DockLayout layout = DockLayout.Default.Move(PanelId.Runs, DockSide.Right, 0);

        Assert.Equal([PanelId.Values], layout.At(DockSide.Left).Panels);
        Assert.Equal([PanelId.Runs, PanelId.Step], layout.At(DockSide.Right).Panels);

        AssertEveryPanelAppearsOnce(layout);
    }

    [Fact]
    public void AMovedPanelTakesTheAttentionWhereItLands()
    {
        // Dragging a panel somewhere is asking to look at it. Landing it behind whatever was already there
        // would read as the drag having failed.
        DockLayout layout = DockLayout.Default.Move(PanelId.Summary, DockSide.Right, 0);

        Assert.Equal(PanelId.Summary, layout.At(DockSide.Right).Active);
    }

    [Fact]
    public void TakingTheActivePanelOutPromotesItsNeighbour()
    {
        DockLayout layout = DockLayout.Default
            .Move(PanelId.Summary, DockSide.Left, 2)
            .Activate(PanelId.Values)
            .Close(PanelId.Values);

        // The one that took its place, which is what closing a tab does everywhere else.
        Assert.Equal(PanelId.Summary, layout.At(DockSide.Left).Active);
    }

    [Fact]
    public void TakingTheLastPanelOutLeavesNoAttentionButKeepsTheEdge()
    {
        // The extent and the pin are preferences about the edge, not about whichever panel was there. Dragging
        // the last one out and bringing it back must not reset the width somebody chose.
        DockLayout layout = DockLayout.Default
            .Resize(DockSide.Right, 0.4)
            .Pin(DockSide.Right, pinned: true)
            .Close(PanelId.Step);

        DockWell well = layout.At(DockSide.Right);

        Assert.True(well.IsEmpty);
        Assert.Null(well.Active);
        Assert.Equal(0.4, well.Extent);
        Assert.True(well.Pinned);
    }

    [Fact]
    public void AnExtentIsHeldInsideWhatCanBeRead()
    {
        // A sliver too narrow to read but wide enough to take clicks is worse than a closed panel, and past
        // half the window a layer over the canvas has stopped being a layer over the canvas.
        Assert.Equal(DockWell.MinimumExtent, DockLayout.Default.Resize(DockSide.Left, 0.01).At(DockSide.Left).Extent);
        Assert.Equal(DockWell.MaximumExtent, DockLayout.Default.Resize(DockSide.Left, 0.9).At(DockSide.Left).Extent);
    }

    [Fact]
    public void FloatingAPanelTakesItOutOfItsWell()
    {
        DockLayout layout = DockLayout.Default.Float(PanelId.Step, new DockBounds { Left = 100, Top = 50 });

        Assert.True(layout.At(DockSide.Right).IsEmpty);

        DockFloat window = Assert.Single(layout.Floats);

        Assert.Equal([PanelId.Step], window.Panels);
        Assert.Equal(PanelId.Step, window.Active);
        Assert.Equal(100, window.Bounds.Left);

        AssertEveryPanelAppearsOnce(layout);
    }

    [Fact]
    public void AFloatThatEmptiesIsClosedRatherThanKept()
    {
        // An empty window is furniture the reader has to dismiss to be rid of something already dismissed.
        DockLayout layout = DockLayout.Default
            .Float(PanelId.Step, new DockBounds())
            .Move(PanelId.Step, DockSide.Right, 0);

        Assert.Empty(layout.Floats);
    }

    [Fact]
    public void APanelCanBeDroppedIntoAFloatThatAlreadyHasOne()
    {
        // Cross-window docking: the float's tab strip is a drop target like any well's.
        DockLayout layout = DockLayout.Default
            .Float(PanelId.Step, new DockBounds { Left = 10, Top = 20 })
            .MoveIntoFloat(PanelId.Values, floatIndex: 0, index: 0);

        DockFloat window = Assert.Single(layout.Floats);

        Assert.Equal([PanelId.Values, PanelId.Step], window.Panels);
        Assert.Equal(PanelId.Values, window.Active);
        Assert.Equal([PanelId.Runs], layout.At(DockSide.Left).Panels);

        AssertEveryPanelAppearsOnce(layout);
    }

    [Fact]
    public void MovingTheLastPanelOfOneFloatIntoAnotherDoesNotLoseIt()
    {
        // The dangerous case: taking the panel out empties its window, which renumbers the float list — so an
        // index captured before the detach would address the wrong window, or none.
        DockLayout layout = DockLayout.Default
            .Float(PanelId.Step, new DockBounds { Left = 10, Top = 10 })
            .Float(PanelId.Summary, new DockBounds { Left = 900, Top = 10 });

        layout = layout.MoveIntoFloat(PanelId.Step, floatIndex: 1, index: 0);

        DockFloat window = Assert.Single(layout.Floats);

        Assert.Equal([PanelId.Step, PanelId.Summary], window.Panels);
        Assert.Equal(900, window.Bounds.Left);

        AssertEveryPanelAppearsOnce(layout);
    }

    [Fact]
    public void RevealingAClosedPanelOpensItWhereTheCallerSays()
    {
        // The model holds no default placements: where a panel belongs is a fact about the panel, which the
        // registry beside the controls knows. This takes the answer rather than guessing it.
        DockLayout layout = DockLayout.Default.Reveal(PanelId.Summary, DockSide.Bottom);

        Assert.Equal([PanelId.Summary], layout.At(DockSide.Bottom).Panels);
        Assert.Equal(PanelId.Summary, layout.At(DockSide.Bottom).Active);
    }

    [Fact]
    public void RevealingAnOpenPanelOnlyGivesItTheAttention()
    {
        // Never moves it. A reader who has arranged a panel somewhere and then clicks its icon is asking to look
        // at it, not asking for it to be relocated under them.
        DockLayout layout = DockLayout.Default.Reveal(PanelId.Values, DockSide.Bottom);

        Assert.Equal([PanelId.Runs, PanelId.Values], layout.At(DockSide.Left).Panels);
        Assert.Equal(PanelId.Values, layout.At(DockSide.Left).Active);
        Assert.True(layout.At(DockSide.Bottom).IsEmpty);
    }

    [Fact]
    public void TogglingClosesWhatIsOpenAndOpensWhatIsClosed()
    {
        DockLayout closed = DockLayout.Default.Toggle(PanelId.Home, DockSide.Center);

        Assert.False(closed.IsOpen(PanelId.Home));
        Assert.True(closed.At(DockSide.Center).IsEmpty);

        DockLayout reopened = closed.Toggle(PanelId.Home, DockSide.Center);

        Assert.Equal([PanelId.Home], reopened.At(DockSide.Center).Panels);
    }

    [Fact]
    public void ClosingTheLastCentrePanelIsHowTheCanvasComesBack()
    {
        // Which is what closing the runs page does today, and the reason the centre is a well rather than a
        // permanent surface.
        DockLayout layout = DockLayout.Default.Close(PanelId.Home);

        Assert.True(layout.At(DockSide.Center).IsEmpty);
        Assert.Contains(PanelId.Home, layout.Closed);
    }

    [Fact]
    public void AnArrangementThatMentionsAPanelTwiceKeepsItsDockedHome()
    {
        // Repaired rather than refused: throwing away somebody's whole arrangement over one duplicated entry
        // would be the greater harm. The docked position wins because it is the one more likely to be deliberate.
        DockLayout broken = new()
        {
            Sides = ImmutableDictionary<DockSide, DockWell>.Empty
                .Add(DockSide.Left, new DockWell { Panels = [PanelId.Runs], Active = PanelId.Runs }),
            Floats = [new DockFloat { Panels = [PanelId.Runs], Active = PanelId.Runs }]
        };

        DockLayout fixedUp = broken.Normalise();

        Assert.Equal([PanelId.Runs], fixedUp.At(DockSide.Left).Panels);
        Assert.Empty(fixedUp.Floats);

        AssertEveryPanelAppearsOnce(fixedUp);
    }

    [Fact]
    public void APanelTheFileNeverMentionedIsPutAwayRatherThanLost()
    {
        // How a build that adds a seventh panel reads a file written by one that had six.
        DockLayout sparse = new()
        {
            Sides = ImmutableDictionary<DockSide, DockWell>.Empty
                .Add(DockSide.Left, new DockWell { Panels = [PanelId.Runs], Active = PanelId.Runs })
        };

        DockLayout fixedUp = sparse.Normalise();

        AssertEveryPanelAppearsOnce(fixedUp);
        Assert.All(
            Enum.GetValues<PanelId>().Where(panel => panel != PanelId.Runs),
            panel => Assert.Contains(panel, fixedUp.Closed));
    }

    [Fact]
    public void AWellWhoseActivePanelHasGonePicksAnother()
    {
        // A well with panels in it and nothing active draws as a stack nobody is looking at, and in the centre
        // it draws as nothing at all.
        DockLayout broken = new()
        {
            Sides = ImmutableDictionary<DockSide, DockWell>.Empty
                .Add(DockSide.Left, new DockWell { Panels = [PanelId.Runs], Active = PanelId.Inspector })
        };

        Assert.Equal(PanelId.Runs, broken.Normalise().At(DockSide.Left).Active);
    }

    [Fact]
    public void NormalisingIsAlreadyDoneForTheDefault()
    {
        // The factory arrangement must not be something the repair pass changes, or the tool would open in an
        // arrangement nobody wrote down.
        Assert.Equal(DockLayout.Default, DockLayout.Default.Normalise());
    }

    [Fact]
    public void TwoArrangementsBuiltTheSameWayAreEqual()
    {
        // Both collection members compare by reference under the generated equality, so without hand-written
        // comparisons the host would tear down and rebuild every panel on a dispatch that changed nothing.
        DockLayout one = DockLayout.Default.Move(PanelId.Summary, DockSide.Bottom, 0);
        DockLayout two = DockLayout.Default.Move(PanelId.Summary, DockSide.Bottom, 0);

        Assert.Equal(one, two);
        Assert.True(one.SameShapeAs(two));
    }

    [Fact]
    public void ResizingChangesTheArrangementButNotItsShape()
    {
        // The distinction the host is built on: a splitter drag moves a column, and must never rebuild the
        // panels — that would throw away a scroll position and whatever was typed into a filter.
        DockLayout before = DockLayout.Default;
        DockLayout after = before.Resize(DockSide.Left, 0.4);

        Assert.NotEqual(before, after);
        Assert.True(before.SameShapeAs(after));
    }

    [Fact]
    public void MovingAFloatChangesTheArrangementButNotItsShape()
    {
        DockLayout before = DockLayout.Default.Float(PanelId.Step, new DockBounds { Left = 0, Top = 0 });
        DockLayout after = before.MoveFloat(0, new DockBounds { Left = 800, Top = 300 });

        Assert.NotEqual(before, after);
        Assert.True(before.SameShapeAs(after));
    }

    [Fact]
    public void MovingAPanelChangesTheShape()
    {
        DockLayout before = DockLayout.Default;

        Assert.False(before.SameShapeAs(before.Move(PanelId.Runs, DockSide.Right, 0)));
        Assert.False(before.SameShapeAs(before.Close(PanelId.Runs)));

        // Which panel holds the attention is part of the shape, because it decides what a tabbed well draws and
        // which header the activity bar lights.
        Assert.False(before.SameShapeAs(before.Activate(PanelId.Values)));
    }

    [Fact]
    public void AFloatIsNeverSmallerThanSomethingCanBeReadIn()
    {
        // The reason being a stack trace: it is the widest thing any of these panels shows, and the reason
        // somebody pops one out.
        DockLayout layout = DockLayout.Default.Float(PanelId.Step, new DockBounds { Width = 10, Height = 10 });

        Assert.Equal(DockBounds.MinimumWidth, layout.Floats[0].Bounds.Width);
        Assert.Equal(DockBounds.MinimumHeight, layout.Floats[0].Bounds.Height);
    }

    [Fact]
    public void AnOutOfRangeFloatIsIgnoredRatherThanThrowing()
    {
        // Both indices come from a window that may have closed between the pointer going down and coming up.
        DockLayout layout = DockLayout.Default;

        Assert.Equal(layout, layout.MoveIntoFloat(PanelId.Step, floatIndex: 7, index: 0));
        Assert.Equal(layout, layout.MoveFloat(7, new DockBounds()));
    }

    private static void AssertEveryPanelAppearsOnce(DockLayout layout)
    {
        ImmutableList<PanelId> placed =
        [
            .. Enum.GetValues<DockSide>().SelectMany(side => layout.At(side).Panels),
            .. layout.Floats.SelectMany(window => window.Panels),
            .. layout.Closed
        ];

        Assert.Equal(Enum.GetValues<PanelId>().Length, placed.Count);
        Assert.Equal(placed.Count, placed.Distinct().Count());
    }
}
