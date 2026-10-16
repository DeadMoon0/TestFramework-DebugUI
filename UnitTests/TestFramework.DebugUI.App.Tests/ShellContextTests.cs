using System;
using Axiom.State;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers what a view is built with.
/// </summary>
/// <remarks>
/// A view takes the store it shows as a constructor argument precisely so it cannot exist without one.
/// A context that let a missing store through would move that failure from the constructor to the first
/// binding, which is somewhere in the middle of a run being watched.
/// </remarks>
public sealed class ShellContextTests
{
    [Fact]
    public void AViewCannotBeGivenNoStoreToShow()
    {
        using StateStore<MainState> store = MainStore.Create().Build();
        IShellCommands commands = Shells.For(store).Commands;

        Assert.Throws<ArgumentNullException>(() => new ShellContext(null!, commands));
    }

    [Fact]
    public void AViewCannotBeGivenNoShellToAsk()
    {
        using StateStore<MainState> store = MainStore.Create().Build();

        Assert.Throws<ArgumentNullException>(() => new ShellContext(store, null!));
    }
}
