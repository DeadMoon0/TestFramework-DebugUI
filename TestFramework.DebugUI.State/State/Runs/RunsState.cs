using System.Collections.Immutable;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// Which runs the UI knows about, and which of them is being looked at.
/// </summary>
/// <remarks>
/// A slice rather than fields on <see cref="MainState"/>, because these two belong together and to
/// nothing else: the list is the catalogue, and the selection is an index into it. Splitting them
/// apart would let a reducer move the selection to a session the catalogue has never heard of.
/// <para>
/// Not a <c>readonly record struct</c>, and that is deliberate for every slice in this tree: Axiom
/// compiles a field copier per struct type, but an init-only field cannot be assigned from an
/// expression tree, so a readonly slice silently falls back to boxed reflection on every clone.
/// </para>
/// </remarks>
public record struct RunsState()
{
    /// <summary>Every run this UI knows about, live or replayed, newest first.</summary>
    public ImmutableList<RunSummary> All = ImmutableList<RunSummary>.Empty;

    /// <summary>The session currently rendered, or null when none is selected.</summary>
    public string? SelectedSessionId = null;
}
