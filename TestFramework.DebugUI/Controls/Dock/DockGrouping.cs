using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// Which panels share a card.
/// </summary>
/// <remarks>
/// <para>
/// The one place that answers it, because two things depend on the answer and they must not disagree: the host
/// draws tabbed wells as a single card and stacked ones as a card each, and the title bar's strip groups its
/// icons the same way. If those two ever parted company the strip would offer to put away half of something.
/// </para>
/// <para>
/// A card is the unit a reader can act on. Panels that share one are one thing on screen — closing "the values"
/// while "the runs page" stayed in the same card would be closing half a card, which is not a state the window
/// can be in. Panels in their own cards are their own business.
/// </para>
/// </remarks>
internal static class DockGrouping
{
    /// <summary>
    /// The panels drawn in the same card as this one, itself included.
    /// </summary>
    /// <remarks>
    /// Tabbed containers — the centre well and every floating window — put all their panels in one card. A side
    /// well stacks a card per panel, so each of those answers with only itself.
    /// </remarks>
    public static ImmutableList<PanelId> SharingACard(DockLayout layout, PanelId panel)
    {
        ArgumentNullException.ThrowIfNull(layout);

        DockLocation at = layout.Locate(panel);

        return at.Placement switch
        {
            DockPlacement.Side when at.Side == DockSide.Center => layout.At(DockSide.Center).Panels,
            DockPlacement.Float => layout.Floats[at.FloatIndex].Panels,
            _ => [panel]
        };
    }

    /// <summary>
    /// Every card's worth of panels, in the order the strip should offer them.
    /// </summary>
    /// <remarks>
    /// Registry order, except that panels sharing a card are pulled together so the group is adjacent — the icons
    /// for one card have to sit side by side or there is nothing to say they act as one. A closed panel is a group
    /// of its own, because it has no card to share.
    /// </remarks>
    public static ImmutableList<ImmutableList<PanelId>> Cards(DockLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        ImmutableList<ImmutableList<PanelId>>.Builder groups = ImmutableList.CreateBuilder<ImmutableList<PanelId>>();
        System.Collections.Generic.HashSet<PanelId> taken = [];

        foreach (PanelDescriptor descriptor in PanelRegistry.All)
        {
            if (!taken.Add(descriptor.Id))
                continue;

            ImmutableList<PanelId> together = SharingACard(layout, descriptor.Id);

            // Kept in registry order within the group as well, so an icon never moves relative to its neighbours
            // just because a well happened to be built in a different order.
            ImmutableList<PanelId> ordered =
            [
                .. PanelRegistry.All
                    .Select(entry => entry.Id)
                    .Where(together.Contains)
            ];

            foreach (PanelId member in ordered)
                taken.Add(member);

            groups.Add(ordered);
        }

        return groups.ToImmutable();
    }
}
