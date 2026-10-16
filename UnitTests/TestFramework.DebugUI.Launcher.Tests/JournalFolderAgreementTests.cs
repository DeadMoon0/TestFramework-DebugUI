using System;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// Holds the marker the launcher creates to the marker Core actually looks at.
/// </summary>
/// <remarks>
/// <para>
/// The launcher creates a file; Core — in another package, in another process, unpackaged while the
/// launcher is packaged — decides whether to record a run by whether that file is there.
/// Neither side can see the other's constant, because the launcher deliberately does not reference
/// Core, so the two paths agree only for as long as somebody remembers they have to.
/// </para>
/// <para>
/// That has already failed once, and expensively: the folder went uncreated for months while the
/// replay path that depends on it was written, reviewed and covered, and nothing anywhere reported a
/// problem — journalling simply read as switched off. Moving the root out of <c>AppData</c> is the
/// second time the same pair of constants has had to be changed together. So the agreement is a test
/// rather than a comment in two files.
/// </para>
/// </remarks>
public sealed class JournalFolderAgreementTests
{
    [Fact]
    public void TheLauncherCreatesTheMarkerCoreLooksAt()
    {
        // Core lets an environment variable redirect this, and caches whichever answer it read
        // first. Nothing in this assembly sets it, so on any ordinary machine the default is what
        // gets compared. If it is set, the comparison is not the one this test means to make — and
        // it says so rather than passing on a check that never happened.
        string? redirected = Environment.GetEnvironmentVariable("TESTFRAMEWORK_DEBUG_JOURNAL_DIR");

        Assert.True(
            string.IsNullOrWhiteSpace(redirected),
            $"TESTFRAMEWORK_DEBUG_JOURNAL_DIR is set to '{redirected}', so Core's default journal root cannot be read and this agreement cannot be checked.");

        LauncherPaths paths = new();

        Assert.Equal(DebugJournal.Root, paths.JournalFolder);
        Assert.Equal(DebugJournal.RecordingMarker, paths.RecordingMarker);
    }
}
