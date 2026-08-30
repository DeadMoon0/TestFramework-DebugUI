using System;
using System.Globalization;
using TestFramework.DebugUI;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the one way this tool writes a duration.
/// </summary>
/// <remarks>
/// There were five of these and two of them had drifted, so the same step read one way on the board and
/// another in the panel beside it. What is pinned here is that there is one answer, not what the bands
/// happen to be.
/// </remarks>
public class DurationTextTests
{
    public DurationTextTests()
        => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    [Fact]
    public void SomethingTooFastToMeasureSaysSoRatherThanReadingAsNeverRun()
    {
        // "0 ms" is what a step that did not run would say, which is a much more alarming statement
        // than "too fast to have a number worth comparing".
        Assert.Equal("<1 ms", DurationText.Compact(TimeSpan.FromTicks(1)));
    }

    [Theory]
    [InlineData(1, "1 ms")]
    [InlineData(999, "999 ms")]
    public void UnderASecondIsMilliseconds(int milliseconds, string expected)
        => Assert.Equal(expected, DurationText.Compact(TimeSpan.FromMilliseconds(milliseconds)));

    [Theory]
    [InlineData(1000, "1 s")]
    [InlineData(1400, "1.4 s")]
    [InlineData(59_400, "59.4 s")]
    public void UnderAMinuteIsSeconds(int milliseconds, string expected)
        => Assert.Equal(expected, DurationText.Compact(TimeSpan.FromMilliseconds(milliseconds)));

    [Theory]
    [InlineData(84, "1m 24s")]
    [InlineData(65, "1m 05s")]
    public void UnderAnHourIsMinutesAndSeconds(int seconds, string expected)
        => Assert.Equal(expected, DurationText.Compact(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void AnHourOrMoreIsHoursAndMinutes()
        => Assert.Equal("2h 05m", DurationText.Compact(TimeSpan.FromMinutes(125)));

    [Fact]
    public void ANegativeSpanIsWrittenAsALength()
    {
        // A comparison against an earlier run produces one of these, and "quicker by" is the caller's
        // word for the direction — a minus sign in the number would say it twice.
        Assert.Equal("1.4 s", DurationText.Compact(TimeSpan.FromMilliseconds(-1400)));
    }
}
