using System;
using System.Collections.Immutable;
using Axiom.State.Selectors;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// The ways the UI asks about runs.
/// </summary>
/// <remarks>
/// Every lane here replaced a lambda that was written out at each call site. "The selected run" was
/// spelled five different times across the board, the run bar and the summary panel — each one a
/// <c>Find</c> over the list against <see cref="RunsState.SelectedSessionId"/>, and each one free to
/// disagree with the others about a run that is not there.
/// </remarks>
public static class RunsSelectors
{
    /// <summary>The catalogue.</summary>
    public static readonly Selector<MainState, ImmutableList<RunSummary>> SelectAll =
        MainSelectors.SelectRuns.Then(Selector.Property((RunsState runs) => runs.All));

    /// <summary>The session being looked at, or null when none is.</summary>
    public static readonly Selector<MainState, string?> SelectSelectedSessionId =
        MainSelectors.SelectRuns.Then(Selector.Property((RunsState runs) => runs.SelectedSessionId));

    /// <summary>The run being looked at, or null when none is or the selection names a run that is gone.</summary>
    public static readonly Selector<MainState, RunSummary?> SelectSelectedRun =
        ReadOnlySelector.Of<RunSummary?>(nameof(SelectSelectedRun), SelectedRunOf);

    /// <summary>
    /// The test the selected run is an execution of, or empty when nothing is selected.
    /// </summary>
    /// <remarks>
    /// Its own lane because a breakpoint belongs to a test rather than to a session, so this is what
    /// the board tells <c>Breakpoints</c> it is looking at.
    /// </remarks>
    public static readonly Selector<MainState, string> SelectSelectedTest =
        ReadOnlySelector.Of(nameof(SelectSelectedTest), SelectedTestOf);

    /// <summary>Whether the selected run carries enough identity to be repeated.</summary>
    public static readonly Selector<MainState, bool> SelectCanRerunSelected =
        ReadOnlySelector.Of(nameof(SelectCanRerunSelected), static state => RerunCommand.IsAvailableFor(SelectedRunOf(state)));

    /// <summary>Whether anything is selected at all.</summary>
    public static readonly Selector<MainState, bool> SelectHasSelection =
        ReadOnlySelector.Of(nameof(SelectHasSelection), static state => state.Runs.SelectedSessionId is not null);

    /// <summary>One named run, or null when the catalogue does not hold it.</summary>
    public static Selector<MainState, RunSummary?> SelectBySession(string? sessionId)
        => ReadOnlySelector.Of<RunSummary?>(nameof(SelectBySession), state => RunOf(state, sessionId));

    /// <summary>Whether one named run is the selected one.</summary>
    public static Selector<MainState, bool> SelectIsSelected(string? sessionId)
        => ReadOnlySelector.Of(nameof(SelectIsSelected), state => sessionId is not null
            && string.Equals(state.Runs.SelectedSessionId, sessionId, StringComparison.Ordinal));

    /// <summary>
    /// Looks a run up by session.
    /// </summary>
    /// <remarks>
    /// Public, and the same method the lane above is built from, because the store can be read two
    /// ways: <c>Bind</c> takes a selector, and <c>GetValue</c> takes a plain function and has no
    /// selector overload. One implementation behind both entry points is what stops a one-off read
    /// answering differently from the binding beside it.
    /// <para>
    /// Ordinal, and stated once: a session identifier is an opaque token, and comparing two of them
    /// by culture would make the match depend on where the machine happens to be.
    /// </para>
    /// </remarks>
    public static RunSummary? RunOf(MainState state, string? sessionId)
        => sessionId is null
            ? null
            : state.Runs.All.Find(run => string.Equals(run.SessionId, sessionId, StringComparison.Ordinal));

    /// <summary>The run being looked at, or null when nothing is or the selection is stale.</summary>
    public static RunSummary? SelectedRunOf(MainState state) => RunOf(state, state.Runs.SelectedSessionId);

    /// <summary>The test the selected run is an execution of, or empty when nothing is selected.</summary>
    public static string SelectedTestOf(MainState state) => SelectedRunOf(state)?.Test ?? string.Empty;
}
