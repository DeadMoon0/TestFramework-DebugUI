using Axiom.State.Reducers;

namespace TestFramework.DebugUI.State.Shell;

/// <summary>
/// Applies the transitions that change the shell.
/// </summary>
public sealed class ShellReducer : Reducer<MainState>
{
    /// <summary>
    /// Initializes the reducer and registers its handlers.
    /// </summary>
    public ShellReducer()
    {
        Scope(MainSelectors.SelectShell)
            .On(ShellActions.SetTransportStatus, (state, status) => state with { Transport = status })
            .On(ShellActions.AwaitRerun, (state, test) => state with { AwaitedRerun = test });
    }
}
