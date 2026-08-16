using System;
using TestFramework.DebugUI.Launcher;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// Covers reading a release out of what GitHub answers.
/// </summary>
/// <remarks>
/// Every unreadable answer has to end as "no release", never as an exception: the caller's contract
/// is that not knowing is ordinary, and the launcher carries on with what is installed.
/// </remarks>
public class ReleaseReadingTests
{
    [Fact]
    public void AReleaseIsReadFromItsTagAndItsPackage()
    {
        ReleaseInfo release = GitHubReleaseSource.Read("""
            { "tag_name": "v0.5.0", "assets": [ { "browser_download_url": "https://example.invalid/DebugUI-0.5.0.zip" } ] }
            """)!;

        Assert.Equal(Version.Parse("0.5.0"), release.Version);
        Assert.Equal("https://example.invalid/DebugUI-0.5.0.zip", release.DownloadUrl.ToString());
    }

    [Theory]
    [InlineData("v0.5.0", "0.5.0")]
    [InlineData("V0.5.0", "0.5.0")]
    [InlineData("0.5.0", "0.5.0")]
    [InlineData("0.5.0.2", "0.5.0.2")]
    public void ATagIsReadWithOrWithoutItsPrefix(string tag, string expected)
    {
        // Both shapes turn up: release tooling writes one, people write the other.
        Assert.Equal(Version.Parse(expected), ReleaseTag.Read(tag));
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void ATagThatIsNotAVersionIsNotARelease(string? tag)
    {
        // Not an error — just not something this launcher can install.
        Assert.Null(ReleaseTag.Read(tag));
    }

    [Fact]
    public void AReleaseWithNoPackageYetIsTreatedAsNoRelease()
    {
        // Publishing the notes before attaching the binaries is an ordinary few minutes of anyone's
        // release process. Updating to it would leave the tool unable to start.
        Assert.Null(GitHubReleaseSource.Read("""{ "tag_name": "v0.5.0", "assets": [] }"""));
    }

    [Fact]
    public void APackageThatIsNotAZipIsNotTakenForOne()
    {
        Assert.Null(GitHubReleaseSource.Read("""
            { "tag_name": "v0.5.0", "assets": [ { "browser_download_url": "https://example.invalid/notes.txt" } ] }
            """));
    }

    [Fact]
    public void TheZipIsPickedOutFromAmongTheOtherAssets()
    {
        ReleaseInfo release = GitHubReleaseSource.Read("""
            { "tag_name": "v0.5.0", "assets": [
                { "name": "checksums.txt", "browser_download_url": "https://example.invalid/checksums.txt" },
                { "name": "DebugUI.zip", "browser_download_url": "https://example.invalid/DebugUI.zip" } ] }
            """)!;

        Assert.EndsWith("DebugUI.zip", release.DownloadUrl.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheApplicationIsTakenByNameWhenTheReleaseCarriesSeveralArchives()
    {
        // A release grows assets over time — the launcher's own, symbols, a source snapshot. Taking
        // whichever zip came back first would install one of those the day a second is attached.
        ReleaseInfo release = GitHubReleaseSource.Read("""
            { "tag_name": "v0.5.0", "assets": [
                { "name": "TestFramework.DebugUI.Launcher.zip", "browser_download_url": "https://example.invalid/launcher.zip" },
                { "name": "TestFramework.DebugUI.zip", "browser_download_url": "https://example.invalid/app.zip" } ] }
            """)!;

        Assert.EndsWith("app.zip", release.DownloadUrl.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralUnnamedArchivesAreDeclinedRatherThanGuessedBetween()
    {
        // Installing the wrong application silently is worse than not updating. Not updating leaves
        // a working tool in place and says so.
        Assert.Null(GitHubReleaseSource.Read("""
            { "tag_name": "v0.5.0", "assets": [
                { "name": "one.zip", "browser_download_url": "https://example.invalid/one.zip" },
                { "name": "two.zip", "browser_download_url": "https://example.invalid/two.zip" } ] }
            """));
    }

    [Fact]
    public void AnAnswerWithNoTagIsNotARelease()
    {
        Assert.Null(GitHubReleaseSource.Read("""{ "assets": [] }"""));
    }
}
