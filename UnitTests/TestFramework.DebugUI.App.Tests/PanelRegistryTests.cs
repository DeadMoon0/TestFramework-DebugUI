using System;
using System.Linq;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the table that says what each panel is.
/// </summary>
/// <remarks>
/// The arrangement knows where panels are and this knows what they are, so this is where "a panel exists but
/// cannot be reached" would show up: the title bar's strip is built from this table, and a panel missing from it
/// would be one nothing could ever open.
/// </remarks>
public class PanelRegistryTests
{
    [Fact]
    public void EveryPanelIsDescribed()
    {
        // The strip is filled from this table, so an entry missing here is a panel with no way in.
        foreach (PanelId panel in Enum.GetValues<PanelId>())
            Assert.Equal(panel, PanelRegistry.Of(panel).Id);

        Assert.Equal(Enum.GetValues<PanelId>().Length, PanelRegistry.All.Count);
    }

    [Fact]
    public void EveryPanelHasAName()
    {
        Assert.All(PanelRegistry.All, descriptor => Assert.False(string.IsNullOrWhiteSpace(descriptor.Title)));

        // Distinct, because the titles are what a reader tells two headers apart by.
        Assert.Equal(
            PanelRegistry.All.Count,
            PanelRegistry.All.Select(descriptor => descriptor.Title).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryPanelHasAPictureOfItsOwn()
    {
        Assert.All(PanelRegistry.All, descriptor => Assert.False(string.IsNullOrWhiteSpace(descriptor.IconKey)));

        // A row of identical glyphs would name none of them, which is the failure this guards against.
        Assert.Equal(
            PanelRegistry.All.Count,
            PanelRegistry.All.Select(descriptor => descriptor.IconKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheRunsPageOpensInTheCentreBecauseItCannotFitARail()
    {
        // The one default placement that is not a preference: six columns in three hundred pixels is six
        // ellipses.
        Assert.Equal(DockSide.Center, PanelRegistry.DefaultSideOf(PanelId.Home));
    }

    [Fact]
    public void TheDefaultArrangementAgreesWithTheDefaultSides()
    {
        // Two tables that could disagree: the arrangement ships a factory layout, and each panel names where it
        // belongs. A panel opened at one place by default and reopened at another would look like a bug in the
        // strip.
        foreach (PanelDescriptor descriptor in PanelRegistry.All)
        {
            DockLocation at = DockLayout.Default.Locate(descriptor.Id);

            if (at.Placement == DockPlacement.Side)
                Assert.Equal(descriptor.DefaultSide, at.Side);
        }
    }

    [Fact]
    public void AnUnknownPanelIsRefusedRatherThanReturningNothing()
    {
        // Cast from an int the way a corrupt file's value would arrive. The registry is asked for a descriptor
        // by things that cannot proceed without one, so a null here would surface as a crash further away.
        Assert.Throws<InvalidOperationException>(() => PanelRegistry.Of((PanelId)99));
    }
}
