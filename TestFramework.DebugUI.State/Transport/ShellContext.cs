using System;
using Axiom.State;

namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// What a view is built with: the store it shows and what it may ask the shell to do.
/// </summary>
/// <remarks>
/// <para>
/// Handed to every view through its constructor, so a view cannot exist without the store it reads.
/// The views used to reach for a process-wide default store instead, which carried the unwritten rule
/// "somebody made one the default by now" — true only because the window happened to build it before
/// any view, and a test had to make one default for the whole process and keep its runs apart by luck.
/// </para>
/// <para>
/// With the store handed in, a test builds a view against a store of its own and nothing it does can
/// reach another test's. The two travel together because every view that shows the state may also
/// need to act on it, and two arguments that always go together are one argument.
/// </para>
/// </remarks>
/// <param name="Store">The store the view binds to and reads from.</param>
/// <param name="Commands">What the view may ask the shell to do.</param>
public sealed record ShellContext(StateStore<MainState> Store, IShellCommands Commands)
{
    /// <summary>Gets the store the view binds to and reads from.</summary>
    public StateStore<MainState> Store { get; } = Store ?? throw new ArgumentNullException(nameof(Store));

    /// <summary>Gets what the view may ask the shell to do.</summary>
    public IShellCommands Commands { get; } = Commands ?? throw new ArgumentNullException(nameof(Commands));
}
