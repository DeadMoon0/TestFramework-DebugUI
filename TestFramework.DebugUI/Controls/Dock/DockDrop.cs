using System;
using System.Collections.Generic;
using System.Windows;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// One card as the drag layer sees it: which panel, which well, and where on screen.
/// </summary>
/// <remarks>
/// Collected as the host draws, because working it out again from the visual tree during a drag would mean
/// walking it on every mouse move — and the drag needs an answer for every pixel the pointer covers.
/// </remarks>
internal sealed record DockCard
{
    /// <summary>Gets the panel the card holds.</summary>
    public required PanelId Panel { get; init; }

    /// <summary>Gets the well the card belongs to.</summary>
    public required DockSide Side { get; init; }

    /// <summary>Gets its position within that well.</summary>
    public required int Index { get; init; }

    /// <summary>Gets where the card is, in the host's own coordinates.</summary>
    public required Rect Bounds { get; init; }
}

/// <summary>Where a dragged panel would land, and the shape that says so.</summary>
internal sealed record DockDrop
{
    /// <summary>Gets the well it would join.</summary>
    public required DockSide Side { get; init; }

    /// <summary>Gets the position it would take in that well.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the region to draw as the hint.</summary>
    public required Rect Hint { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a gap between cards rather than a region.
    /// </summary>
    /// <remarks>
    /// Drawn as a line rather than a block, because the two mean different things: a block says "the panel goes
    /// here, filling this", and a line says "the panel goes between these two". Showing an insertion as a block
    /// would claim it was about to cover the card it is going above.
    /// </remarks>
    public bool IsInsertion { get; init; }

    /// <summary>
    /// Works out where a pointer is asking to put a panel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A card takes priority over the edge it sits on. Over a card, the half the pointer is in decides whether
    /// the panel lands above or below it — which is how a reader says which of two panels is on top, and the one
    /// thing about a rail that has to be theirs rather than the tool's.
    /// </para>
    /// <para>
    /// Away from every rail card, the edges of the window answer: the outer bands dock to a side, and what is
    /// left is the centre. The order matters. The centre card fills the whole canvas, so if it were consulted
    /// with the rails it would cover every band and an empty side would have nothing to aim at — dropping a panel
    /// along the bottom would quietly add it to the centre's tabs instead.
    /// </para>
    /// </remarks>
    public static DockDrop? Resolve(Point at, Size host, IReadOnlyList<DockCard> cards, PanelId dragged)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (host.Width <= 0 || host.Height <= 0)
            return null;

        // A card in a rail answers for its own space: over one, the half the pointer is in decides above or
        // below. The centre is deliberately not consulted yet — it fills the whole canvas, so consulting it here
        // would swallow every edge band and leave an empty side with no way to reach it.
        foreach (DockCard card in cards)
        {
            if (card.Side == DockSide.Center || !card.Bounds.Contains(at))
                continue;

            bool below = at.Y > card.Bounds.Top + (card.Bounds.Height / 2);
            int index = below ? card.Index + 1 : card.Index;

            // A panel dropped either side of itself has not moved. Reported as no drop at all rather than as a
            // move that changes nothing, so no hint appears where releasing would do nothing.
            if (card.Panel == dragged && (index == card.Index || index == card.Index + 1))
                return null;

            double line = below ? card.Bounds.Bottom : card.Bounds.Top;

            return new DockDrop
            {
                Side = card.Side,
                Index = index,
                IsInsertion = true,
                Hint = new Rect(card.Bounds.Left, line - (InsertionThickness / 2), card.Bounds.Width, InsertionThickness)
            };
        }

        double band = EdgeBand;

        if (at.X < host.Width * band)
            return Edge(DockSide.Left, new Rect(0, 0, host.Width * band, host.Height), cards);

        if (at.X > host.Width * (1 - band))
            return Edge(DockSide.Right, new Rect(host.Width * (1 - band), 0, host.Width * band, host.Height), cards);

        if (at.Y > host.Height * (1 - band))
        {
            return Edge(
                DockSide.Bottom,
                new Rect(host.Width * band, host.Height * (1 - band), host.Width * (1 - (2 * band)), host.Height * band),
                cards);
        }

        foreach (DockCard card in cards)
        {
            // Over the centre and away from every band: join its strip rather than split it, because the centre
            // is tabbed and has no above or below to choose.
            if (card.Side == DockSide.Center && card.Bounds.Contains(at))
                return new DockDrop { Side = DockSide.Center, Index = card.Index + 1, Hint = card.Bounds };
        }

        return Edge(
            DockSide.Center,
            new Rect(host.Width * band, 0, host.Width * (1 - (2 * band)), host.Height * (1 - band)),
            cards);
    }

    /// <summary>How wide the strip along each edge is, as a fraction of the window.</summary>
    /// <remarks>
    /// Generous, because these bands are only reachable where no card is under the pointer — a narrow target for
    /// the one gesture that fills an empty side would be a gesture nobody finds.
    /// </remarks>
    private const double EdgeBand = 0.18;

    /// <summary>How thick the line between two cards is drawn.</summary>
    private const double InsertionThickness = 4;

    /// <summary>A drop at the end of a side's well.</summary>
    private static DockDrop Edge(DockSide side, Rect hint, IReadOnlyList<DockCard> cards)
    {
        int occupants = 0;

        foreach (DockCard card in cards)
        {
            if (card.Side == side)
                occupants++;
        }

        return new DockDrop { Side = side, Index = occupants, Hint = hint };
    }
}
