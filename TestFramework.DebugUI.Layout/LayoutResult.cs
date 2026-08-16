using System;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.Layout;

/// <summary>What kind of thing a laid-out box represents.</summary>
public enum LayoutNodeKind
{
    /// <summary>A stage band, enclosing the rows of steps belonging to it.</summary>
    Stage,

    /// <summary>One step.</summary>
    Step,

    /// <summary>
    /// The verdict: every asserted value flows into it.
    /// </summary>
    /// <remarks>
    /// A run's steps say what happened; this says whether what happened was correct. Drawing it as a
    /// node the asserted values connect into makes the answer to "why is this run valid" a thing you
    /// can follow with your eye, rather than a list somewhere else that has to be reconciled with
    /// the board by hand.
    /// </remarks>
    Verdict
}

/// <summary>What a pipe carries.</summary>
public enum LayoutEdgeKind
{
    /// <summary>Runs from the step that produced a value to a step that declared it as an input.</summary>
    Consumption,

    /// <summary>
    /// Runs from a value into the verdict, because that value was asserted on.
    /// </summary>
    /// <remarks>
    /// Distinguished from a consumption so a view can colour it by whether the assertion held rather
    /// than by what kind of value flowed: on the pipes into the verdict, holding or not holding is
    /// the only thing worth knowing.
    /// </remarks>
    Assertion
}

/// <summary>A point on the board.</summary>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>
/// One laid-out box.
/// </summary>
public sealed record LayoutNode
{
    /// <summary>Gets the identifier a view keys its visual off, unique within a result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets what this box represents.</summary>
    public required LayoutNodeKind Kind { get; init; }

    /// <summary>Gets the stage this box belongs to.</summary>
    public required string StageName { get; init; }

    /// <summary>Gets the step this box is, or belongs to. Null on a stage band.</summary>
    public int? StepId { get; init; }

    /// <summary>Gets the variable or artifact key, on a value box.</summary>
    public string? Key { get; init; }

    /// <summary>Gets whether a value box holds a variable or an artifact.</summary>
    public DebugValueKind ValueKind { get; init; } = DebugValueKind.Variable;

    /// <summary>Gets whether every assertion held, on a verdict box.</summary>
    public bool IsSatisfied { get; init; } = true;

    /// <summary>Gets the left edge.</summary>
    public required double X { get; init; }

    /// <summary>Gets the top edge.</summary>
    public required double Y { get; init; }

    /// <summary>Gets the width.</summary>
    public required double Width { get; init; }

    /// <summary>Gets the height.</summary>
    public required double Height { get; init; }

    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets the horizontal centre.</summary>
    public double CentreX => X + (Width / 2);

    /// <summary>Gets the vertical centre.</summary>
    public double CentreY => Y + (Height / 2);
}

/// <summary>
/// A connector on a box, where a pipe plugs in.
/// </summary>
/// <remarks>
/// Ports are what make the flow legible. A pipe that ran between two box centres says only that two
/// steps are related; a pipe that leaves a named connector on the bottom of one box and enters a
/// named connector on the top of another says which value moved, and where it went. Inputs sit
/// along the top edge and outputs along the bottom, so the direction of travel is the direction of
/// the board.
/// </remarks>
public sealed record LayoutPort
{
    /// <summary>Gets the identifier a view keys its visual off, unique within a result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the box this connector belongs to.</summary>
    public required string NodeId { get; init; }

    /// <summary>Gets the variable or artifact that flows through it.</summary>
    public required string Key { get; init; }

    /// <summary>Gets a value indicating whether this port receives rather than sends.</summary>
    public required bool IsInput { get; init; }

    /// <summary>Gets whether a variable or an artifact flows through it, which decides its colour.</summary>
    public DebugValueKind Kind { get; init; } = DebugValueKind.Variable;

    /// <summary>Gets a value indicating whether anything is actually connected to it.</summary>
    /// <remarks>
    /// An input nothing produces still gets a connector, drawn unconnected. Hiding it would lose the
    /// fact that the step declared it — which is exactly what a reader needs when a step is not
    /// getting the value it expected.
    /// </remarks>
    public bool IsConnected { get; init; }

    /// <summary>Gets the horizontal position of the connector.</summary>
    public required double X { get; init; }

    /// <summary>Gets the vertical position of the connector.</summary>
    public required double Y { get; init; }
}

/// <summary>
/// One laid-out pipe.
/// </summary>
/// <remarks>
/// The points describe an axis-aligned polyline: every segment is strictly horizontal or vertical
/// and every point lands on the grid, so parallel pipes stay parallel instead of drifting. Rounding
/// the corners is the view's job — see <see cref="PipeGeometry"/> — because a corner radius is an
/// appearance, and baking it in would put a visual choice in data that is meant to survive one.
/// </remarks>
public sealed record LayoutEdge
{
    /// <summary>Gets the identifier a view keys its visual off, unique within a result.</summary>
    public required string Id { get; init; }

    /// <summary>Gets what the pipe carries.</summary>
    public required LayoutEdgeKind Kind { get; init; }

    /// <summary>Gets the node the pipe leaves.</summary>
    public required string FromNodeId { get; init; }

    /// <summary>Gets the node the pipe enters.</summary>
    public required string ToNodeId { get; init; }

    /// <summary>Gets the variable or artifact flowing along the pipe.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets whether a variable or an artifact flows along it.
    /// </summary>
    /// <remarks>
    /// Drives the pipe's colour. The two behave differently — a variable is a value, an artifact is
    /// a thing with a lifecycle — and telling them apart at a glance is most of what makes a busy
    /// board readable.
    /// </remarks>
    public DebugValueKind ValueKind { get; init; } = DebugValueKind.Variable;

    /// <summary>Gets the connector the pipe leaves.</summary>
    public string FromPortId { get; init; } = string.Empty;

    /// <summary>Gets the connector the pipe enters.</summary>
    public string ToPortId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the routing lane, or zero when the pipe runs straight down the flow.
    /// </summary>
    /// <remarks>
    /// A pipe whose consumer is not in the next row down bulges into a lane to the right of the
    /// content, so it neither crosses the rows between nor overlaps another routed pipe.
    /// </remarks>
    public int Lane { get; init; }

    /// <summary>Gets the polyline, from source to target.</summary>
    public required ImmutableList<LayoutPoint> Points { get; init; }

    /// <summary>Compares two pipes by their route, not by which list object holds it.</summary>
    /// <remarks>
    /// A record compares a collection member by reference, so two pipes following the same route
    /// would come out unequal — silently, and in the one place a caller would assume otherwise
    /// because the type is a record. That would make "has this board changed?" always answer yes.
    /// </remarks>
    public bool Equals(LayoutEdge? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return string.Equals(Id, other.Id, StringComparison.Ordinal)
               && Kind == other.Kind
               && ValueKind == other.ValueKind
               && string.Equals(FromNodeId, other.FromNodeId, StringComparison.Ordinal)
               && string.Equals(ToNodeId, other.ToNodeId, StringComparison.Ordinal)
               && string.Equals(Key, other.Key, StringComparison.Ordinal)
               && Lane == other.Lane
               && Points.SequenceEqual(other.Points);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built from the identity and the route's length rather than every point: it must agree with
    /// <see cref="Equals(LayoutEdge)"/>, and hashing a long polyline on every lookup would cost more
    /// than the collisions it avoids.
    /// </remarks>
    public override int GetHashCode() => HashCode.Combine(Id, Kind, FromNodeId, ToNodeId, Key, Lane, Points.Count);
}

/// <summary>
/// A laid-out board.
/// </summary>
public sealed record LayoutResult
{
    /// <summary>An empty board.</summary>
    public static LayoutResult Empty { get; } = new()
    {
        Nodes = ImmutableList<LayoutNode>.Empty,
        Edges = ImmutableList<LayoutEdge>.Empty,
        Ports = ImmutableList<LayoutPort>.Empty,
        Width = 0,
        Height = 0
    };

    /// <summary>Gets the boxes, stage bands first so a view can draw them behind the steps.</summary>
    public required ImmutableList<LayoutNode> Nodes { get; init; }

    /// <summary>Gets the pipes.</summary>
    public required ImmutableList<LayoutEdge> Edges { get; init; }

    /// <summary>Gets the connectors on the boxes.</summary>
    public required ImmutableList<LayoutPort> Ports { get; init; }

    /// <summary>Gets the total width, including margins and routing lanes.</summary>
    public required double Width { get; init; }

    /// <summary>Gets the total height, including margins.</summary>
    public required double Height { get; init; }

    /// <summary>Compares two boards by what is on them.</summary>
    /// <remarks>
    /// Same reason as <see cref="LayoutEdge.Equals(LayoutEdge)"/>: without this a board recomputed
    /// from an unchanged run never equals the one it replaced, so nothing downstream can tell that
    /// nothing moved.
    /// </remarks>
    public bool Equals(LayoutResult? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Width.Equals(other.Width)
               && Height.Equals(other.Height)
               && Nodes.SequenceEqual(other.Nodes)
               && Edges.SequenceEqual(other.Edges)
               && Ports.SequenceEqual(other.Ports);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height, Nodes.Count, Edges.Count, Ports.Count);
}
