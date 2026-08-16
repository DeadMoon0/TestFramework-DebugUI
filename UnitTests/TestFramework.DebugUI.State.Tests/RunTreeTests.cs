using System;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers arranging runs as project, then test, then executions of it.
/// </summary>
public class RunTreeTests
{
    [Fact]
    public void RunsOfOneTestGroupUnderIt()
    {
        // The point of the whole thing: three runs of one test are three runs of one test, not three
        // unrelated rows that happen to share a name.
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Run("a", "Suite.Tests.Login", at: 3),
            Run("b", "Suite.Tests.Login", at: 2),
            Run("c", "Suite.Tests.Login", at: 1)
        ]);

        TestGroup test = Assert.Single(Assert.Single(tree).Tests);

        Assert.Equal(3, test.Runs.Count);
        Assert.Equal(["a", "b", "c"], test.Runs.Select(run => run.SessionId));
    }

    [Fact]
    public void TestsFromDifferentProjectsDoNotShareAGroup()
    {
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Run("a", "Suite.Tests.Login", project: @"C:\src\Alpha.Tests.dll"),
            Run("b", "Suite.Tests.Login", project: @"C:\src\Beta.Tests.dll")
        ]);

        Assert.Equal(2, tree.Count);
        Assert.Equal(["Alpha.Tests", "Beta.Tests"], tree.Select(group => group.Project).Order());
    }

    [Fact]
    public void AProjectIsNamedByItsFileRatherThanItsPath()
    {
        // Every run of a suite shares its path up to the last segment, so the path itself
        // distinguishes nothing and does not fit in a list.
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", "Suite.Tests.Login", project: @"C:\src\deep\Alpha.Tests.csproj")]);

        Assert.Equal("Alpha.Tests", Assert.Single(tree).Project);
    }

    [Fact]
    public void AnAssemblyNameKeepsItsLastSegment()
    {
        // A bare assembly name is dotted like a path is, and trimming its last segment as if it were
        // an extension would file the run under a project that does not exist.
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", project: "Acme.Billing.Tests")]);

        Assert.Equal("Acme.Billing.Tests", Assert.Single(tree).Project);
    }

    [Fact]
    public void ARunWithNoProjectIsStillGrouped()
    {
        // A run recorded before the path was carried, or by a producer that could not report one.
        // It is listed rather than dropped.
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", "Suite.Tests.Login", project: null)]);

        Assert.Equal("Unknown project", Assert.Single(tree).Project);
    }

    [Fact]
    public void ARunWithNoTestIdentityGroupsUnderItsOwnName()
    {
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", fullyQualifiedName: null, name: "ad hoc run")]);

        Assert.Equal("ad hoc run", Assert.Single(Assert.Single(tree).Tests).Test);
    }

    [Fact]
    public void TheMostRecentlyRunTestComesFirst()
    {
        // Ordered by what is being worked on now, not by an alphabet.
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Run("a", "Suite.Tests.Alpha", at: 1),
            Run("b", "Suite.Tests.Zulu", at: 5)
        ]);

        Assert.Equal(["Suite.Tests.Zulu", "Suite.Tests.Alpha"], Assert.Single(tree).Tests.Select(test => test.Test));
    }

    [Fact]
    public void AGroupWhoseLatestRunPassedStillReportsAnEarlierFailure()
    {
        // Rolled up by severity, not recency. A header that reported the newest run would hide the
        // one run in the group worth opening.
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Passed("new", at: 5),
            Failed("old", at: 1)
        ]);

        Assert.Equal(RunHealth.Failed, Assert.Single(tree).Health);
    }

    [Fact]
    public void ARunningRunOutweighsAFailedOne()
    {
        // Something still happening is the most useful thing a header can say.
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Failed("old", at: 1),
            Run("live", "Suite.Tests.Login", at: 5) with { IsLive = true }
        ]);

        Assert.Equal(RunHealth.Running, Assert.Single(tree).Health);
    }

    [Fact]
    public void AnEmptyListMakesNoGroups()
    {
        Assert.Empty(RunTree.Of([]));
        Assert.Equal(RunHealth.Unknown, RunTree.WorstOf([]));
    }

    [Fact]
    public void ALongTestNameIsShownByItsTail()
    {
        // Every test in a suite shares its leading namespaces, so a name trimmed from the right
        // shows the same prefix on every row.
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", "Very.Deep.Name.Space.LoginTests.Works")]);

        Assert.Equal("LoginTests.Works", Assert.Single(Assert.Single(tree).Tests).DisplayName);
    }

    private static RunSummary Run(
        string sessionId,
        string? fullyQualifiedName = "Suite.Tests.Login",
        string? project = @"C:\src\Alpha.Tests.dll",
        int at = 1,
        string name = "run")
        => new()
        {
            SessionId = sessionId,
            Name = name,
            FullyQualifiedName = fullyQualifiedName,
            ProjectPath = project,
            StartedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(at)
        };

    private static RunSummary Passed(string sessionId, int at)
        => Run(sessionId, at: at) with
        {
            IsFinished = true,
            Progress = RunProgress.Empty with { Steps = 1, AssertionsPassed = 1 }
        };

    private static RunSummary Failed(string sessionId, int at)
        => Run(sessionId, at: at) with
        {
            IsFinished = true,
            Progress = RunProgress.Empty with { Steps = 1, AssertionsFailed = 1 }
        };
}
