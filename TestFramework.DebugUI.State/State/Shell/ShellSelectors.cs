using Axiom.State.Selectors;

namespace TestFramework.DebugUI.State.Shell;

/// <summary>
/// The ways the UI asks about the shell.
/// </summary>
public static class ShellSelectors
{
    /// <summary>How the UI is connected.</summary>
    public static readonly Selector<MainState, TransportStatus> SelectTransport =
        MainSelectors.SelectShell.Then(Selector.Property((ShellState shell) => shell.Transport));

    /// <summary>The test whose re-run is being waited for, or null when none is.</summary>
    public static readonly Selector<MainState, string?> SelectAwaitedRerun =
        MainSelectors.SelectShell.Then(Selector.Property((ShellState shell) => shell.AwaitedRerun));

    /// <summary>What the transport is, and how busy it is.</summary>
    public static readonly Selector<MainState, TransportDetails> SelectTransportDetails =
        MainSelectors.SelectShell.Then(Selector.Property((ShellState shell) => shell.Details));

    /// <summary>How many events arrived but could not be read.</summary>
    public static readonly Selector<MainState, int> SelectUnreadableEvents =
        MainSelectors.SelectShell.Then(Selector.Property((ShellState shell) => shell.UnreadableEvents));
}
