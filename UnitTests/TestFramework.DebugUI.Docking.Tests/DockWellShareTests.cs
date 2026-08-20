using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Docking.Tests;

/// <summary>
/// Covers how a well divides itself between the panels stacked in it.
/// </summary>
/// <remarks>
/// The arithmetic of a division is the part worth testing: the pointer only ever hands it a delta, and whether
/// that comes out as two panels sharing a rail sensibly — or as one panel four pixels tall — is decided here,
/// where it can be asserted without a window.
/// </remarks>
public class DockWellShareTests
{
    [Fact]
    public void AWellNobodyHasDividedSplitsEvenly()
    {
        DockWell well = Rail();

        Assert.Equal([0.5, 0.5], well.Shares);
    }

    [Fact]
    public void DraggingABoundaryOnlyMovesTheTwoPanelsEitherSideOfIt()
    {
        // What makes dragging one edge feel local: the third panel keeps exactly what it had.
        DockWell well = Rail().Insert(PanelId.Step, 2).Divide(0, 0.1);

        ImmutableList<double> shares = well.Shares;

        Assert.Equal(0.433, shares[0], 3);
        Assert.Equal(0.233, shares[1], 3);
        Assert.Equal(0.333, shares[2], 3);
    }

    [Fact]
    public void ADivisionAlwaysStillAddsUpToOne()
    {
        DockWell well = Rail().Divide(0, 0.2);

        Assert.Equal(1, well.Shares.Sum(), 6);
    }

    [Fact]
    public void NeitherPanelCanBeSqueezedBelowWhatIsWorthShowing()
    {
        // A panel dragged to nothing is a header that still takes clicks and shows none of what it is for.
        // Closing it is what the reader actually means, and that is one click away on the header.
        Assert.Equal(DockWell.MinimumShare, Rail().Divide(0, -5).Shares[0], 6);
        Assert.Equal(DockWell.MinimumShare, Rail().Divide(0, 5).Shares[1], 6);
    }

    [Fact]
    public void APanelArrivingTakesAnAverageShareAndLeavesTheRestAlone()
    {
        // The proportions the reader had already set between the existing panels survive; a fixed slice for the
        // newcomer would quietly resize everything else.
        DockWell well = Rail().Divide(0, 0.15).Insert(PanelId.Step, 2);

        ImmutableList<double> shares = well.Shares;

        Assert.Equal(3, shares.Count);
        Assert.Equal(1, shares.Sum(), 6);
        Assert.True(shares[0] > shares[1], "the division that was set is still there");
    }

    [Fact]
    public void APanelLeavingHandsItsShareBackWithoutResettingTheOthers()
    {
        DockWell well = Rail().Insert(PanelId.Step, 2).Divide(0, 0.2).Remove(PanelId.Step);

        ImmutableList<double> shares = well.Shares;

        Assert.Equal(2, shares.Count);
        Assert.Equal(1, shares.Sum(), 6);
        Assert.True(shares[0] > shares[1], "what was left keeps its proportions");
    }

    [Fact]
    public void WeightsThatNoLongerMatchThePanelsFallBackToEvenShares()
    {
        // How a file written by a build with a different set of panels stays survivable: a rail drawn with one
        // panel at nine hundred pixels and the next at four is worse than one drawn evenly.
        DockWell broken = Rail() with { Weights = [0.9] };

        Assert.Equal([0.5, 0.5], broken.Shares);
    }

    [Fact]
    public void ADivisionIsNotAShapeChange()
    {
        // The host rebuilds on a shape change and only moves rows otherwise. A division that counted as a shape
        // change would tear down and rebuild both panels on every pixel of the drag.
        DockWell before = Rail();
        DockWell after = before.Divide(0, 0.2);

        Assert.NotEqual(before, after);
        Assert.True(before.SameShapeAs(after));
    }

    [Fact]
    public void AnOutOfRangeBoundaryIsIgnoredRatherThanThrowing()
    {
        DockWell well = Rail();

        Assert.Equal(well, well.Divide(-1, 0.1));
        Assert.Equal(well, well.Divide(7, 0.1));
        Assert.Equal(well, well.Divide(1, 0.1));
    }

    private static DockWell Rail() => new()
    {
        Panels = [PanelId.Runs, PanelId.Values],
        Active = PanelId.Runs
    };
}
