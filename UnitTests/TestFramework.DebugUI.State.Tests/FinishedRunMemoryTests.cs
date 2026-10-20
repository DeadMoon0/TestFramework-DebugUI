using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// A run the window saw finish stays finished when something is added to it later - for the recent runs.
/// </summary>
public class FinishedRunMemoryTests
{
    [Fact]
    public void ARunThatFinishedIsRemembered()
    {
        FinishedRunMemory memory = new(capacity: 4);

        memory.Remember("run-1");

        Assert.True(memory.Finished("run-1"));
    }

    [Fact]
    public void ARunThatNeverFinishedIsNot()
    {
        FinishedRunMemory memory = new(capacity: 4);

        memory.Remember("run-1");

        Assert.False(memory.Finished("run-2"));
    }

    [Fact]
    public void BeyondItsCapacityTheOldestRunIsForgottenFirst()
    {
        FinishedRunMemory memory = new(capacity: 2);

        memory.Remember("run-1");
        memory.Remember("run-2");
        memory.Remember("run-3");

        Assert.False(memory.Finished("run-1"));
        Assert.True(memory.Finished("run-2"));
        Assert.True(memory.Finished("run-3"));
    }

    [Fact]
    public void ARunRememberedTwiceTakesOnePlace()
    {
        FinishedRunMemory memory = new(capacity: 2);

        memory.Remember("run-1");
        memory.Remember("run-1");
        memory.Remember("run-2");

        Assert.True(memory.Finished("run-1"));
        Assert.True(memory.Finished("run-2"));
    }
}
