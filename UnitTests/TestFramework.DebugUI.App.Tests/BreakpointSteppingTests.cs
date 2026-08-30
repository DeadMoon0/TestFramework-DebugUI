using System;
using System.Threading.Tasks;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI;
using TestFramework.DebugUI.State.Settings;
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
/// Each test builds its own <see cref="BreakpointService"/>. While these marks lived in a static, every
/// test here had to clear it first and the whole class had to run in a collection of its own.
/// </remarks>
public class BreakpointSteppingTests
{
    private const string Test = "Acme.Orders.Tests.OrderTests.PlacesAnOrder";

    [Fact]
    public void AnArmedRunStopsAtItsNextStep()
    {
        BreakpointService breakpoints = new();
        breakpoints.ArmNextStep("session-1");

        Assert.True(breakpoints.ShouldPause(Ask("session-1", "Main", 7)));
    }

    [Fact]
    public void TheStepIsSpentOnce()
    {
        // The whole difference between a step and a mode. Left armed, the run would stop at every
        // remaining step and there would be no way to let it finish.
        BreakpointService breakpoints = new();
        breakpoints.ArmNextStep("session-1");

        Assert.True(breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
        Assert.False(breakpoints.ShouldPause(Ask("session-1", "Main", 2)));
    }

    [Fact]
    public void AnotherRunCannotSpendIt()
    {
        // Several runs can be attached at once. A bare flag would be taken by whichever reached a step
        // first, which would stop a run nobody was looking at and let the stepped one go.
        BreakpointService breakpoints = new();
        breakpoints.ArmNextStep("session-1");

        Assert.False(breakpoints.ShouldPause(Ask("session-2", "Main", 1)));
        Assert.True(breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
    }

    [Fact]
    public void AStepIsSpentEvenWhereABreakpointWouldHaveHeldAnyway()
    {
        // Otherwise the run stops twice in the same place: once for the step, once for the mark still
        // pending, which reads as step-forward having done nothing at all.
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Main", 1);
        breakpoints.ArmNextStep("session-1");

        Assert.True(breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
        Assert.False(breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public async Task SteppingArmsTheStopBeforeItLetsTheRunGo()
    {
        // The order is the whole correctness of stepping: a run let go first can reach its next step and
        // ask about it before this side has said anything, and it sails past to the end.
        BreakpointService breakpoints = new();

        bool armedWhenReleased = false;

        await breakpoints.StepThroughAsync("session-1", () =>
        {
            armedWhenReleased = breakpoints.IsStepping("session-1");
            return Task.FromResult(true);
        });

        Assert.True(armedWhenReleased, "the run was released before the stop was armed");
    }

    [Fact]
    public async Task AReleaseThatDidNotHappenWithdrawsTheArming()
    {
        // Otherwise a run that was never let go stops unbidden at the next step it ever takes.
        BreakpointService breakpoints = new();

        bool released = await breakpoints.StepThroughAsync("session-1", () => Task.FromResult(false));

        Assert.False(released);
        Assert.False(breakpoints.IsStepping("session-1"));
        Assert.False(breakpoints.ShouldPause(Ask("session-1", "Main", 1)));
    }

    [Fact]
    public async Task AReleaseThatThrewWithdrawsTheArming()
    {
        // The transport reports a lost run by returning false, but nothing about this method should
        // depend on that staying the only way a release can fail.
        BreakpointService breakpoints = new();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => breakpoints.StepThroughAsync("session-1", () => throw new InvalidOperationException("the pipe died")));

        Assert.False(breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public void ClearingTakesThePendingStepWithIt()
    {
        BreakpointService breakpoints = new();
        breakpoints.ArmNextStep("session-1");
        breakpoints.Clear();

        Assert.False(breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public void RestoringSavedBreakpointsTakesThePendingStepWithIt()
    {
        // Restore runs at start-up. A step left over from a previous session would hold the first run of
        // the next one at a step nobody had asked about.
        BreakpointService breakpoints = new();
        breakpoints.ArmNextStep("session-1");
        breakpoints.Restore([]);

        Assert.False(breakpoints.IsStepping("session-1"));
    }

    [Fact]
    public void AMarkedStepStillHoldsWithNoStepArmed()
    {
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Main", 4);

        Assert.True(breakpoints.ShouldPause(Ask("session-1", "Main", 4)));
        Assert.False(breakpoints.ShouldPause(Ask("session-1", "Main", 5)));
    }

    [Fact]
    public void TogglingTwiceLeavesNoMark()
    {
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);

        Assert.True(breakpoints.Toggle("Act", 2));
        Assert.False(breakpoints.Toggle("Act", 2));

        Assert.False(breakpoints.IsSet("Act", 2));
        Assert.Empty(breakpoints.Snapshot());
    }

    [Fact]
    public void AMarkSetOnOneTestDoesNotStopAnother()
    {
        // The bug this key exists to fix. Stage names are conventional — Arrange, Act, Assert — so a mark
        // on step 2 of Act used to stop step 2 of Act in every other test that ran afterwards, and it was
        // saved to disk, so it did it again the next day.
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Act", 2);

        Assert.True(breakpoints.ShouldPause(Ask("session-1", "Act", 2, Test)));
        Assert.False(breakpoints.ShouldPause(Ask("session-2", "Act", 2, "Acme.Billing.Tests.InvoiceTests.Sends")));
    }

    [Fact]
    public void AMarkAppliesToTheSameTestRunAgain()
    {
        // Which is the property that makes a breakpoint worth keeping at all: you stop the run, fix the
        // code, run it again, and the mark is still where you put it — under a new session id.
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Act", 2);

        Assert.True(breakpoints.ShouldPause(Ask("a-later-session", "Act", 2, Test)));
    }

    [Fact]
    public void ARunThatNamedNoTestIsNeverMatchedAgainstMarks()
    {
        // Treating "unknown" as a name would put every unidentifiable run in one bucket, which is the
        // collision again with a different spelling.
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Act", 2);

        Assert.False(breakpoints.ShouldPause(Ask("session-1", "Act", 2, test: null)));
    }

    [Fact]
    public void ATestThatCannotBeNamedCannotBeMarked()
    {
        // Rather than filing the mark under an empty name, where it would apply to every other run this
        // window could not name.
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(null);

        Assert.False(breakpoints.Toggle("Act", 2));
        Assert.False(breakpoints.IsSet("Act", 2));
        Assert.Empty(breakpoints.Snapshot());
    }

    [Fact]
    public void MarksAreReadAgainstTheTestOnScreen()
    {
        // The board asks whether to light a marker without naming a test, so what it gets back has to be
        // the marks of the run it is showing and not of every run.
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Act", 2);

        Assert.True(breakpoints.IsSet("Act", 2));

        breakpoints.NowLookingAt("Acme.Billing.Tests.InvoiceTests.Sends");
        Assert.False(breakpoints.IsSet("Act", 2));
    }

    [Fact]
    public void ASavedMarkSurvivesARoundTripThroughTheSettingsFile()
    {
        BreakpointService breakpoints = new();
        breakpoints.NowLookingAt(Test);
        breakpoints.Toggle("Act", 2);

        BreakpointMark saved = Assert.Single(breakpoints.Snapshot());
        Assert.Equal(Test, saved.Test);

        BreakpointService restored = new();
        restored.Restore([saved]);

        Assert.True(restored.ShouldPause(Ask("session-9", "Act", 2, Test)));
    }

    [Fact]
    public void AMarkFromBeforeTestsWereNamedIsDropped()
    {
        // A version-one settings file. There is no way to work out which test each of those belonged to,
        // and keeping them would mean applying them to every test — the bug — or to none while still
        // counting them on the settings page.
        BreakpointService breakpoints = new();
        breakpoints.Restore([new BreakpointMark { Stage = "Act", StepId = 2 }]);

        Assert.Empty(breakpoints.Snapshot());
        Assert.False(breakpoints.ShouldPause(Ask("session-1", "Act", 2, Test)));
    }

    [Fact]
    public void NothingIsHeldWithoutARequest()
    {
        BreakpointService breakpoints = new();

        Assert.False(breakpoints.ShouldPause(null!));
        Assert.False(breakpoints.ShouldPause(new BreakpointQuestion { Request = null! }));
    }

    private static BreakpointQuestion Ask(string sessionId, string stage, int stepId, string? test = Test)
        => new()
        {
            Request = new PipeBreakpointHitRequestSignal { SessionId = sessionId, Stage = stage, StepId = stepId },
            Test = test
        };
}
