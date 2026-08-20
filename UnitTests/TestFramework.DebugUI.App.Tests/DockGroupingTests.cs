using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers which panels count as one thing on screen.
/// </summary>
/// <remarks>
/// The title bar's strip and the host both read this, and they must not disagree: the host draws a tabbed well as
/// one card, so the strip has to offer that card as one thing. When they parted company the strip offered to put
/// away half a card — the values, but not the runs page sharing its tabs — which is not a state the window can be
/// in.
/// </remarks>
public class DockGroupingTests
{
    [Fact]
    public void PanelsInTheirOwnCardsAnswerOnlyForThemselves()
    {
        // A rail stacks a card per panel, so each is separately on screen and separately closable.
        DockLayout layout = DockLayout.Default;

        Assert.Equal([PanelId.Runs], DockGrouping.SharingACard(layout, PanelId.Runs));
        Assert.Equal([PanelId.Values], DockGrouping.SharingACard(layout, PanelId.Values));
    }

    [Fact]
    public void TabsInTheCentreAnswerForEachOther()
    {
        DockLayout layout = DockLayout.Default.Move(PanelId.Values, DockSide.Center, 1);

        Assert.Equal([PanelId.Home, PanelId.Values], DockGrouping.SharingACard(layout, PanelId.Values));
        Assert.Equal([PanelId.Home, PanelId.Values], DockGrouping.SharingACard(layout, PanelId.Home));
    }

    [Fact]
    public void PanelsInOneFloatingWindowAnswerForEachOther()
    {
        // A float is tabbed for the same reason the centre is, so it groups the same way.
        DockLayout layout = DockLayout.Default
            .Float(PanelId.Step, new DockBounds())
            .MoveIntoFloat(PanelId.Summary, floatIndex: 0, index: 1);

        Assert.Equal([PanelId.Step, PanelId.Summary], DockGrouping.SharingACard(layout, PanelId.Step));
    }

    [Fact]
    public void AClosedPanelIsItsOwnGroupBecauseItHasNoCardToShare()
    {
        Assert.Equal([PanelId.Summary], DockGrouping.SharingACard(DockLayout.Default, PanelId.Summary));
    }

    [Fact]
    public void EveryPanelAppearsInExactlyOneGroup()
    {
        // The strip is built from these groups, so a panel in none would have no icon and a panel in two would
        // have an icon that contradicted itself.
        DockLayout layout = DockLayout.Default.Move(PanelId.Values, DockSide.Center, 1);

        ImmutableList<PanelId> listed = [.. DockGrouping.Cards(layout).SelectMany(group => group)];

        Assert.Equal(PanelRegistry.All.Count, listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    [Fact]
    public void PanelsSharingACardAreOfferedNextToEachOther()
    {
        // Adjacency is the whole of what says the icons act together, so it is worth pinning rather than assuming.
        DockLayout layout = DockLayout.Default.Move(PanelId.Values, DockSide.Center, 1);

        ImmutableList<PanelId> together = DockGrouping.Cards(layout)
            .Single(group => group.Count > 1);

        // Registry order inside the group, not tab order. The strip is read by position — "where is the values
        // icon" has to have one answer — so reordering the card's tabs must not shuffle the row.
        Assert.Equal([PanelId.Values, PanelId.Home], together);
    }

    [Fact]
    public void GroupsFollowRegistryOrderSoAnIconNeverMovesWithoutReason()
    {
        // The strip is read by position. A panel whose icon jumped along the row because a well happened to be
        // built in another order would be a panel nobody could find twice.
        ImmutableList<ImmutableList<PanelId>> groups = DockGrouping.Cards(DockLayout.Default);

        Assert.Equal(
            [.. PanelRegistry.All.Select(descriptor => descriptor.Id)],
            groups.SelectMany(group => group));
    }
}
