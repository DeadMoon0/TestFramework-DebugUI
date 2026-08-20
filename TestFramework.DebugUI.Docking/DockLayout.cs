using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.Docking;

/// <summary>Whether a panel is at a side, in a window of its own, or put away.</summary>
public enum DockPlacement
{
    /// <summary>In one of the wells over the canvas.</summary>
    Side,

    /// <summary>In a floating window.</summary>
    Float,

    /// <summary>Not on screen, but still known about.</summary>
    Closed
}

/// <summary>Where one panel currently is.</summary>
/// <remarks>
/// A single answer rather than three separate questions, because every caller that asks needs all of it: the
/// host draws it, the activity bar lights its icon, and a drag needs to know where the panel is leaving.
/// </remarks>
public readonly record struct DockLocation(DockPlacement Placement, DockSide Side, int FloatIndex, int Index)
{
    /// <summary>A panel that is put away.</summary>
    public static DockLocation Away { get; } = new(DockPlacement.Closed, DockSide.Center, -1, -1);

    /// <summary>Whether the panel is on screen somewhere.</summary>
    public bool IsOpen => Placement != DockPlacement.Closed;
}

/// <summary>
/// Where every panel is, as one value.
/// </summary>
/// <remarks>
/// <para>
/// The arrangement is data, and the visual tree is drawn from it — never the other way round. This is the same
/// call the board makes: geometry comes from a computed layout and nothing positional is kept in the controls.
/// Hand-rolled docking usually lets the visual tree be the truth, which is why it usually cannot be tested,
/// cannot be persisted without drifting, and grows disagreements between the file and the screen.
/// </para>
/// <para>
/// Every panel appears exactly once, across the sides, the floats and what is put away. That is the invariant
/// the whole thing rests on: a panel cannot be in two places, and cannot go missing by being in none.
/// <see cref="Normalise"/> is what restores it after reading a file, and the tests pin it.
/// </para>
/// </remarks>
public sealed record DockLayout
{
    /// <summary>
    /// The arrangement a reader who has never moved anything gets.
    /// </summary>
    /// <remarks>
    /// Deliberately the arrangement the tool already had before any of this existed: the tree and the values
    /// stacked down the left, the step panel on the right, the runs page over the canvas, and the summary and the
    /// inspector opened on demand. Somebody upgrading should not have to rebuild their window to get back what
    /// they had.
    /// </remarks>
    public static DockLayout Default { get; } = new()
    {
        Sides = ImmutableDictionary<DockSide, DockWell>.Empty
            .Add(DockSide.Left, new DockWell { Panels = [PanelId.Runs, PanelId.Values], Active = PanelId.Runs, Extent = 0.22 })
            .Add(DockSide.Right, new DockWell { Panels = [PanelId.Step], Active = PanelId.Step, Extent = 0.28 })
            .Add(DockSide.Bottom, DockWell.Empty with { Extent = 0.3 })
            .Add(DockSide.Center, new DockWell { Panels = [PanelId.Home], Active = PanelId.Home }),
        Closed = [PanelId.Summary, PanelId.Inspector]
    };

    /// <summary>Gets the wells, keyed by side.</summary>
    public ImmutableDictionary<DockSide, DockWell> Sides { get; init; } = ImmutableDictionary<DockSide, DockWell>.Empty;

    /// <summary>Gets the floating windows, in no particular order.</summary>
    public ImmutableList<DockFloat> Floats { get; init; } = [];

    /// <summary>
    /// Gets the panels that are not on screen.
    /// </summary>
    /// <remarks>
    /// Kept rather than simply absent, so the set of panels stays closed and "exactly once" is something a test
    /// can check. It is also what lets a panel go back where it came from: a closed panel remembers nothing,
    /// but the layout remembers that it exists and is not lost.
    /// </remarks>
    public ImmutableList<PanelId> Closed { get; init; } = [];

    /// <summary>The well at a side, empty when there has never been one.</summary>
    public DockWell At(DockSide side) => Sides.TryGetValue(side, out DockWell? well) ? well : DockWell.Empty;

    /// <summary>Where a panel is.</summary>
    public DockLocation Locate(PanelId panel)
    {
        foreach (KeyValuePair<DockSide, DockWell> side in Sides)
        {
            int index = side.Value.Panels.IndexOf(panel);

            if (index >= 0)
                return new DockLocation(DockPlacement.Side, side.Key, -1, index);
        }

        for (int window = 0; window < Floats.Count; window++)
        {
            int index = Floats[window].Panels.IndexOf(panel);

            if (index >= 0)
                return new DockLocation(DockPlacement.Float, DockSide.Center, window, index);
        }

        return DockLocation.Away;
    }

    /// <summary>Whether a panel is on screen anywhere.</summary>
    public bool IsOpen(PanelId panel) => Locate(panel).IsOpen;

    /// <summary>
    /// Puts a panel at a side, taking it out of wherever it was.
    /// </summary>
    /// <remarks>
    /// One operation rather than a remove and an add, because the two halves must not be separable: a caller
    /// that did the remove and then failed the add would leave the panel nowhere, breaking the one invariant
    /// everything else assumes.
    /// </remarks>
    public DockLayout Move(PanelId panel, DockSide side, int index)
        => Detach(panel) is { } cleared
            ? cleared with { Sides = cleared.Sides.SetItem(side, cleared.At(side).Insert(panel, index)) }
            : this;

    /// <summary>Puts a panel in a window of its own at the given bounds.</summary>
    public DockLayout Float(PanelId panel, DockBounds bounds)
    {
        DockLayout cleared = Detach(panel);

        return cleared with
        {
            Floats = cleared.Floats.Add(new DockFloat
            {
                Panels = [panel],
                Active = panel,
                Bounds = (bounds ?? new DockBounds()).Sane()
            })
        };
    }

    /// <summary>
    /// Puts a panel into an existing floating window.
    /// </summary>
    /// <remarks>
    /// What dropping onto a float's tab strip does. Out of range is ignored rather than throwing: the index
    /// comes from a window that may have closed between the pointer going down and coming up.
    /// </remarks>
    public DockLayout MoveIntoFloat(PanelId panel, int floatIndex, int index)
    {
        if (floatIndex < 0 || floatIndex >= Floats.Count)
            return this;

        // Read before detaching: taking the panel out can empty its old window and renumber the list.
        DockBounds bounds = Floats[floatIndex].Bounds;
        DockLayout cleared = Detach(panel);

        int moved = cleared.Floats.FindIndex(window => Equals(window.Bounds, bounds));

        if (moved < 0)
            return cleared.Float(panel, bounds);

        return cleared with { Floats = cleared.Floats.SetItem(moved, cleared.Floats[moved].Insert(panel, index)) };
    }

    /// <summary>Takes a panel off the screen, remembering that it exists.</summary>
    public DockLayout Close(PanelId panel)
    {
        DockLayout cleared = Detach(panel);

        return cleared.Closed.Contains(panel) ? cleared : cleared with { Closed = cleared.Closed.Add(panel) };
    }

    /// <summary>Gives a panel the attention where it already is.</summary>
    public DockLayout Activate(PanelId panel)
    {
        DockLocation at = Locate(panel);

        return at.Placement switch
        {
            DockPlacement.Side => this with { Sides = Sides.SetItem(at.Side, At(at.Side).Activate(panel)) },
            DockPlacement.Float => this with { Floats = Floats.SetItem(at.FloatIndex, Floats[at.FloatIndex].Activate(panel)) },
            _ => this
        };
    }

    /// <summary>
    /// Brings a panel to the reader's attention, opening it at a given side if it was put away.
    /// </summary>
    /// <remarks>
    /// What an activity-bar icon does, and the reason the model does not hold default placements: where a panel
    /// belongs when nobody has said otherwise is a fact about the panel, which the registry beside the controls
    /// knows. This takes the answer as an argument instead of guessing one.
    /// </remarks>
    public DockLayout Reveal(PanelId panel, DockSide fallback)
        => IsOpen(panel) ? Activate(panel) : Move(panel, fallback, int.MaxValue);

    /// <summary>Opens a panel if it is away, or puts it away if it is on screen.</summary>
    public DockLayout Toggle(PanelId panel, DockSide fallback)
        => IsOpen(panel) ? Close(panel) : Move(panel, fallback, int.MaxValue);

    /// <summary>Sets how much room a side takes.</summary>
    public DockLayout Resize(DockSide side, double extent)
        => this with { Sides = Sides.SetItem(side, At(side).Resize(extent)) };

    /// <summary>Moves the boundary between two panels stacked in the same well.</summary>
    public DockLayout Divide(DockSide side, int boundary, double delta)
        => this with { Sides = Sides.SetItem(side, At(side).Divide(boundary, delta)) };

    /// <summary>Sets whether a side reserves its room or covers the canvas.</summary>
    public DockLayout Pin(DockSide side, bool pinned)
        => this with { Sides = Sides.SetItem(side, At(side) with { Pinned = pinned }) };

    /// <summary>Records where a floating window has been dragged or resized to.</summary>
    public DockLayout MoveFloat(int floatIndex, DockBounds bounds)
        => floatIndex < 0 || floatIndex >= Floats.Count || bounds is null
            ? this
            : this with { Floats = Floats.SetItem(floatIndex, Floats[floatIndex] with { Bounds = bounds.Sane() }) };

    /// <summary>
    /// Restores the invariant after reading an arrangement from elsewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repaired rather than refused, which is the opposite of what a recorded annotation or an exported bundle
    /// does with a file it does not understand — and deliberately so. A drawing placed against a layout this
    /// build lays out differently would make a false claim about a run, so it is turned away. A panel in the
    /// wrong well is a mild inconvenience, and throwing away someone's whole arrangement because a later build
    /// added a seventh panel would be the greater harm.
    /// </para>
    /// <para>
    /// So: a panel this build does not know is dropped, a panel the file never mentioned is put away, a panel
    /// listed twice keeps its first home, an emptied float is closed, and a well whose active panel has gone
    /// picks another.
    /// </para>
    /// </remarks>
    public DockLayout Normalise()
    {
        HashSet<PanelId> seen = [];
        DockLayout layout = this;

        // Sides first, then floats, so a panel listed in both keeps the docked position — the one a reader is
        // more likely to have arranged on purpose.
        foreach (DockSide side in Enum.GetValues<DockSide>())
        {
            DockWell well = layout.At(side);
            ImmutableList<PanelId> kept = [.. well.Panels.Where(panel => Enum.IsDefined(panel) && seen.Add(panel))];

            layout = layout with
            {
                Sides = layout.Sides.SetItem(side, Settled(well with { Panels = kept }))
            };
        }

        ImmutableList<DockFloat>.Builder floats = ImmutableList.CreateBuilder<DockFloat>();

        foreach (DockFloat window in layout.Floats)
        {
            ImmutableList<PanelId> kept = [.. window.Panels.Where(panel => Enum.IsDefined(panel) && seen.Add(panel))];

            if (kept.Count == 0)
                continue;

            DockFloat settled = window with { Panels = kept, Bounds = (window.Bounds ?? new DockBounds()).Sane() };

            floats.Add(settled.Active is { } active && kept.Contains(active) ? settled : settled with { Active = kept[0] });
        }

        return layout with
        {
            Floats = floats.ToImmutable(),

            // Anything this build knows about and the file did not place. A panel added by a later build lands
            // here rather than nowhere, and the activity bar can then open it.
            Closed = [.. Enum.GetValues<PanelId>().Where(panel => !seen.Contains(panel))]
        };
    }

    /// <summary>A well whose attention is on a panel it actually holds, and whose extent is in bounds.</summary>
    private static DockWell Settled(DockWell well)
    {
        DockWell sized = well.Resize(well.Extent);

        if (sized.Panels.IsEmpty)
            return sized with { Active = null };

        return sized.Active is { } active && sized.Panels.Contains(active) ? sized : sized with { Active = sized.Panels[0] };
    }

    /// <summary>
    /// The same arrangement with a panel taken out of wherever it was, emptied floats removed.
    /// </summary>
    private DockLayout Detach(PanelId panel)
    {
        ImmutableDictionary<DockSide, DockWell> sides = Sides;

        foreach (KeyValuePair<DockSide, DockWell> side in Sides)
        {
            if (side.Value.Holds(panel))
                sides = sides.SetItem(side.Key, side.Value.Remove(panel));
        }

        ImmutableList<DockFloat> floats =
        [
            .. Floats
                .Select(window => window.Holds(panel) ? window.Remove(panel) : window)
                .Where(window => !window.IsEmpty)
        ];

        return this with
        {
            Sides = sides,
            Floats = floats,
            Closed = Closed.Remove(panel)
        };
    }

    /// <summary>Compares the whole arrangement by content.</summary>
    /// <remarks>
    /// Both collection members compare by reference under the generated equality, so without this the host
    /// would rebuild every panel on any dispatch that touched the layout — including one that changed nothing.
    /// </remarks>
    public bool Equals(DockLayout? other)
        => other is not null
           && Closed.SequenceEqual(other.Closed)
           && Floats.SequenceEqual(other.Floats)
           && Enum.GetValues<DockSide>().All(side => Equals(At(side), other.At(side)));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Closed.Count, Floats.Count, At(DockSide.Left), At(DockSide.Right));

    /// <summary>
    /// Whether this arrangement would be drawn with the same controls in the same places, ignoring sizes.
    /// </summary>
    /// <remarks>
    /// What the host asks before deciding whether to rebuild. A splitter drag and a float being moved both
    /// leave the shape alone; anything that changes which panel is where does not.
    /// </remarks>
    public bool SameShapeAs(DockLayout? other)
        => other is not null
           && Closed.SequenceEqual(other.Closed)
           && Floats.Count == other.Floats.Count
           && Floats.Zip(other.Floats).All(pair => pair.First.SameShapeAs(pair.Second))
           && Enum.GetValues<DockSide>().All(side => At(side).SameShapeAs(other.At(side)));
}
