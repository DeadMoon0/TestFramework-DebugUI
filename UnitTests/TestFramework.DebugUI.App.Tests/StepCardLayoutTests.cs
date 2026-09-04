using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TestFramework.DebugUI.Controls.Board;
using TestFramework.DebugUI.Layout;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers a step card's corner, where two things that do not know about each other are drawn.
/// </summary>
/// <remarks>
/// <para>
/// The breakpoint marker hangs outside the card's box on purpose — a card that has not run is dimmed
/// to four tenths, and a breakpoint set on a step that has not run is exactly the one worth seeing —
/// so it is in no grid with the heading and nothing makes the two negotiate. They overlapped for as
/// long as the timing was short enough to miss, and then the comparison against the last passing run
/// added a second figure to the same line and put the marker through it.
/// </para>
/// <para>
/// This is the shape of test that only a laid-out control can carry: every assertion about what the
/// card <em>contains</em> passed throughout.
/// </para>
/// </remarks>
public sealed class StepCardLayoutTests
{
    [Fact]
    public void TheDurationDoesNotRunUnderTheBreakpointMarker()
    {
        Wpf.Run(() =>
        {
            // A card sized for a widget, which is where this goes wrong: a short card centres its
            // writing and the heading falls below the marker by luck, while a card with room for a
            // picture anchors its writing to the top - level with the marker, in the same corner.
            (FrameworkElement card, TextBlock duration, FrameworkElement marker) =
                Card(LayoutOptions.Default.StepHeightWithWidget);

            // As long as a slow step next to a comparison makes it - the widest this line ever gets.
            duration.Text = "14m 18s  +2m 04s";

            Wpf.Layout(card, card.Width, card.Height);

            Rect written = Wpf.BoundsOf(duration, card);
            Rect mark = Wpf.BoundsOf(marker, card);

            Assert.False(
                written.IntersectsWith(mark),
                $"The duration occupies {written} and the marker {mark}, so one is drawn over the other.");
        });
    }

    [Fact]
    public void AStepWithNothingToShowDrawsNoPicture()
    {
        // The widget area exists on every card and stays out of the way until there is something in
        // it. A card that reserved room for a picture it does not have would be a column of holes.
        Wpf.Run(() =>
        {
            (FrameworkElement card, _, _) = Card(LayoutOptions.Default.StepHeight);

            Wpf.Layout(card, card.Width, card.Height);

            Assert.Empty(Wpf.Descendants<Image>(card));
        });
    }

    /// <summary>
    /// A step card, with the two things whose corner is in question.
    /// </summary>
    /// <remarks>
    /// Found by shape rather than by name, because naming them would mean the board handing a test
    /// its own internals: the marker is the only sized box pinned to the card's top-right corner —
    /// the status dot is sized too, but sits in the heading's flow — and the duration is the only
    /// text pushed right.
    /// </remarks>
    private static (FrameworkElement Card, TextBlock Duration, FrameworkElement Marker) Card(double height)
    {
        // The composer rather than the whole board: what is under test is how a card is built, and it
        // no longer takes a control to build one.
        Canvas canvas = new();
        BoardComposer composer = new(canvas, canvas, () => null, (_, _) => { }, () => { });

        FrameworkElement card = (FrameworkElement)composer.BuildStep(new BoardVisuals(), LayoutResult.Empty, new LayoutNode
        {
            Id = "Main Stage/0",
            Kind = LayoutNodeKind.Step,
            StageName = "Main Stage",
            StepId = 0,
            X = 0,
            Y = 0,
            Width = LayoutOptions.Default.StepWidth,
            Height = height
        });

        Wpf.Layout(card, card.Width, card.Height);

        FrameworkElement marker = Wpf.Descendants<Border>(card).Single(border =>
            border.HorizontalAlignment == HorizontalAlignment.Right
            && border.VerticalAlignment == VerticalAlignment.Top
            && !double.IsNaN(border.Width));

        TextBlock duration = Wpf.Descendants<TextBlock>(card)
            .Single(block => block.HorizontalAlignment == HorizontalAlignment.Right);

        return (card, duration, marker);
    }
}
