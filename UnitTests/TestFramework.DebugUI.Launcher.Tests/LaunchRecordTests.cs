using System;
using System.IO;
using TestFramework.DebugUI.Launcher;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// Covers what the launcher remembers about versions that would not start.
/// </summary>
/// <remarks>
/// The distinction all of this exists for: a version folder proves a download finished, not that the
/// application inside it has ever opened a window. Without somewhere to keep the second fact, a
/// broken update is offered again on every start, forever, and the only way out is knowing to hold
/// shift.
/// </remarks>
public sealed class LaunchRecordTests
{
    [Fact]
    public void AnAttemptNobodyConfirmedBecomesAQuarantine()
    {
        // The mechanism in one test. The attempt was written before the start; finding it still
        // there on the next start is the evidence that the start never got anywhere.
        LaunchRecord settled = LaunchRecord.Empty.Attempting(V("0.5.0")).Settled();

        Assert.Null(settled.Attempted);
        Assert.Equal([V("0.5.0")], settled.Quarantined);
    }

    [Fact]
    public void SettlingChangesNothingWhenThereWasNoAttempt()
    {
        // The ordinary case: last start confirmed what it launched, so there is nothing to conclude.
        Assert.Same(LaunchRecord.Empty, LaunchRecord.Empty.Settled());
    }

    [Fact]
    public void SurvivingClearsTheAttemptAndLiftsAnEarlierQuarantine()
    {
        // A version that works now is not suspect however it behaved before — the machine may have
        // been missing a runtime that has since been installed. Holding it against the build forever
        // would strand someone on an older one for a reason that has gone away.
        LaunchRecord record = LaunchRecord.Empty.Quarantining(V("0.5.0")).Attempting(V("0.5.0"));

        LaunchRecord survived = record.Survived(V("0.5.0"));

        Assert.Null(survived.Attempted);
        Assert.Empty(survived.Quarantined);
    }

    [Fact]
    public void SurvivingLeavesOtherVersionsQuarantined()
    {
        LaunchRecord record = LaunchRecord.Empty.Quarantining(V("0.5.0")).Quarantining(V("0.4.0"));

        Assert.Equal([V("0.5.0")], record.Survived(V("0.4.0")).Quarantined);
    }

    [Fact]
    public void QuarantiningTheSameVersionTwiceDoesNotListItTwice()
    {
        // It happens: the fallback fails, gets quarantined, and is tried again as a last resort
        // later because nothing else will start either.
        LaunchRecord record = LaunchRecord.Empty.Quarantining(V("0.5.0")).Quarantining(V("0.5.0"));

        Assert.Equal([V("0.5.0")], record.Quarantined);
    }

    [Fact]
    public void AMachineThatHasNeverRunTheLauncherRemembersNothing()
    {
        Assert.Null(LaunchRecord.Empty.Attempted);
        Assert.Empty(LaunchRecord.Empty.Quarantined);
    }

    private static Version V(string version) => Version.Parse(version);
}

/// <summary>
/// Covers reading and writing the record.
/// </summary>
/// <remarks>
/// All of it is about failing safely. The record is what stops the launcher offering a build that
/// does not run; none of that is worth a launcher that will not start, so every failure here has to
/// resolve to "remember nothing" and carry on.
/// </remarks>
public sealed class LaunchRecordStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tf-record-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WhatWasWrittenComesBack()
    {
        LaunchRecord written = LaunchRecord.Empty.Quarantining(V("0.5.0")).Attempting(V("0.4.0"));

        Store().Write(written);

        LaunchRecord read = Store().Read();

        Assert.Equal(V("0.4.0"), read.Attempted);
        Assert.Equal([V("0.5.0")], read.Quarantined);
    }

    [Fact]
    public void AMissingFileReadsAsRememberingNothing()
    {
        Assert.Same(LaunchRecord.Empty, Store().Read());
    }

    [Fact]
    public void AFileThatIsNotJsonReadsAsRememberingNothing()
    {
        // A half-written file after a machine lost power, or something else's file under the same
        // name. Either way the launcher's job is to start the tool, not to complain about this.
        Directory.CreateDirectory(root);
        File.WriteAllText(Store().FilePath, "{ this is not json");

        Assert.Same(LaunchRecord.Empty, Store().Read());
    }

    [Fact]
    public void AQuarantineEntryThatIsNotAVersionIsDroppedAndTheRestKept()
    {
        // The file is meant to be editable by hand — taking a version out of the quarantine is a
        // reasonable thing to want — so one bad line must not cost the other lines.
        Directory.CreateDirectory(root);
        File.WriteAllText(Store().FilePath, """
            { "attempted": null, "quarantined": [ "0.5.0", "not-a-version", "0.3.0" ] }
            """);

        Assert.Equal([V("0.5.0"), V("0.3.0")], Store().Read().Quarantined);
    }

    [Fact]
    public void AnAttemptThatIsNotAVersionReadsAsNoAttempt()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Store().FilePath, """{ "attempted": "yesterday" }""");

        Assert.Null(Store().Read().Attempted);
    }

    [Fact]
    public void WritingIntoAPlaceThatDoesNotExistYetCreatesIt()
    {
        // First run on a new machine: nothing under the data root exists.
        Store().Write(LaunchRecord.Empty.Attempting(V("0.5.0")));

        Assert.Equal(V("0.5.0"), Store().Read().Attempted);
    }

    private LaunchRecordStore Store() => new(new LauncherPaths(root));

    private static Version V(string version) => Version.Parse(version);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
