using System;
using Axiom.State.Selectors;

namespace TestFramework.DebugUI.State;

/// <summary>
/// Builds a selector that answers a question about the state but cannot write one back.
/// </summary>
/// <remarks>
/// Axiom's <see cref="Selector"/> is a two-way lane: a getter for binding and a setter so a reducer
/// can be scoped to it. Most of the lanes worth naming here are derived — "the selected run", "is a
/// step held at a breakpoint" — and there is no state to assign back through them. Rather than give
/// them a setter that quietly discards the write, they get one that says so, so scoping a reducer to
/// a derived lane fails at the first dispatch instead of losing every update through it.
/// </remarks>
internal static class ReadOnlySelector
{
    internal static Selector<MainState, T> Of<T>(string name, SelectorGetter<MainState, T> getter)
        => Selector.Custom(getter, (state, _) => throw new NotSupportedException(
            $"'{name}' is derived from the state and cannot be written back, so a reducer cannot be scoped to it."));
}
