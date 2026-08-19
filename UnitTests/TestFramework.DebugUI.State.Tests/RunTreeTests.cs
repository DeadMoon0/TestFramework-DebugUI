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
    public void ATestIsLabelledByItsMethodAndItsClassByItsOwnRow()
    {
        // The class is a level, not part of the label. Written into every test's name it was the one
        // part of the name a suite's rows had in common, repeated down the whole rail.
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", "Very.Deep.Name.Space.LoginTests.Works")]);

        ClassGroup type = Assert.Single(Assert.Single(tree).Classes);

        Assert.Equal("LoginTests", type.DisplayName);
        Assert.Equal("Very.Deep.Name.Space.LoginTests", type.Class);
        Assert.Equal("Works", Assert.Single(type.Tests).DisplayName);
    }

    [Fact]
    public void TestsOfOneClassGroupUnderIt()
    {
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Run("a", "Suite.LoginTests.Accepts"),
            Run("b", "Suite.LoginTests.Rejects"),
            Run("c", "Suite.LogoutTests.Works")
        ]);

        ImmutableList<ClassGroup> classes = Assert.Single(tree).Classes;

        Assert.Equal(2, classes.Count);
        Assert.Equal(2, classes.Single(type => type.DisplayName == "LoginTests").Tests.Count);
        Assert.Single(classes.Single(type => type.DisplayName == "LogoutTests").Tests);
    }

    [Fact]
    public void ARunWithNoTestIdentityGetsAClassOfItsOwn()
    {
        // These are real: the tool is often pointed at a host process that never said which test it was
        // running. They are listed rather than dropped, and not filed under a blank heading.
        ImmutableList<ProjectGroup> tree = RunTree.Of([Run("a", fullyQualifiedName: null, name: "testhost")]);

        Assert.Equal(RunTree.NoIdentity, Assert.Single(Assert.Single(tree).Classes).Class);
    }

    [Fact]
    public void AProjectStillReportsItsTestsWithoutTheirClasses()
    {
        // The flattened view, for callers that want the tests and do not care which class they are in.
        ImmutableList<ProjectGroup> tree = RunTree.Of(
        [
            Run("a", "Suite.LoginTests.Accepts", at: 1),
            Run("b", "Suite.LogoutTests.Works", at: 5)
        ]);

        Assert.Equal(["Works", "Accepts"], Assert.Single(tree).Tests.Select(test => test.DisplayName));
    }

    [Fact]
    public void OnlyRunsWorthLookingAtNeedAttention()
    {
        // The whole basis of the page's two halves. Passed and never-opened are the bulk of any journal;
        // if either were to count as needing attention the strip would be the wall it replaced.
        Assert.True(RunTree.NeedsAttention(RunHealth.Failed));
        Assert.True(RunTree.NeedsAttention(RunHealth.Aborted));
        Assert.True(RunTree.NeedsAttention(RunHealth.Unproven));
        Assert.True(RunTree.NeedsAttention(RunHealth.Waiting));
        Assert.True(RunTree.NeedsAttention(RunHealth.Running));

        Assert.False(RunTree.NeedsAttention(RunHealth.Passed));
        Assert.False(RunTree.NeedsAttention(RunHealth.Unknown));
    }

    [Fact]
    public void AFilterMatchesAtWhicheverLevelTheReaderNamed()
    {
        // One box, four names. Someone thinking of a class and someone thinking of a test are asking the
        // same question of the same field, and neither should have to know which level they are naming.
        RunSummary run = Run("a", fullyQualifiedName: "Acme.Orders.Tests.OrderTests.PlacesAnOrder");

        Assert.True(Matches(run, "PlacesAnOrder"));
        Assert.True(Matches(run, "OrderTests"));
        Assert.True(Matches(run, "Alpha.Tests"));
        Assert.True(Matches(run, "run"));

        Assert.False(Matches(run, "Billing"));
    }

    [Fact]
    public void AFilterIgnoresCaseAndTakesAWildcard()
    {
        // The same pattern rules the run search uses, so nobody has to learn two kinds of typing.
        RunSummary run = Run("a", fullyQualifiedName: "Acme.Orders.Tests.OrderTests.PlacesAnOrder");

        Assert.True(Matches(run, "placesanorder"));
        Assert.True(Matches(run, "Places*Order"));
        Assert.True(Matches(run, "/Places(An)?Order/"));

        Assert.False(Matches(run, "Places?Order"));
    }

    [Fact]
    public void ARunWithNoIdentityIsStillFindableUnderTheNameItGroupsBy()
    {
        // The tool is often pointed at a host that never said which test it was running. Those runs group
        // under a name of their own, so that name has to be one the filter answers to.
        RunSummary run = Run("a", fullyQualifiedName: null, name: "orphan");

        Assert.True(Matches(run, RunTree.NoIdentity));
        Assert.True(Matches(run, "orphan"));
    }

    [Fact]
    public void FilteringTheRunsIsWhatPrunesTheTree()
    {
        // No separate pruning pass: the grouping is derived, so a branch survives exactly as long as one of
        // its runs does. This is the test that fails if anyone adds one.
        ImmutableList<RunSummary> runs =
        [
            Run("a", fullyQualifiedName: "Suite.Tests.Login"),
            Run("b", fullyQualifiedName: "Suite.Tests.Logout")
        ];

        Assert.True(SearchPattern.TryParse("Login", out SearchPattern? pattern, out _));

        ImmutableList<ProjectGroup> tree = RunTree.Of([.. runs.Where(run => RunTree.Matches(run, pattern!))]);

        ClassGroup type = Assert.Single(Assert.Single(tree).Classes);

        Assert.Equal("Login", Assert.Single(type.Tests).DisplayName);
    }

    private static bool Matches(RunSummary run, string filter)
    {
        Assert.True(SearchPattern.TryParse(filter, out SearchPattern? pattern, out _));

        return RunTree.Matches(run, pattern!);
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
