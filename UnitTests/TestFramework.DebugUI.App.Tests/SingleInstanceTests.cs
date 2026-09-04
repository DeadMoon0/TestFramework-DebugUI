using System;
using System.Runtime.CompilerServices;
using System.Threading;
using TestFramework.DebugUI;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the election and the handover.
/// </summary>
/// <remarks>
/// Every test holds its own election, under a name of its own. Sharing the product's name made these
/// tests fail for a reason that had nothing to do with them - a developer with the tool open was
/// already the owner, so the suite could only be trusted with the tool closed - and made any two of
/// them each other's second instance, which is the very thing under test.
/// </remarks>
public class SingleInstanceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>An election of this test's own, which nothing else on the machine is holding.</summary>
    private static string Election([CallerMemberName] string caller = "")
        => $"TestFramework.DebugUI.Tests.{caller}.{Guid.NewGuid():N}";

    [Fact]
    public void TheFirstOneToAskIsTheOwner()
    {
        using SingleInstance first = SingleInstance.Acquire(Election());

        Assert.True(first.IsOwner);
    }

    [Fact]
    public void ASecondAskIsNotTheOwner()
    {
        // The whole point. Without this the second process takes the debug pipe away from the first and then
        // sits there looking as though it were listening.
        string election = Election();

        using SingleInstance first = SingleInstance.Acquire(election);
        using SingleInstance second = SingleInstance.Acquire(election);

        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public void OwnershipIsGivenUpWhenTheOwnerGoes()
    {
        string election = Election();

        using (SingleInstance first = SingleInstance.Acquire(election))
            Assert.True(first.IsOwner);

        using SingleInstance next = SingleInstance.Acquire(election);

        Assert.True(next.IsOwner);
    }

    [Fact]
    public void AnArgumentReachesTheOwner()
    {
        string election = Election();

        using SingleInstance owner = SingleInstance.Acquire(election);

        Assert.True(owner.IsOwner);

        using ManualResetEventSlim arrived = new(false);
        string? received = null;

        owner.Activated += payload => { received = payload; arrived.Set(); };
        owner.Listen();

        Assert.True(SingleInstance.TrySend(@"C:\somewhere\shared.tfrun", Patience, election), "the owner should have answered");
        Assert.True(arrived.Wait(Patience), "the argument should have arrived");

        Assert.Equal(@"C:\somewhere\shared.tfrun", received);
    }

    [Fact]
    public void ALaunchWithNothingToSayStillArrives()
    {
        // Starting the tool while it is already running is a request to be shown the window, not a mistake, so
        // an empty handover has to be delivered rather than dropped.
        string election = Election();

        using SingleInstance owner = SingleInstance.Acquire(election);
        using ManualResetEventSlim arrived = new(false);

        string? received = "not set";

        owner.Activated += payload => { received = payload; arrived.Set(); };
        owner.Listen();

        Assert.True(SingleInstance.TrySend(null, Patience, election));
        Assert.True(arrived.Wait(Patience), "an empty handover should still arrive");

        Assert.Null(received);
    }

    [Fact]
    public void SeveralHandoversInARowAllArrive()
    {
        // The listener has to survive its first connection. A loop that ended after one would leave every later
        // double-click doing nothing at all for the rest of the session.
        string election = Election();

        using SingleInstance owner = SingleInstance.Acquire(election);
        using CountdownEvent counted = new(3);

        owner.Activated += _ => counted.Signal();
        owner.Listen();

        for (int index = 0; index < 3; index++)
            Assert.True(SingleInstance.TrySend($"file-{index}.tfrun", Patience, election), $"handover {index} was not answered");

        Assert.True(counted.Wait(Patience), $"only {3 - counted.CurrentCount} of 3 arrived");
    }

    [Fact]
    public void SendingWithNobodyListeningFails()
    {
        // Which is what tells a second launch to carry on and start normally, rather than exiting and leaving
        // the reader with no window at all.
        Assert.False(SingleInstance.TrySend("x.tfrun", TimeSpan.FromMilliseconds(300), Election()));
    }
}
