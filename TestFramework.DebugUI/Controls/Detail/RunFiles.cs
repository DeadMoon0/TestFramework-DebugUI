using System;
using Axiom.State;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;
using TestFramework.DebugUI.State.Bundles;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// Finds the file behind a value or a widget, wherever it is now.
/// </summary>
/// <remarks>
/// Two questions that always travel together: where the run recorded the file, and where the run
/// itself came from. A run imported from somebody else's machine records a path that does not exist
/// here, and its files sit beside its journal instead — so neither answer is any use without the
/// other, and every surface that shows a file needs both.
/// </remarks>
internal static class RunFiles
{
    /// <summary>
    /// The file a body refers to, or null when it cannot be found on this machine.
    /// </summary>
    /// <param name="body">What the run recorded about the file.</param>
    /// <param name="store">The store whose selected run the body belongs to, which says where that run came from.</param>
    public static string? Resolve(ValueBody? body, StateStore<MainState> store)
        => body is null ? null : ValueFiles.Resolve(body.Path, body.RelativePath, JournalPath(store));

    /// <summary>The journal the selected run was replayed from, when it came from disk.</summary>
    private static string? JournalPath(StateStore<MainState> store)
        => store
            .GetValue(state => state.Runs.All
                .Find(run => string.Equals(run.SessionId, state.Runs.SelectedSessionId, StringComparison.Ordinal))?.JournalPath);
}
