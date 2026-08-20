using System;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.Docking;

/// <summary>
/// Where a floating window is, in screen coordinates.
/// </summary>
/// <remarks>
/// Screen pixels rather than a fraction, unlike a well's extent: a float is placed against a monitor the
/// reader chose, and the whole point of putting one on the second screen is that it stays there. Whether the
/// monitor still exists is checked when the arrangement is applied, the way the main window's own placement
/// already is.
/// </remarks>
public sealed record DockBounds
{
    /// <summary>The smallest a float may be, in pixels.</summary>
    /// <remarks>
    /// Enough to read a stack trace in, which is the widest thing any of these panels shows and the reason
    /// somebody would pop one out in the first place.
    /// </remarks>
    public const double MinimumWidth = 320;

    /// <summary>The shortest a float may be, in pixels.</summary>
    public const double MinimumHeight = 240;

    /// <summary>Gets the left edge.</summary>
    public double Left { get; init; }

    /// <summary>Gets the top edge.</summary>
    public double Top { get; init; }

    /// <summary>Gets the width, never below the minimum.</summary>
    public double Width { get; init; } = 480;

    /// <summary>Gets the height, never below the minimum.</summary>
    public double Height { get; init; } = 560;

    /// <summary>The same bounds with the size held to what can be read.</summary>
    public DockBounds Sane() => this with
    {
        Width = Math.Max(MinimumWidth, Width),
        Height = Math.Max(MinimumHeight, Height)
    };
}

/// <summary>
/// A well of panels in a window of its own.
/// </summary>
/// <remarks>
/// <para>
/// One process, one store. A floated panel binds the same <c>StateStore</c> the docked ones do, so it follows
/// the same run with nothing threaded through to it — which is the reason popping out is cheap here and would
/// not be in an application that passed its data down a visual tree.
/// </para>
/// <para>
/// Tabbed rather than stacked, whatever side the panels came from: a float is a window the reader sized for
/// one thing, and stacking three panels inside it would give each a third of what they asked for.
/// </para>
/// </remarks>
public sealed record DockFloat
{
    /// <summary>Gets the panels in the window, in tab order.</summary>
    public ImmutableList<PanelId> Panels { get; init; } = [];

    /// <summary>Gets the visible one.</summary>
    public PanelId? Active { get; init; }

    /// <summary>Gets where the window is and how big.</summary>
    public DockBounds Bounds { get; init; } = new();

    /// <summary>Gets whether there is anything left in it.</summary>
    /// <remarks>
    /// A float that empties is closed rather than kept: an empty window is furniture the reader has to dismiss
    /// to be rid of something they already dismissed.
    /// </remarks>
    public bool IsEmpty => Panels.Count == 0;

    /// <summary>Whether a panel is in this window.</summary>
    public bool Holds(PanelId panel) => Panels.Contains(panel);

    /// <summary>Puts a panel in at a position, and shows it.</summary>
    public DockFloat Insert(PanelId panel, int index)
    {
        DockFloat without = Remove(panel);
        int at = Math.Clamp(index, 0, without.Panels.Count);

        return without with
        {
            Panels = without.Panels.Insert(at, panel),
            Active = panel
        };
    }

    /// <summary>Takes a panel out, showing a neighbour instead if it was the visible one.</summary>
    public DockFloat Remove(PanelId panel)
    {
        int index = Panels.IndexOf(panel);

        if (index < 0)
            return this;

        ImmutableList<PanelId> remaining = Panels.RemoveAt(index);

        if (Active != panel)
            return this with { Panels = remaining };

        return this with
        {
            Panels = remaining,
            Active = remaining.IsEmpty ? null : remaining[Math.Min(index, remaining.Count - 1)]
        };
    }

    /// <summary>Shows a panel already in this window.</summary>
    public DockFloat Activate(PanelId panel) => Holds(panel) ? this with { Active = panel } : this;

    /// <summary>Compares the panels by content, for the reason <see cref="DockWell.Equals(DockWell)"/> gives.</summary>
    public bool Equals(DockFloat? other)
        => other is not null
           && Active == other.Active
           && Equals(Bounds, other.Bounds)
           && Panels.SequenceEqual(other.Panels);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Panels.Count, Active, Bounds);

    /// <summary>Whether the window would hold the same panels, wherever it has been dragged to.</summary>
    public bool SameShapeAs(DockFloat? other)
        => other is not null
           && Active == other.Active
           && Panels.SequenceEqual(other.Panels);
}
