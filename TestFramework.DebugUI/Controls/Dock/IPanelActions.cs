using System.Windows;
using System.Windows.Controls;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// A panel that offers buttons of its own, and lets whoever draws its chrome decide where they go.
/// </summary>
/// <remarks>
/// <para>
/// Reload, import, share and the like belong to the panel, not to the frame around it — but where they read best
/// depends on the frame. A panel with a header of its own puts them in it, beside the way out, because that is
/// where every window in every tool keeps the things it does. A panel sharing a tab strip cannot: the strip
/// belongs to several panels and buttons in it would look like they applied to all of them, so they drop back
/// inside the panel instead.
/// </para>
/// <para>
/// The panel hands over the container rather than a list, so the host moves one element and the panel keeps
/// deciding what is in it and in which order.
/// </para>
/// </remarks>
internal interface IPanelActions
{
    /// <summary>The panel's own buttons, as one element to be placed.</summary>
    FrameworkElement Actions { get; }

    /// <summary>
    /// Takes them back, for when the chrome has nowhere to put them.
    /// </summary>
    /// <remarks>
    /// The panel does this rather than the host, because where they live inside the panel is the panel's business
    /// — and something has to put them back, or moving a panel into a tab strip would leave its buttons behind in
    /// a header that no longer exists.
    /// </remarks>
    void ReclaimActions();
}

/// <summary>Shared plumbing for a panel whose actions can be lifted into a header.</summary>
internal static class PanelActions
{
    /// <summary>Takes an element out of whatever currently holds it, wherever that is.</summary>
    /// <remarks>
    /// One control has one parent, so the actions have to leave the header before they can go back into the panel
    /// and vice versa. Handles either kind of holder because a header is a grid and the panel's own slot is a
    /// border.
    /// </remarks>
    public static void Detach(FrameworkElement actions)
    {
        switch (actions?.Parent)
        {
            case Panel holder:
                holder.Children.Remove(actions);
                break;

            case Border border:
                border.Child = null;
                break;

            case ContentControl content:
                content.Content = null;
                break;
        }
    }
}
