using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Home;

/// <summary>
/// What each verdict looks like and is called.
/// </summary>
/// <remarks>
/// One table, because three surfaces show the same verdicts: the rail's health edge, a card's badge and
/// a row's edge. Written out separately they had already begun to differ — the rail called an aborted run
/// red without saying so while the card spelled it "ABORTED" — and a colour that means one thing in the
/// rail and another in the list is worse than no colour at all.
///
/// Amber for a run that passed without asserting anything: it did not fail, but it did not prove anything
/// either, and a wall of green for runs that checked nothing would be a comfortable lie.
/// </remarks>
internal static class HealthLook
{
    /// <summary>The resource key of the brush a verdict is drawn in.</summary>
    internal static string BrushKey(RunHealth health) => health switch
    {
        RunHealth.Running => "StateRunning",
        RunHealth.Waiting => "StatePaused",
        RunHealth.Passed => "StateComplete",
        RunHealth.Unproven => "StateTimeout",
        RunHealth.Failed => "StateError",
        RunHealth.Aborted => "StateError",
        _ => "StateNotRun"
    };

    /// <summary>The word a verdict is badged with.</summary>
    internal static string Label(RunHealth health) => health switch
    {
        RunHealth.Running => "RUNNING",
        RunHealth.Waiting => "PAUSED",
        RunHealth.Passed => "PASSED",
        RunHealth.Unproven => "UNPROVEN",
        RunHealth.Failed => "FAILED",
        RunHealth.Aborted => "ABORTED",
        _ => "NOT OPENED"
    };
}
