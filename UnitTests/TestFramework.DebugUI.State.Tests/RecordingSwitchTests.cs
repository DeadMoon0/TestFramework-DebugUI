using System;
using System.IO;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Shell;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers the recording switch: on means Core's marker is there, and off never costs a recorded run.
/// </summary>
public sealed class RecordingSwitchTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tf-recording-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AMachineWithNoJournalFolderIsNotRecording()
    {
        Assert.False(new RecordingSwitch(root).IsOn);
    }

    [Fact]
    public void AJournalFolderWithoutTheMarkerIsNotRecording()
    {
        // The folder outlives the switch: it holds the runs recorded so far.
        Directory.CreateDirectory(root);

        Assert.False(new RecordingSwitch(root).IsOn);
    }

    [Fact]
    public void TurningRecordingOnCreatesTheMarkerCoreLooksFor()
    {
        RecordingSwitch recording = new(root);

        recording.TurnOn();

        Assert.True(recording.IsOn);
        Assert.True(File.Exists(Path.Combine(root, DebugJournal.RecordingMarkerName)));
    }

    [Fact]
    public void TurningRecordingOffKeepsTheRunsRecordedSoFar()
    {
        RecordingSwitch recording = new(root);
        recording.TurnOn();

        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(runs);
        string recorded = Path.Combine(runs, "20261016-101500000-session.meta.json");
        File.WriteAllText(recorded, "{}");

        recording.TurnOff();

        Assert.False(recording.IsOn);
        Assert.True(File.Exists(recorded));
    }

    [Fact]
    public void TurningOnTwiceAndOffTwiceAreNotErrors()
    {
        RecordingSwitch recording = new(root);

        recording.TurnOff();
        recording.TurnOn();
        recording.TurnOn();
        Assert.True(recording.IsOn);

        recording.TurnOff();
        recording.TurnOff();
        Assert.False(recording.IsOn);
    }

    [Fact]
    public void TheStateSaysWhetherRunsAreRecordedAndWhere()
    {
        RecordingSwitch recording = new(root);

        Assert.Equal($"Recording: off — {root}", recording.Read().Summary);

        recording.TurnOn();

        Assert.Equal($"Recording: on — {root}", recording.Read().Summary);
    }

    [Fact]
    public void AnEmptyRunListPromisesKeptRunsOnlyWhileRecordingIsOn()
    {
        Assert.Contains("Recording is on", new RecordingState { IsOn = true, Root = root }.EmptyRuns, StringComparison.Ordinal);
        Assert.Contains("Recording is off", new RecordingState { IsOn = false, Root = root }.EmptyRuns, StringComparison.Ordinal);
        Assert.Contains("Recording is off", RecordingState.Unknown.EmptyRuns, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
