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
}
