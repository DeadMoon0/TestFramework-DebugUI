namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// The measurements every part of the dock draws to.
/// </summary>
/// <remarks>
/// Shared rather than repeated, because three things have to agree about them: the card that is drawn,
/// the divider that is inset between two cards, and the hint that shows where a dragged panel would
/// land. A hint with a different corner from the card it is promising is a hint that promises the
/// wrong thing.
/// </remarks>
internal static class DockMetrics
{
    /// <summary>The inset every card carries on all four sides.</summary>
    /// <remarks>
    /// One number, so the gap between two cards is always twice the gap to the window and the whole arrangement
    /// sits on one rhythm. Nothing docked ever touches anything else.
    /// </remarks>
    public const double CardGap = 7;

    /// <summary>How round a card's corners are.</summary>
    public const double CardRadius = 8;
}
