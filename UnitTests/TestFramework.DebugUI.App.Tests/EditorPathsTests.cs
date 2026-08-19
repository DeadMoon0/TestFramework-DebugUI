using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TestFramework.DebugUI.Editors;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers finding the editors and deciding what to hand them.
/// </summary>
/// <remarks>
/// The interesting cases are all the ones this machine cannot demonstrate: neither editor installed, a
/// vswhere that answers with something other than a path, a project with no solution above it. Every method
/// under test takes its view of the disk as a delegate, which is what makes those cases reachable here.
/// </remarks>
public class EditorPathsTests
{
    [Fact]
    public void TheUserInstallOfCodeIsLookedForFirst()
    {
        // Which is where VS Code's own installer puts it by default, so it is where most machines have it.
        ImmutableList<string> candidates = EditorPaths.CodeCandidates(Env);

        Assert.Equal(@"C:\Users\ada\AppData\Local\Programs\Microsoft VS Code\Code.exe", candidates[0]);
        Assert.Contains(@"C:\Program Files\Microsoft VS Code\Code.exe", candidates);
        Assert.Contains(@"C:\Program Files (x86)\Microsoft VS Code\Code.exe", candidates);
    }

    [Fact]
    public void AMissingEnvironmentVariableContributesNoCandidate()
    {
        // Rather than a path rooted at nothing, which would be probed and might even exist.
        ImmutableList<string> candidates = EditorPaths.CodeCandidates(_ => null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void TheFirstCandidateThatExistsWins()
    {
        string? found = EditorPaths.FirstExisting(
            ["a.exe", "b.exe", "c.exe"],
            path => path is "b.exe" or "c.exe");

        Assert.Equal("b.exe", found);
    }

    [Fact]
    public void NothingIsFoundWhenNoCandidateExists()
    {
        // The machine without VS Code. The button has to disappear, so this has to be null and not a guess.
        Assert.Null(EditorPaths.FirstExisting(["a.exe", "b.exe"], _ => false));
    }

    [Fact]
    public void VsWhereOutputIsReadAsAPath()
    {
        Assert.Equal(
            @"C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\devenv.exe",
            EditorPaths.ProductPathFrom("C:\\Program Files\\Microsoft Visual Studio\\18\\Professional\\Common7\\IDE\\devenv.exe\r\n"));
    }

    [Fact]
    public void TheNewestOfSeveralInstallationsIsTakenFirst()
    {
        // vswhere prints one line per match; -latest puts the newest first and this trusts that order.
        string output = "C:\\VS\\2026\\devenv.exe\nC:\\VS\\2022\\devenv.exe\n";

        Assert.Equal(@"C:\VS\2026\devenv.exe", EditorPaths.ProductPathFrom(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("No instances found.")]
    [InlineData("vswhere : error : something went wrong")]
    public void AnythingThatIsNotAnExecutableIsNotAPath(string? output)
    {
        // vswhere prints nothing when no installation matches, and prints diagnostics when it fails. Either
        // passed off as a path would become a launch that fails in front of the reader.
        Assert.Null(EditorPaths.ProductPathFrom(output));
    }

    [Fact]
    public void ASolutionBesideTheProjectIsFound()
    {
        string? found = EditorPaths.SolutionNear(
            @"C:\src\app\App.Tests\App.Tests.csproj",
            directory => directory == @"C:\src\app\App.Tests" ? [@"C:\src\app\App.Tests\Local.sln"] : []);

        Assert.Equal(@"C:\src\app\App.Tests\Local.sln", found);
    }

    [Fact]
    public void ASolutionAboveTheProjectIsFound()
    {
        // The usual shape: tests in a subdirectory, solution at the repository root.
        string? found = EditorPaths.SolutionNear(
            @"C:\src\app\UnitTests\App.Tests\App.Tests.csproj",
            directory => directory == @"C:\src\app" ? [@"C:\src\app\App.sln"] : []);

        Assert.Equal(@"C:\src\app\App.sln", found);
    }

    [Fact]
    public void TheNearestSolutionWinsOverAHigherOne()
    {
        string? found = EditorPaths.SolutionNear(
            @"C:\src\app\UnitTests\App.Tests\App.Tests.csproj",
            directory => directory switch
            {
                @"C:\src\app\UnitTests" => [@"C:\src\app\UnitTests\Tests.sln"],
                @"C:\src\app" => [@"C:\src\app\App.sln"],
                _ => []
            });

        Assert.Equal(@"C:\src\app\UnitTests\Tests.sln", found);
    }

    [Fact]
    public void TheSearchDoesNotWalkToTheRootOfTheDrive()
    {
        // Or it would eventually find somebody else's solution and open that.
        List<string> looked = [];

        EditorPaths.SolutionNear(
            @"C:\a\b\c\d\e\f\g\Project.csproj",
            directory => { looked.Add(directory); return []; });

        Assert.DoesNotContain(@"C:\", looked);
    }

    [Fact]
    public void VisualStudioIsGivenTheSolutionItself()
    {
        string? target = EditorPaths.TargetFor(
            @"C:\src\app\UnitTests\App.Tests\App.Tests.csproj",
            wantsFolder: false,
            directory => directory == @"C:\src\app" ? [@"C:\src\app\App.sln"] : []);

        Assert.Equal(@"C:\src\app\App.sln", target);
    }

    [Fact]
    public void CodeIsGivenTheFolderAroundIt()
    {
        // Handed the solution file, VS Code opens one file and shows none of the code around it.
        string? target = EditorPaths.TargetFor(
            @"C:\src\app\UnitTests\App.Tests\App.Tests.csproj",
            wantsFolder: true,
            directory => directory == @"C:\src\app" ? [@"C:\src\app\App.sln"] : []);

        Assert.Equal(@"C:\src\app", target);
    }

    [Fact]
    public void AProjectWithNoSolutionIsOpenedOnItsOwn()
    {
        Assert.Equal(
            @"C:\src\loose\Thing.csproj",
            EditorPaths.TargetFor(@"C:\src\loose\Thing.csproj", wantsFolder: false, _ => []));

        Assert.Equal(
            @"C:\src\loose",
            EditorPaths.TargetFor(@"C:\src\loose\Thing.csproj", wantsFolder: true, _ => []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARunThatRecordedNoProjectHasNothingToOpen(string? projectFilePath)
    {
        // Recorded runs from before the project file was carried, and runs whose producer could not report
        // one. The button says so rather than opening something arbitrary.
        Assert.Null(EditorPaths.TargetFor(projectFilePath, wantsFolder: false, _ => []));
        Assert.Null(EditorPaths.TargetFor(projectFilePath, wantsFolder: true, _ => []));
    }

    [Fact]
    public void CodeIsGivenTheFolderAndThePositionTogether()
    {
        // Both, not one or the other: the workspace so the rest of the code is there, and the position so
        // the reader does not have to find one test in a suite.
        Assert.Equal(
            [@"C:\src\app", "--goto", @"C:\src\app\Tests\OrderTests.cs:42"],
            EditorPaths.ArgumentsFor(
                @"C:\src\app\Tests\Tests.csproj",
                wantsFolder: true,
                new SourceLocation { FilePath = @"C:\src\app\Tests\OrderTests.cs", Line = 42 },
                directory => directory == @"C:\src\app" ? [@"C:\src\app\App.sln"] : []));
    }

    [Fact]
    public void VisualStudioIsGivenTheFileToEditBecauseItCannotBeToldALine()
    {
        // devenv has no command-line way to say a line, so the solution is dropped in favour of landing on
        // the right file — which /edit opens inside the instance the reader already has open.
        Assert.Equal(
            ["/edit", @"C:\src\app\Tests\OrderTests.cs"],
            EditorPaths.ArgumentsFor(
                @"C:\src\app\Tests\Tests.csproj",
                wantsFolder: false,
                new SourceLocation { FilePath = @"C:\src\app\Tests\OrderTests.cs", Line = 42 },
                _ => [@"C:\src\app\App.sln"]));
    }

    [Fact]
    public void ARunWithNoSourceStillOpensItsSolution()
    {
        // Every run recorded before the source location was read here, and every imported one, which strips
        // local paths on purpose. The button keeps working, it just lands where it used to.
        Assert.Equal(
            [@"C:\src\app\App.sln"],
            EditorPaths.ArgumentsFor(
                @"C:\src\app\Tests\Tests.csproj",
                wantsFolder: false,
                source: null,
                directory => directory == @"C:\src\app" ? [@"C:\src\app\App.sln"] : []));
    }

    [Fact]
    public void AFileWithNoLineIsOpenedAtItsTop()
    {
        // A producer that reported the file and not the line. The right file with no position beats no
        // position and no file.
        // No solution above it either, so the folder is the project's own — the existing rule, unchanged.
        Assert.Equal(
            [@"C:\src\app\Tests", "--goto", @"C:\src\app\Tests\OrderTests.cs"],
            EditorPaths.ArgumentsFor(
                @"C:\src\app\Tests\Tests.csproj",
                wantsFolder: true,
                new SourceLocation { FilePath = @"C:\src\app\Tests\OrderTests.cs" },
                _ => []));
    }

    [Fact]
    public void ARunWithNeitherProjectNorSourceHasNothingToOpen()
    {
        Assert.Empty(EditorPaths.ArgumentsFor(null, wantsFolder: true, source: null, _ => []));
        Assert.Empty(EditorPaths.ArgumentsFor(null, wantsFolder: false, source: null, _ => []));
    }

    [Fact]
    public void ALineWithoutAFileIsNotALocation()
    {
        // The line is meaningless on its own, and a location pointing nowhere would become a button that
        // launches an editor on nothing.
        Assert.Null(SourceLocation.From(null, 42));
        Assert.Null(SourceLocation.From("   ", 42));
        Assert.Equal(0, SourceLocation.From(@"C:\x\Test.cs", -3)!.Line);
    }

    private static string? Env(string name) => name switch
    {
        "LOCALAPPDATA" => @"C:\Users\ada\AppData\Local",
        "ProgramFiles" => @"C:\Program Files",
        "ProgramFiles(x86)" => @"C:\Program Files (x86)",
        _ => null
    };
}
