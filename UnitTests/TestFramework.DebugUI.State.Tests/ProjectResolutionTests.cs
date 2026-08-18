using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers finding, on this machine, the project a shared run came from.
/// </summary>
/// <remarks>
/// The file system is injected, so these are about the rules rather than about whose machine the tests run on.
/// Paths are written in the separator of the platform under test, because the whole point is comparing a path
/// written elsewhere against one written here.
/// </remarks>
public class ProjectResolutionTests
{
    private static readonly char S = Path.DirectorySeparatorChar;

    private static string Sender(params string[] parts) => Join([.. new[] { "C:", "Users", "Them", "src" }, .. parts]);

    private static string Local(params string[] parts) => Join([.. new[] { "D:", "work" }, .. parts]);

    [Fact]
    public void ARunFromThisMachineIsLeftAlone()
    {
        // Nothing to translate, and nothing returned: the caller treats the result as overrides only.
        string here = Local("Thing", "Alpha.Tests", "Alpha.Tests.csproj");

        ImmutableDictionary<string, string> resolved =
            ProjectResolution.ResolveAll([Run("a", here)], path => path == here);

        Assert.Empty(resolved);
    }

    [Fact]
    public void AnImportedRunFindsTheSameProjectByName()
    {
        // The step that does the work in practice: the reader has run this suite themselves, so their own
        // history already says where it lives here. No search, no configuration.
        string theirs = Sender("Thing", "Alpha.Tests", "Alpha.Tests.csproj");
        string mine = Local("Thing", "Alpha.Tests", "Alpha.Tests.csproj");

        ImmutableDictionary<string, string> resolved = ProjectResolution.ResolveAll(
            [Run("imported", theirs), Run("mine", mine)],
            path => path == mine);

        Assert.Equal(mine, resolved["imported"]);
        Assert.False(resolved.ContainsKey("mine"));
    }

    [Fact]
    public void AProjectNobodyHereHasRunIsFoundThroughOneThatDid()
    {
        // The reason the ladder has a third step. Only Alpha is in the reader's history; Beta arrived in the
        // same bundle and has never been run here, but once Alpha resolves the two roots are known.
        string theirAlpha = Sender("Thing", "Alpha.Tests", "Alpha.Tests.csproj");
        string theirBeta = Sender("Thing", "Beta.Tests", "Beta.Tests.csproj");
        string myAlpha = Local("Thing", "Alpha.Tests", "Alpha.Tests.csproj");
        string myBeta = Local("Thing", "Beta.Tests", "Beta.Tests.csproj");

        ImmutableDictionary<string, string> resolved = ProjectResolution.ResolveAll(
            [Run("alpha", theirAlpha), Run("beta", theirBeta), Run("mine", myAlpha)],
            path => path == myAlpha || path == myBeta);

        Assert.Equal(myAlpha, resolved["alpha"]);
        Assert.Equal(myBeta, resolved["beta"]);
    }

    [Fact]
    public void AnAnonymousExportResolvesToo()
    {
        // An anonymised path keeps its tail and loses its head, and the head is exactly what is being replaced.
        // The placeholder is just an opaque prefix as far as this is concerned.
        string anonymised = Join("<user>", "source", "repos", "Thing", "Alpha.Tests", "Alpha.Tests.csproj");
        string mine = Local("Thing", "Alpha.Tests", "Alpha.Tests.csproj");

        ImmutableDictionary<string, string> resolved = ProjectResolution.ResolveAll(
            [Run("anon", anonymised), Run("mine", mine)],
            path => path == mine);

        Assert.Equal(mine, resolved["anon"]);
    }

    [Fact]
    public void AProjectThatIsNowhereHereStaysUnresolved()
    {
        // Left alone on purpose. Re-run then reports why it cannot run rather than aiming at the wrong project,
        // which would not fail — it would run different tests and pass.
        ImmutableDictionary<string, string> resolved = ProjectResolution.ResolveAll(
            [Run("orphan", Sender("Other", "Gamma.Tests", "Gamma.Tests.csproj"))],
            _ => false);

        Assert.Empty(resolved);
    }

    [Fact]
    public void ARunWithNoProjectAtAllIsNotGuessedAt()
    {
        ImmutableDictionary<string, string> resolved =
            ProjectResolution.ResolveAll([Run("none", null)], _ => true);

        Assert.Empty(resolved);
    }

    [Fact]
    public void TwoFilesSharingOnlyANameTeachNothing()
    {
        // One shared segment is a coincidence, not a repository shape. Learning a root from it would rewrite
        // unrelated paths into somewhere that happens to exist.
        Assert.Null(ProjectResolution.RootMap.Learn(@"C:\a\X.csproj", @"D:\totally\different\X.csproj"));
    }

    [Fact]
    public void ARootIsLearnedFromTheSharedTail()
    {
        ProjectResolution.RootMap? map = ProjectResolution.RootMap.Learn(
            Sender("Thing", "Alpha.Tests", "Alpha.Tests.csproj"),
            Local("Thing", "Alpha.Tests", "Alpha.Tests.csproj"));

        Assert.NotNull(map);

        // The longest shared tail, so what is left is the part that belongs to a machine rather than to a
        // repository. "Thing" is shared and therefore stays in the tail — which is what lets the mapping resolve
        // a sibling project under the same repository root.
        Assert.Equal(Join("C:", "Users", "Them", "src"), map.From);
        Assert.Equal(Join("D:", "work"), map.To);
    }

    [Fact]
    public void ARootIsMatchedBySegmentRatherThanByCharacter()
    {
        // "Alpha.Tests" and "Alpha.TestsExtra" line up for eleven characters and are different folders. Matching
        // by character would learn a root ending in the middle of a name.
        ProjectResolution.RootMap? map = ProjectResolution.RootMap.Learn(
            Join("C:", "src", "Alpha.TestsExtra", "Shared.csproj"),
            Join("D:", "work", "Alpha.Tests", "Shared.csproj"));

        Assert.Null(map);
    }

    [Fact]
    public void AMappingOnlyAppliesToPathsUnderIt()
    {
        ProjectResolution.RootMap map = new() { From = Join("C:", "src"), To = Join("D:", "work") };

        Assert.Equal(Join("D:", "work", "Thing", "a.csproj"), map.Apply(Join("C:", "src", "Thing", "a.csproj")));
        Assert.Null(map.Apply(Join("E:", "elsewhere", "a.csproj")));
    }

    [Fact]
    public void AMappingReadsAPathWrittenWithEitherSeparator()
    {
        // A journal carries whatever the host gave it.
        ProjectResolution.RootMap map = new() { From = Join("C:", "src"), To = Join("D:", "work") };

        Assert.Equal(Join("D:", "work", "Thing", "a.csproj"), map.Apply("C:/src/Thing/a.csproj"));
    }

    [Fact]
    public void AResolvedRunCanBeRerun()
    {
        // The point of the whole exercise: the command that could not be built before can be built now.
        string theirs = Sender("Thing", "Alpha.Tests", "Alpha.Tests.csproj");
        string mine = Local("Thing", "Alpha.Tests", "Alpha.Tests.csproj");

        RunSummary imported = Run("imported", theirs) with
        {
            CanRerun = true,
            FullyQualifiedName = "Alpha.Tests.LoginTests.Works"
        };

        Assert.False(RerunCommand.IsAvailableFor(imported with { ProjectFilePath = theirs + ".missing" })
                     && !File.Exists(theirs));

        ImmutableDictionary<string, string> resolved =
            ProjectResolution.ResolveAll([imported, Run("mine", mine)], path => path == mine);

        RunSummary located = imported with { ProjectFilePath = resolved["imported"] };

        Assert.True(RerunCommand.TryFor(located, out RerunCommand? command, out string reason), reason);
        Assert.Contains(mine, command!.Arguments, StringComparison.Ordinal);
        Assert.Contains("Alpha.Tests.LoginTests.Works", command.Arguments, StringComparison.Ordinal);
    }

    private static RunSummary Run(string sessionId, string? projectFilePath) => new()
    {
        SessionId = sessionId,
        Name = sessionId,
        ProjectFilePath = projectFilePath
    };

    /// <summary>A path in this platform's own separator, which is what the rules compare.</summary>
    private static string Join(params string[] parts) => string.Join(S, parts);
}
