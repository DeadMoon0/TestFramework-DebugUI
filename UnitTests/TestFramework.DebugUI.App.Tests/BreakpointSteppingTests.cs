using TestFramework.Core.Debugger;
using TestFramework.DebugUI;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the single step that step-forward arms.
/// </summary>
/// <remarks>
/// Worth testing on its own because the thing being decided is whether a run stops, and it is decided on
/// the transport's reader thread with no UI in the loop. Getting it wrong either strands a run that nobody
/// asked to hold, or stops the wrong one of several attached runs.
///
/// <see cref="Breakpoints"/> is static, so every test here clears it first rather than trusting the order
/// they run in.
/// </remarks>
[Collection("breakpoints")]
public class BreakpointSteppingTests
{
    [Fact]
    public void AnArmedRunStopsAtItsNextStep()
    {
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");

        Assert.True(Breakpoints.ShouldPause(Request("session-1", "Main", 7)));
    }

    [Fact]
    public void TheStepIsSpentOnce()
    {
        // The whole difference between a step and a mode. Left armed, the run would stop at every
        // remaining step and there would be no way to let it finish.
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");

        Assert.True(Breakpoints.ShouldPause(Request("session-1", "Main", 1)));
        Assert.False(Breakpoints.ShouldPause(Request("session-1", "Main", 2)));
    }

    [Fact]
    public void AnotherRunCannotSpendIt()
    {
        // Several runs can be attached at once. A bare flag would be taken by whichever reached a step
        // first, which would stop a run nobody was looking at and let the stepped one go.
        Breakpoints.Clear();
        Breakpoints.StepOnce("session-1");

        Assert.False(Breakpoints.ShouldPause(Request("session-2", "Main", 1)));
        Assert.True(Breakpoints.ShouldPause(Request("session-1", "Main", 1)));
    }

    [Fact]
    public void AStepIsSpentEvenWhereABreakpointWouldHaveHeldAnyway()
    {
        // Otherwise the run stops twice in the same place: once for the step, once for the mark still
        // pending, which reads as step-forward having done nothing at all.
        Breakpoints.Clear();
        Breakpoints.Toggle("Main", 1);
        Breakpoints.StepOnce("session-1");

        Assert.True(Breakpoints.ShouldPause(Request("session-1", "Main", 1)));
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

        Assert.False(Breakpoints.ShouldPause(Request("session-1", "Main", 1)));
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
        Breakpoints.Toggle("Main", 4);

        Assert.True(Breakpoints.ShouldPause(Request("session-1", "Main", 4)));
        Assert.False(Breakpoints.ShouldPause(Request("session-1", "Main", 5)));

        Breakpoints.Clear();
    }

    [Fact]
    public void NothingIsHeldWithoutARequest()
    {
        Breakpoints.Clear();

        Assert.False(Breakpoints.ShouldPause(null!));
    }

    private static PipeBreakpointHitRequestSignal Request(string sessionId, string stage, int stepId)
        => new() { SessionId = sessionId, Stage = stage, StepId = stepId };
}
