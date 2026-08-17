using System;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers building the command that runs a test again.
/// </summary>
/// <remarks>
/// The interesting half is what makes a re-run impossible. A filter guessed from an incomplete
/// identity does not fail — it runs the wrong tests and reports success, which is worse than not
/// offering the button at all.
/// </remarks>
public class RerunCommandTests
{
    [Fact]
    public void ARunWithAFullIdentityBecomesADotnetTestFilteredToThatTest()
    {
        Assert.True(RerunCommand.TryFor(Run(), out RerunCommand? command, out _));

        Assert.Equal("dotnet", command!.FileName);
        Assert.Contains("test \"C:\\src\\Acme.Billing.Tests\\Acme.Billing.Tests.csproj\"", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("--filter \"FullyQualifiedName=Acme.Billing.Tests.OrderTests.Accepts\"", command.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ItRunsFromTheProjectsOwnDirectory()
    {
        RerunCommand.TryFor(Run(), out RerunCommand? command, out _);

        Assert.Equal(@"C:\src\Acme.Billing.Tests", command!.WorkingDirectory);
    }

    [Fact]
    public void APathWithASpaceInItIsQuoted()
    {
        // Ordinary on Windows, and an unquoted path silently becomes two arguments and an error that
        // names neither.
        RerunCommand.TryFor(
            Run() with { ProjectFilePath = @"C:\my source\Acme.Tests\Acme.Tests.csproj" },
            out RerunCommand? command,
            out _);

        Assert.Contains("\"C:\\my source\\Acme.Tests\\Acme.Tests.csproj\"", command!.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunTheProducerSaidCannotBeRepeatedIsNotSecondGuessed()
    {
        // The producer knows whether the identity it resolved is complete. Deciding again here would
        // be two rules for one question.
        Assert.False(RerunCommand.TryFor(Run() with { CanRerun = false }, out _, out string reason));
        Assert.Contains("identity", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARunWithNoTestNameSaysSoRatherThanRunningEverything()
    {
        // An empty filter runs the whole project. That is the failure worth guarding: it looks like
        // it worked.
        Assert.False(RerunCommand.TryFor(Run() with { FullyQualifiedName = null }, out _, out string reason));
        Assert.Contains("which test", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAssemblyPathIsNotMistakenForAProject()
    {
        // Under a test runner the assembly path is the host process, and `dotnet test` needs
        // something it can build.
        Assert.False(RerunCommand.TryFor(Run() with { ProjectFilePath = @"C:\agent\bin\testhost.exe" }, out _, out string reason));
        Assert.Contains("project file", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARunWithNoProjectAtAllSaysSo()
    {
        Assert.False(RerunCommand.TryFor(Run() with { ProjectFilePath = null }, out _, out string reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void AvailabilityMatchesWhetherACommandCanBeBuilt()
    {
        // What the button binds to, so the two cannot disagree.
        Assert.True(RerunCommand.IsAvailableFor(Run()));
        Assert.False(RerunCommand.IsAvailableFor(Run() with { CanRerun = false }));
        Assert.False(RerunCommand.IsAvailableFor(null));
    }

    private static RunSummary Run() => new()
    {
        SessionId = "session-1",
        Name = "Accepts",
        FullyQualifiedName = "Acme.Billing.Tests.OrderTests.Accepts",
        ProjectFilePath = @"C:\src\Acme.Billing.Tests\Acme.Billing.Tests.csproj",
        CanRerun = true
    };
}
