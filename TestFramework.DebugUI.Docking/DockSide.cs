namespace TestFramework.DebugUI.Docking;

/// <summary>
/// Where a well of panels sits over the canvas.
/// </summary>
/// <remarks>
/// <para>
/// A fixed set of sides rather than a tree of arbitrary splits. The window has one dominant surface — the
/// board — and everything here is a layer over it, so there is no arrangement in which the canvas becomes one
/// cell among many. A split tree would have allowed exactly that.
/// </para>
/// <para>
/// Panels in a side well are <em>stacked</em>, one above the next with its own header, which is what keeps the
/// tree and the values both visible down the left edge as they are today. Panels in
/// <see cref="Center"/> are <em>tabbed</em>, because what goes there wants the whole area and two of them
/// sharing it would leave neither enough. That rule lives in the host that draws a well, not here.
/// </para>
/// </remarks>
public enum DockSide
{
    /// <summary>Down the left edge.</summary>
    Left,

    /// <summary>Down the right edge.</summary>
    Right,

    /// <summary>Across the bottom.</summary>
    Bottom,

    /// <summary>
    /// Over the canvas entirely, the way a page covers it today.
    /// </summary>
    /// <remarks>
    /// For a panel that is not a rail: the runs page is a table six columns wide and would be unreadable in
    /// three hundred pixels. An empty centre well is what makes the board visible, so closing the last panel
    /// here is how a reader gets back to the run — which is what closing the runs page does today.
    /// </remarks>
    Center
}
