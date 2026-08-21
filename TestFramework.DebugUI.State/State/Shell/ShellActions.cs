using Axiom.State.Actions;

namespace TestFramework.DebugUI.State.Shell;

/// <summary>
/// The actions that change the shell.
/// </summary>
public static class ShellActions
{
    /// <summary>Records the transport's current status.</summary>
    public static readonly StateAction<TransportStatus> SetTransportStatus =
        new(nameof(ShellActions), nameof(SetTransportStatus));

    /// <summary>
    /// Records that a re-run of one test was asked for, so its run is shown when it arrives.
    /// </summary>
    /// <remarks>
    /// Carried as the test's name: the session the new run announces itself under is decided by the
    /// test host, so there is nothing else to match it by until it exists.
    /// <para>
    /// Dispatched on its own by whoever knows the test without going through the selection —
    /// <see cref="MainActions.RerunSelected"/> is the path that starts from the selected run.
    /// </para>
    /// </remarks>
    public static readonly StateAction<string> AwaitRerun = new(nameof(ShellActions), nameof(AwaitRerun));
}
