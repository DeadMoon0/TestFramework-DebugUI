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
    /// <para>
    /// One control has one parent, so a thing has to leave where it is before it can go anywhere else —
    /// the actions leaving a header before they go back into their panel, and a panel leaving its card
    /// before the arrangement is redrawn around it.
    /// </para>
    /// <para>
    /// Every kind of holder, because there are three and which one is holding a given thing depends on
    /// where the reader last dragged it: a header is a panel, a card and a float are decorators, and a
    /// tab is a content control. Written twice once, and the second copy had a narrower case than the
    /// first — <see cref="Border"/> rather than <see cref="Decorator"/> — which is exactly the kind of
    /// difference two copies grow.
    /// </para>
    /// </remarks>
    public static void Detach(FrameworkElement? element)
    {
        switch (element?.Parent)
        {
            case Panel holder:
                holder.Children.Remove(element);
                break;

            case Decorator decorator:
                decorator.Child = null;
                break;

            case ContentControl content:
                content.Content = null;
                break;
        }
    }
}
