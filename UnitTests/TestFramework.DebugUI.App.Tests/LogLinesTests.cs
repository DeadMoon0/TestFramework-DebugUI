using System;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.Controls.Detail;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers how a step's log reads.
/// </summary>
/// <remarks>
/// The rows themselves need a window; the wording in them does not, and the wording is the part that has to
/// agree between what is on screen and what is on the clipboard.
/// </remarks>
public class LogLinesTests
{
    [Fact]
    public void EverySeverityTagIsTheSameWidth()
    {
        // Which is what lets the messages start at the same place on every row without measuring anything.
        Assert.Equal(4, LogLines.Tag(DebugLogLevel.Information).Length);
        Assert.Equal(4, LogLines.Tag(DebugLogLevel.Warning).Length);
        Assert.Equal(4, LogLines.Tag(DebugLogLevel.Error).Length);
    }

    [Fact]
    public void OnlyTheSeveritiesWorthLookingAtAreColoured()
    {
        // A log of forty ordinary lines should not look like forty things to attend to.
        Assert.Equal("TextFaint", LogLines.Brush(DebugLogLevel.Information));

        // And when there is something, it wears the colour that means the same thing on the board.
        Assert.Equal("StateTimeout", LogLines.Brush(DebugLogLevel.Warning));
        Assert.Equal("StateError", LogLines.Brush(DebugLogLevel.Error));
    }

    [Fact]
    public void AnEntryIsTimedOnTheReadersClock()
    {
        // The record is UTC and stays UTC. The reader is comparing it against the clock on their wall.
        DateTimeOffset moment = new(2026, 8, 18, 14, 44, 9, TimeSpan.Zero);

        LogNode entry = new() { OccurredAtUtc = moment, Level = DebugLogLevel.Information, Template = "Executing Step" };

        Assert.Equal(moment.ToLocalTime().ToString("HH:mm:ss"), LogLines.Time(entry));
    }

    [Fact]
    public void ALineCarriesTheTimeTheSeverityAndTheMessage()
    {
        LogNode entry = new()
        {
            OccurredAtUtc = new DateTimeOffset(2026, 8, 18, 14, 44, 9, TimeSpan.Zero),
            Level = DebugLogLevel.Error,
            Template = "Transform failed"
        };

        Assert.Equal($"{LogLines.Time(entry)}  FAIL  Transform failed", LogLines.Of(entry));
    }

    [Fact]
    public void AWholeLogCopiesAsOneLinePerEntry()
    {
        // One entry per line is the whole point of the change, and it has to survive the copy: a log pasted
        // into a ticket as one run-on paragraph is the thing that was wrong with it on screen.
        LogNode[] entries =
        [
            new() { OccurredAtUtc = DateTimeOffset.UnixEpoch, Level = DebugLogLevel.Information, Template = "first" },
            new() { OccurredAtUtc = DateTimeOffset.UnixEpoch, Level = DebugLogLevel.Warning, Template = "second" }
        ];

        string text = LogLines.Text(entries);

        Assert.Equal(2, text.Split(Environment.NewLine).Length);
        Assert.Contains("INFO  first", text, StringComparison.Ordinal);
        Assert.Contains("WARN  second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OneEntryIsNotReportedAsOneEntries()
    {
        Assert.Equal("1 entry", LogLines.Count(1));
        Assert.Equal("12 entries", LogLines.Count(12));
    }
}
