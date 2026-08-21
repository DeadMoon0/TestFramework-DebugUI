using System.Collections.Immutable;
using Axiom.State.Actions;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// The actions that change what runs the UI knows about.
/// </summary>
public static class RunsActions
{
    /// <summary>
    /// Adds runs recorded on disk to the picker.
    /// </summary>
    /// <remarks>
    /// Kept apart from ingesting: a listed run has been named but not opened, so it contributes a
    /// summary without any of the events behind it. Merging rather than replacing, because a live
    /// run and its own journal entry are the same run and must not appear twice.
    /// </remarks>
    public static readonly StateAction<ImmutableList<RunSummary>> AddRecorded =
        new(nameof(RunsActions), nameof(AddRecorded));
}
