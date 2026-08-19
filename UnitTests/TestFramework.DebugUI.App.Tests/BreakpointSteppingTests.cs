using TestFramework.Core.Debugger;
using TestFramework.DebugUI;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the single step that step-forward arms, and which test a mark belongs to.
/// </summary>
/// <remarks>
/// Worth testing on its own because the thing being decided is whether a run stops, and it is decided on
/// the transport's reader thread with no UI in the loop. Getting it wrong either strands a run that nobody
/// asked to hold, or stops the wrong one of several attached runs — or, as it did for several releases,
/// stops a completely different test that happened to name its stages the same way.
///
/// <see cref="Breakpoints"/> is static, so every test here clears it first rather than trusting the order
/// they run in.
/// </remarks>
[Collection("breakpoints")]
public class BreakpointSteppingTests
{
    private const string Test = "Acme.Orders.Tests.OrderTests.PlacesAnOrder";

    [Fact]
    public void AnArmedRunStopsAtItsNextStep()
    {
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");

        Assert.True(Breakpoints.ShouldPause(Ask("session-1", "Main", 7)));
    }

    [Fact]
    public void TheStepIsSpentOnce()
    {
        // The whole difference between a step and a mode. Left armed, the run would stop at every
        // remaining step and there would be no way to let it finish.
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");

        Assert.True(Breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
        Assert.False(Breakpoints.ShouldPause(Ask("session-1", "Main", 2)));
    }

    [Fact]
    public void AnotherRunCannotSpendIt()
    {
        // Several runs can be attached at once. A bare flag would be taken by whichever reached a step
        // first, which would stop a run nobody was looking at and let the stepped one go.
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");

        Assert.False(Breakpoints.ShouldPause(Ask("session-2", "Main", 1)));
        Assert.True(Breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
    }

    [Fact]
    public void AStepIsSpentEvenWhereABreakpointWouldHaveHeldAnyway()
    {
        // Otherwise the run stops twice in the same place: once for the step, once for the mark still
        // pending, which reads as step-forward having done nothing at all.
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Main", 1);
        Breakpoints.StepOnce("session-1");

        Assert.True(Breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
        Assert.False(Breakpoints.IsStepping("session-1"));

        Breakpoints.Clear();
    }

    [Fact]
    public void AWithdrawnStepDoesNotHoldTheRunLater()
    {
        // The release has to be armed before it is sent, so a release that fails has to be undone.
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");
        Breakpoints.CancelStep("session-1");

        Assert.False(Breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
    }

    [Fact]
    public void OneRunCannotWithdrawAnother()
    {
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");
        Breakpoints.CancelStep("session-2");

        Assert.True(Breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public void ClearingTakesThePendingStepWithIt()
    {
        Breakpoints.StepOnce("session-1");
        Breakpoints.Clear();

        Assert.False(Breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public void RestoringSavedBreakpointsTakesThePendingStepWithIt()
    {
        // Restore runs at start-up. A step left over from a previous session would hold the first run of
        // the next one at a step nobody had asked about.
        Breakpoints.StepOnce("session-1");
        Breakpoints.Restore([]);

        Assert.False(Breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public void AMarkedStepStillHoldsWithNoStepArmed()
    {
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Main", 4);

        Assert.True(Breakpoints.ShouldPause(Ask("session-1", "Main", 4)));
        Assert.False(Breakpoints.ShouldPause(Ask("session-1", "Main", 5)));

        Breakpoints.Clear();
    }

    [Fact]
    public void AMarkSetOnOneTestDoesNotStopAnother()
    {
        // The bug this key exists to fix. Stage names are conventional — Arrange, Act, Assert — so a mark
        // on step 2 of Act used to stop step 2 of Act in every other test that ran afterwards, and it was
        // saved to disk, so it did it again the next day.
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Act", 2);

        Assert.True(Breakpoints.ShouldPause(Ask("session-1", "Act", 2, Test)));
        Assert.False(Breakpoints.ShouldPause(Ask("session-2", "Act", 2, "Acme.Billing.Tests.InvoiceTests.Sends")));

        Breakpoints.Clear();
    }

    [Fact]
    public void AMarkAppliesToTheSameTestRunAgain()
    {
        // Which is the property that makes a breakpoint worth keeping at all: you stop the run, fix the
        // code, run it again, and the mark is still where you put it — under a new session id.
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Act", 2);

        Assert.True(Breakpoints.ShouldPause(Ask("a-later-session", "Act", 2, Test)));

        Breakpoints.Clear();
    }

    [Fact]
    public void ARunThatNamedNoTestIsNeverMatchedAgainstMarks()
    {
        // Treating "unknown" as a name would put every unidentifiable run in one bucket, which is the
        // collision again with a different spelling.
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Act", 2);

        Assert.False(Breakpoints.ShouldPause(Ask("session-1", "Act", 2, test: null)));

        Breakpoints.Clear();
    }

    [Fact]
    public void ATestThatCannotBeNamedCannotBeMarked()
    {
        // Rather than filing the mark under an empty name, where it would apply to every other run this
        // window could not name.
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(null);

        Assert.False(Breakpoints.Toggle("Act", 2));
        Assert.False(Breakpoints.IsSet("Act", 2));
        Assert.Empty(Breakpoints.Snapshot());
    }

    [Fact]
    public void MarksAreReadAgainstTheTestOnScreen()
    {
        // The board asks whether to light a marker without naming a test, so what it gets back has to be
        // the marks of the run it is showing and not of every run.
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Act", 2);

        Assert.True(Breakpoints.IsSet("Act", 2));

        Breakpoints.NowLookingAt("Acme.Billing.Tests.InvoiceTests.Sends");
        Assert.False(Breakpoints.IsSet("Act", 2));

        Breakpoints.Clear();
    }

    [Fact]
    public void ASavedMarkSurvivesARoundTripThroughTheSettingsFile()
    {
        Breakpoints.Clear();
        Breakpoints.NowLookingAt(Test);
        Breakpoints.Toggle("Act", 2);

        BreakpointMark saved = Assert.Single(Breakpoints.Snapshot());
        Assert.Equal(Test, saved.Test);

        Breakpoints.Clear();
        Breakpoints.Restore([saved]);

        Assert.True(Breakpoints.ShouldPause(Ask("session-9", "Act", 2, Test)));

        Breakpoints.Clear();
    }

    [Fact]
    public void AMarkFromBeforeTestsWereNamedIsDropped()
    {
        // A version-one settings file. There is no way to work out which test each of those belonged to,
        // and keeping them would mean applying them to every test — the bug — or to none while still
        // counting them on the settings page.
        Breakpoints.Clear();
        Breakpoints.Restore([new BreakpointMark { Stage = "Act", StepId = 2 }]);

        Assert.Empty(Breakpoints.Snapshot());
        Assert.False(Breakpoints.ShouldPause(Ask("session-1", "Act", 2, Test)));
    }

    [Fact]
    public void NothingIsHeldWithoutARequest()
    {
        Breakpoints.Clear();

        Assert.False(Breakpoints.ShouldPause(null!));
        Assert.False(Breakpoints.ShouldPause(new BreakpointQuestion { Request = null! }));
    }

    private static BreakpointQuestion Ask(string sessionId, string stage, int stepId, string? test = Test)
        => new()
        {
            Request = new PipeBreakpointHitRequestSignal { SessionId = sessionId, Stage = stage, StepId = stepId },
            Test = test
        };
}
