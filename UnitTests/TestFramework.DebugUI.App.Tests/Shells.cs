using System.Threading.Tasks;
using Axiom.State;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Shell.Feed;
using TestFramework.DebugUI.State.Transport;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// What a view under test is built with.
/// </summary>
/// <remarks>
/// A view is handed the store it shows and the shell it may ask things of, and a test supplies both. The
/// store is the test's own, so nothing one test dispatches can reach a view another test built. The shell
/// is a stand-in that does nothing: a view's own drawing is what these tests look at, and a shell that
/// went on to open runs or start test hosts would be testing something else.
/// </remarks>
internal static class Shells
{
    /// <summary>A context over the given store, with a shell that does nothing when asked.</summary>
    /// <param name="store">The store the view under test shows.</param>
    public static ShellContext For(StateStore<MainState> store) => new(store, new QuietShell());

    /// <summary>
    /// A shell that is never there.
    /// </summary>
    /// <remarks>
    /// Every request is accepted and answered as though nothing came of it, which is what a shell with
    /// no transport behind it would truthfully say.
    /// </remarks>
    private sealed class QuietShell : IShellCommands
    {
        public string? RunsDirectory => null;

        public void RefreshRecordedRuns()
        {
        }

        public void SelectRun(string sessionId)
        {
        }

        public void SelectStep(string stageName, int stepId)
        {
        }

        public Task<bool> ContinueSelectedRunAsync() => Task.FromResult(false);

        public Task<bool> CancelSelectedRunAsync(string? reason = null) => Task.FromResult(false);

        public Task<bool> CaptureWidgetsForSelectedRunAsync() => Task.FromResult(false);

        public void RerunSelected()
        {
        }

        public void Report(FeedEntry entry)
        {
        }
    }
}
