using System;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.Docking;

/// <summary>
/// The panels at one side, and how much room they have been given.
/// </summary>
/// <remarks>
/// A well survives its last panel leaving. Its extent and whether it was pinned are the reader's preferences
/// about that edge, not about whichever panel happened to be there — so dragging the last one out and
/// bringing it back must not reset the edge to a default width.
/// </remarks>
public sealed record DockWell
{
    /// <summary>The narrowest a side well may be squeezed, as a fraction of the window.</summary>
    /// <remarks>
    /// Below this a panel is a sliver too narrow to read but still wide enough to take clicks, which is worse
    /// than being closed. Closing is what a reader who wants the room should do, and it is one click away.
    /// </remarks>
    public const double MinimumExtent = 0.12;

    /// <summary>The most of the window a side well may take.</summary>
    /// <remarks>
    /// Half. Past that the layer stops being a panel over the canvas and starts being the canvas's replacement,
    /// and a reader who wants that has the centre well for it.
    /// </remarks>
    public const double MaximumExtent = 0.5;

    /// <summary>The least of a well any one panel stacked in it keeps.</summary>
    /// <remarks>
    /// A panel squeezed below this is a header and nothing else — still there, still taking clicks, showing none
    /// of what it is for. Closing it is what a reader who wants the room actually means, and that is one click
    /// away on the header itself.
    /// </remarks>
    public const double MinimumShare = 0.08;

    /// <summary>An empty well at a middling size.</summary>
    public static DockWell Empty { get; } = new();

    /// <summary>Gets the panels, in the order they are stacked or tabbed.</summary>
    public ImmutableList<PanelId> Panels { get; init; } = [];

    /// <summary>
    /// Gets the panel with the reader's attention, or null when the well is empty.
    /// </summary>
    /// <remarks>
    /// In a tabbed well this is the visible one. In a stacked well every panel is visible and this is the one
    /// that was last revealed, which is what the activity bar scrolls to.
    /// </remarks>
    public PanelId? Active { get; init; }

    /// <summary>Gets how much of the window the well takes, as a fraction.</summary>
    /// <remarks>
    /// A fraction rather than pixels: the same arrangement has to reopen on a laptop after being saved on a
    /// desktop, and a width of six hundred means something different on each.
    /// </remarks>
    public double Extent { get; init; } = 0.22;

    /// <summary>
    /// Gets whether the well reserves its room, insetting the canvas, rather than covering it.
    /// </summary>
    /// <remarks>
    /// The setting that decides whether this is a docking system or a set of floating overlays, and the answer
    /// differs per person and per screen: on a wide monitor there is room to reserve and nothing should ever be
    /// covered, on a laptop the canvas needs every pixel and a panel is something you glance at. Both are
    /// right, so it is remembered rather than decided here.
    /// </remarks>
    public bool Pinned { get; init; }

    /// <summary>
    /// Gets how the well is divided between its panels, unnormalised.
    /// </summary>
    /// <remarks>
    /// Stored raw and normalised on the way out, so nothing has to keep the numbers adding up to one. Empty means
    /// nobody has dragged a boundary, which reads as equal shares — and so does a list that has gone out of step
    /// with the panels, which is how a file written by a build with a different set of them stays survivable.
    /// </remarks>
    public ImmutableList<double> Weights { get; init; } = [];

    /// <summary>Gets whether there is anything to draw.</summary>
    public bool IsEmpty => Panels.Count == 0;

    /// <summary>
    /// Gets what fraction of the well each panel takes, one per panel, adding up to one.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, so there is no invariant about the weights left for anything to break. A well
    /// nobody has divided splits evenly; so does one whose weights no longer match its panels, which is a better
    /// answer than a rail drawn with one panel at nine hundred pixels and the next at four.
    /// </remarks>
    public ImmutableList<double> Shares
    {
        get
        {
            if (Panels.Count == 0)
                return [];

            double total = Weights.Count == Panels.Count ? Weights.Sum() : 0;

            return total <= 0
                ? [.. Panels.Select(_ => 1d / Panels.Count)]
                : [.. Weights.Select(weight => weight / total)];
        }
    }

    /// <summary>Whether a panel is in this well.</summary>
    public bool Holds(PanelId panel) => Panels.Contains(panel);

    /// <summary>
    /// Puts a panel in at a position, and gives it the attention.
    /// </summary>
    /// <remarks>
    /// The index is clamped rather than validated: it comes from where a pointer was released, and a drop a
    /// pixel past the last header means the end of the list rather than an error.
    /// </remarks>
    public DockWell Insert(PanelId panel, int index)
    {
        DockWell without = Remove(panel);
        int at = Math.Clamp(index, 0, without.Panels.Count);

        ImmutableList<double> shares = without.Shares;

        // An average share, so the newcomer arrives about the size of its neighbours and whatever proportions the
        // reader had already set between them survive. A fixed slice would quietly resize everything else.
        double arriving = shares.Count == 0 ? 1 : shares.Sum() / shares.Count;

        return without with
        {
            Panels = without.Panels.Insert(at, panel),
            Weights = shares.Insert(at, arriving),
            Active = panel
        };
    }

    /// <summary>
    /// Takes a panel out, choosing something else to hold the attention if it had it.
    /// </summary>
    /// <remarks>
    /// The neighbour that took its place, falling back to the one before it — which is what a reader expects
    /// after closing a tab, and never an empty well with panels still in it.
    /// </remarks>
    public DockWell Remove(PanelId panel)
    {
        int index = Panels.IndexOf(panel);

        if (index < 0)
            return this;

        ImmutableList<PanelId> remaining = Panels.RemoveAt(index);

        // The share leaves with the panel, and what is left renormalises on the way out — so the survivors keep
        // their proportions relative to each other rather than being reset to even.
        ImmutableList<double> shares = Shares;
        ImmutableList<double> left = index < shares.Count ? shares.RemoveAt(index) : [];

        if (Active != panel)
            return this with { Panels = remaining, Weights = left };

        return this with
        {
            Panels = remaining,
            Weights = left,
            Active = remaining.IsEmpty ? null : remaining[Math.Min(index, remaining.Count - 1)]
        };
    }

    /// <summary>Gives a panel already here the attention, or leaves the well alone.</summary>
    public DockWell Activate(PanelId panel) => Holds(panel) ? this with { Active = panel } : this;

    /// <summary>Sets the extent, held inside the bounds a well is allowed.</summary>
    public DockWell Resize(double extent) => this with { Extent = Math.Clamp(extent, MinimumExtent, MaximumExtent) };

    /// <summary>
    /// Moves the boundary between one panel in the well and the next.
    /// </summary>
    /// <param name="boundary">The index of the panel above the boundary being dragged.</param>
    /// <param name="delta">How much of the whole well to hand the panel above, taken from the one below.</param>
    /// <remarks>
    /// Only the two panels either side of the boundary change. That is what makes dragging one edge feel local:
    /// everything else in the rail stays exactly where it was, rather than every panel shuffling because one
    /// boundary moved. Each of the two keeps at least <see cref="MinimumShare"/>, and a pair already too small to
    /// divide is left alone rather than collapsed.
    /// </remarks>
    public DockWell Divide(int boundary, double delta)
    {
        ImmutableList<double> shares = Shares;

        if (boundary < 0 || boundary + 1 >= shares.Count)
            return this;

        double pair = shares[boundary] + shares[boundary + 1];

        if (pair < 2 * MinimumShare)
            return this;

        double above = Math.Clamp(shares[boundary] + delta, MinimumShare, pair - MinimumShare);

        return this with { Weights = shares.SetItem(boundary, above).SetItem(boundary + 1, pair - above) };
    }

    /// <summary>Compares the panels by content, which the generated equality would not.</summary>
    /// <remarks>
    /// An <see cref="ImmutableList{T}"/> compares by reference, so two identical wells would come out unequal
    /// and the host would tear down and rebuild every panel on any dispatch that touched the layout.
    /// </remarks>
    public bool Equals(DockWell? other)
        => other is not null
           && Active == other.Active
           && Pinned == other.Pinned
           && Extent.Equals(other.Extent)
           && Panels.SequenceEqual(other.Panels)
           && Shares.SequenceEqual(other.Shares);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Panels.Count, Active, Pinned, Extent);

    /// <summary>
    /// Whether this well would be drawn the same way, ignoring how big it is.
    /// </summary>
    /// <remarks>
    /// The distinction the host is built on. Dragging a splitter changes the extent forty times a second, and
    /// rebuilding the panels on each would throw away a scroll position, an expanded stack trace and whatever
    /// the reader had typed into a filter. Extent changes move a column; anything else rebuilds.
    /// </remarks>
    public bool SameShapeAs(DockWell? other)
        => other is not null
           && Active == other.Active
           && Pinned == other.Pinned
           && Panels.SequenceEqual(other.Panels);
}
