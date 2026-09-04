using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// The panels that have been dragged out into windows of their own.
/// </summary>
/// <remarks>
/// <para>
/// The windows are built from the arrangement like everything else, which is what keeps a float from being a
/// second source of truth about where a panel is. A float that has emptied has already been removed from the
/// arrangement by the model, so there is no case here for "a window with nothing in it" — it simply has no
/// float to match and is closed.
/// </para>
/// <para>
/// Matched to the arrangement by position rather than kept in a dictionary keyed on the float: a float is a
/// record whose value changes every time the window is nudged, so it cannot be its own key. The arrangement
/// keeps its floats in a stable order and this list follows it.
/// </para>
/// </remarks>
internal sealed class DockFloats
{
    private readonly FrameworkElement surface;
    private readonly DockDragController drag;
    private readonly Func<PanelId, UserControl> panelFor;
    private readonly Func<PanelId, UIElement> header;
    private readonly Func<DockFloat, UIElement> strip;

    private readonly List<DockFloatWindow> windows = [];

    /// <summary>Keeps one host's floating windows in step with the arrangement.</summary>
    /// <param name="surface">The host, whose window owns every float.</param>
    /// <param name="drag">What answers where a float dragged over the host would dock.</param>
    /// <param name="panelFor">The panel to put in a window, which the host built and keeps.</param>
    /// <param name="header">A single panel's own header, drawn as a docked one would be.</param>
    /// <param name="strip">The tab strip for a float holding more than one.</param>
    public DockFloats(
        FrameworkElement surface,
        DockDragController drag,
        Func<PanelId, UserControl> panelFor,
        Func<PanelId, UIElement> header,
        Func<DockFloat, UIElement> strip)
    {
        this.surface = surface;
        this.drag = drag;
        this.panelFor = panelFor;
        this.header = header;
        this.strip = strip;
    }

    /// <summary>Brings the floating windows into line with the arrangement.</summary>
    public void Show(DockLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Window? owner = Window.GetWindow(surface);

        // Not until the main window has been shown. WPF refuses an Owner that has never been shown, and a saved
        // arrangement is restored before the window opens — so creating a float there threw, and the exception
        // left the application half-built with no window at all. The host's Loaded pass comes back for them.
        if (owner is null || !owner.IsLoaded)
            return;

        while (windows.Count > layout.Floats.Count)
        {
            DockFloatWindow extra = windows[^1];
            windows.RemoveAt(windows.Count - 1);

            // Emptied before closing: a panel still parented into a window that is closing would go down with it.
            extra.ShowContent(new Grid(), new Grid());
            extra.Close();
        }

        while (windows.Count < layout.Floats.Count)
            windows.Add(Adopt(owner));

        for (int index = 0; index < layout.Floats.Count; index++)
        {
            DockFloat afloat = layout.Floats[index];
            DockFloatWindow window = windows[index];

            // One panel is not a set of tabs. A window holding a single panel gets the same header a docked one
            // has — its name, its buttons, its close — because it is the same thing, just in a window of its own.
            // Tabs appear only once a second panel has been dragged in, which is the point at which they mean
            // something.
            UIElement caption = afloat.Panels.Count == 1
                ? header(afloat.Panels[0])
                : strip(afloat);

            window.ShowContent(caption, afloat.Active is { } shown ? panelFor(shown) : new Grid());

            // Shown before it is placed. A window that has never been shown has no handle, and the placement goes
            // through Win32 — so doing it the other way round put every float at whatever position WPF cascades
            // to and quietly dropped the one the panel was released at.
            if (!window.IsVisible)
                window.Show();

            window.PlaceAt(afloat.Bounds);
        }
    }

    /// <summary>Creates a floating window and wires what it reports back to the arrangement.</summary>
    private DockFloatWindow Adopt(Window owner)
    {
        DockFloatWindow window = new(owner);

        // A float takes drops like a well does, which is what lets a panel be dragged from the main window into a
        // window that is already floating. There are no zones inside one: a float is a single tabbed card, so a
        // drop anywhere in it joins the strip.
        window.AllowDrop = true;
        window.DragOver += (_, e) =>
        {
            e.Effects = DockDragController.Carried(e) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        };

        window.Drop += (_, e) =>
        {
            if (DockDragController.Carried(e) is not { } dragged)
                return;

            e.Handled = true;

            int at = windows.IndexOf(window);

            if (at >= 0)
                Arrangement.Apply(layout => layout.MoveIntoFloat(dragged, at, int.MaxValue));
        };

        window.Moved += bounds =>
        {
            int at = windows.IndexOf(window);

            if (at >= 0)
                Arrangement.Apply(layout => layout.MoveFloat(at, bounds));
        };

        // Moving a float over the main window is the only way a single panel gets docked again: it has no tab to
        // drag, and its caption belongs to the window. So the system's own move doubles as the drag, and the hint
        // appears under it exactly as it would for one.
        window.Dragging += where =>
        {
            if (Carrying(window) is not { } afloat)
                return;

            drag.ShowHint(afloat.Active is { } carried ? drag.Resolve(where, carried) : null);
        };

        window.Dropped += where =>
        {
            drag.ClearHint();

            if (Carrying(window) is not { } afloat)
                return;

            if (afloat.Active is not { } carried || drag.Resolve(where, carried) is not { } drop)
                return;

            // Only the panel being shown moves. A float carrying tabs is a set of panels the reader grouped on
            // purpose, and docking the lot of them because the window was dragged over an edge would undo that
            // without being asked.
            DockSide side = drop.Side;
            int index = drop.Index;

            Arrangement.Apply(layout => layout.Move(carried, side, index));
        };

        window.CloseRequested += () =>
        {
            int at = windows.IndexOf(window);

            if (at < 0)
                return;

            Arrangement.Apply(layout =>
            {
                DockLayout closed = layout;

                foreach (PanelId panel in layout.Floats[at].Panels)
                    closed = closed.Close(panel);

                return closed;
            });
        };

        return window;
    }

    /// <summary>
    /// The float one window is showing, or null when it no longer has one.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed, because a window outlives its float by moments: the arrangement can drop a
    /// float while the window it belonged to is still being dragged, and indexing into the list on that frame
    /// is how a move ends in an exception instead of a docked panel.
    /// </remarks>
    private DockFloat? Carrying(DockFloatWindow window)
    {
        int at = windows.IndexOf(window);

        return at >= 0 && at < Arrangement.Current.Floats.Count ? Arrangement.Current.Floats[at] : null;
    }
}
