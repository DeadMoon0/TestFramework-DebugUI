using System;
using System.Linq;
using TestFramework.DebugUI.Launcher;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// Covers what the launcher decides to start.
/// </summary>
/// <remarks>
/// The rule these all serve: an update must never stand between someone and their tool. A launcher
/// that refuses to start because a release feed is unreachable has turned a convenience into an
/// outage, and it does so exactly when someone is offline trying to read a failed run.
/// </remarks>
public class LaunchPlannerTests
{
    [Fact]
    public void AnUnreachableFeedStartsWhatIsAlreadyInstalled()
    {
        LaunchDecision decision = LaunchPlanner.Decide([V("0.4.0"), V("0.3.0")], latest: null);

        Assert.Equal(LaunchAction.Start, decision.Action);
        Assert.Equal(V("0.4.0"), decision.Version);
    }

    [Fact]
    public void ANewerReleaseIsFetchedFirst()
    {
        LaunchDecision decision = LaunchPlanner.Decide([V("0.4.0")], Release("0.5.0"));

        Assert.Equal(LaunchAction.Update, decision.Action);
        Assert.Equal(V("0.5.0"), decision.Version);
    }

    [Fact]
    public void AReleaseNoNewerThanWhatIsHereIsNotFetchedAgain()
    {
        LaunchDecision decision = LaunchPlanner.Decide([V("0.5.0")], Release("0.5.0"));

        Assert.Equal(LaunchAction.Start, decision.Action);
        Assert.Equal(V("0.5.0"), decision.Version);
    }

    [Fact]
    public void AnOlderReleaseNeverDragsAnInstalledVersionBackwards()
    {
        // A feed can go backwards — a release pulled, or a tag corrected. Following it down would
        // uninstall someone's working tool on their behalf.
        LaunchDecision decision = LaunchPlanner.Decide([V("0.5.0")], Release("0.4.0"));

        Assert.Equal(LaunchAction.Start, decision.Action);
        Assert.Equal(V("0.5.0"), decision.Version);
    }

    [Fact]
    public void NothingInstalledAndAReachableFeedInstallsTheRelease()
    {
        LaunchDecision decision = LaunchPlanner.Decide([], Release("0.5.0"));

        Assert.Equal(LaunchAction.Update, decision.Action);
        Assert.Contains("Installing", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingInstalledAndNoFeedIsTheOneHonestFailure()
    {
        // The only case with nothing to start. Everything else starts something.
        LaunchDecision decision = LaunchPlanner.Decide([], latest: null);

        Assert.Equal(LaunchAction.Stuck, decision.Action);
        Assert.Null(decision.Version);
    }

    [Fact]
    public void AChosenVersionOutranksANewerRelease()
    {
        // Someone who just rolled back because the newest build is broken must not be updated
        // forward again by the next start.
        LaunchDecision decision = LaunchPlanner.Decide([V("0.5.0"), V("0.4.0")], Release("0.6.0"), pinned: V("0.4.0"));

        Assert.Equal(LaunchAction.Start, decision.Action);
        Assert.Equal(V("0.4.0"), decision.Version);
    }

    [Fact]
    public void AChosenVersionThatIsNotInstalledIsIgnoredRatherThanObeyed()
    {
        // Asking for something that is not there should start the tool, not refuse to.
        LaunchDecision decision = LaunchPlanner.Decide([V("0.5.0")], latest: null, pinned: V("0.1.0"));

        Assert.Equal(LaunchAction.Start, decision.Action);
        Assert.Equal(V("0.5.0"), decision.Version);
    }

    [Fact]
    public void TheStatusAlwaysSaysSomethingAboutWhatIsHappening()
    {
        // The launcher's whole interface is one line. A blank one is a launcher that looks hung.
        Assert.All(
            new[]
            {
                LaunchPlanner.Decide([V("0.4.0")], latest: null),
                LaunchPlanner.Decide([], Release("0.5.0")),
                LaunchPlanner.Decide([], latest: null)
            },
            decision => Assert.False(string.IsNullOrWhiteSpace(decision.Reason)));
    }

    [Fact]
    public void OnlyTheOldestVersionsBeyondTheKeptFewArePruned()
    {
        Version[] installed = [V("0.5.0"), V("0.4.0"), V("0.3.0"), V("0.2.0"), V("0.1.0")];

        Version[] prunable = [.. LaunchPlanner.Prunable(installed, running: V("0.5.0"), keep: 3)];

        Assert.Equal([V("0.2.0"), V("0.1.0")], prunable);
    }

    [Fact]
    public void TheVersionAboutToRunIsNeverPrunedHoweverOldItIs()
    {
        // A person who pinned an old version has said that is the one they want; deleting it out
        // from under the start would be the launcher overruling them.
        Version[] installed = [V("0.5.0"), V("0.4.0"), V("0.3.0"), V("0.2.0"), V("0.1.0")];

        Version[] prunable = [.. LaunchPlanner.Prunable(installed, running: V("0.1.0"), keep: 3)];

        Assert.DoesNotContain(V("0.1.0"), prunable);
    }

    [Fact]
    public void KeepingMoreVersionsThanExistPrunesNothing()
    {
        Assert.Empty(LaunchPlanner.Prunable([V("0.2.0"), V("0.1.0")], running: V("0.2.0"), keep: 3));
    }

    private static Version V(string version) => Version.Parse(version);

    private static ReleaseInfo Release(string version) => new()
    {
        Version = V(version),
        DownloadUrl = new Uri("https://example.invalid/app.zip")
    };
}
