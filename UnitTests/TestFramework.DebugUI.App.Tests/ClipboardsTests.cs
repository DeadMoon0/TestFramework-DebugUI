using System;
using TestFramework.DebugUI.Copying;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers what a field yields when it is copied.
/// </summary>
/// <remarks>
/// The clipboard itself is not tested — it needs a real one, and putting text on it is a single framework
/// call. What is worth testing is the shaping done first, because a value that arrives with a stray space on
/// the end is a value that will not match when it is pasted into a filter.
/// </remarks>
public class ClipboardsTests
{
    [Fact]
    public void TrailingWhitespaceIsDropped()
    {
        // These come from labels padded for layout. A qualified test name pasted into a --filter with a
        // space on the end matches nothing, and the reader has no way to see why.
        Assert.Equal("Suite.LoginTests.Works", Clipboards.Clean("Suite.LoginTests.Works   "));
        Assert.Equal("Suite.LoginTests.Works", Clipboards.Clean("Suite.LoginTests.Works\r\n"));
    }

    [Fact]
    public void InternalLineBreaksSurvive()
    {
        // A stack trace and a laid-out JSON body are worth having in the shape they were read in.
        string cleaned = Clipboards.Clean("first\nsecond\nthird")!;

        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}third", cleaned);
    }

    [Fact]
    public void LineEndingsAreNormalised()
    {
        // The journal, the pipe and the framework's own messages do not agree on line endings; whatever
        // arrives, one form goes out.
        Assert.Equal(
            $"a{Environment.NewLine}b{Environment.NewLine}c",
            Clipboards.Clean("a\r\nb\rc"));
    }

    [Fact]
    public void InternalBlankLinesSurvive()
    {
        // The failure text deliberately separates its message from its recovery steps with a blank line.
        Assert.Equal(
            $"message{Environment.NewLine}{Environment.NewLine}what to do",
            Clipboards.Clean("message\n\nwhat to do"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    public void AFieldWithNothingInItCopiesNothing(string? text)
    {
        // Which is what stops the button appearing over an empty label at all.
        Assert.Null(Clipboards.Clean(text));
    }

    [Fact]
    public void NothingIsCopiedWhenThereIsNothingToCopy()
    {
        Assert.False(Clipboards.Set(null));
        Assert.False(Clipboards.Set(string.Empty));
    }
}
