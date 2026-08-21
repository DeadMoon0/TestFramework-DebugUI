# TestFramework-DebugUI - arc42 Architecture Documentation

> Date: 2026-06-29

## 1. Introduction and Goals

TestFramework-DebugUI is the desktop inspection surface for TestFramework timeline runs.

Its job is not to execute tests. Its job is to make an already-running or recently-completed timeline understandable as a structured execution flow.

Primary goals:

- visualize a timeline run as ordered stages, layers, steps, and attempts
- expose variables, artifacts, assertions, and logs as structured state
- support breakpoint-driven inspection without forcing users to read transport internals
- keep the debugging workflow explicit instead of hiding it behind test-side magic

## 2. Constraints

- Runtime target is .NET 8 on Windows with WPF.
- The application depends on the TestFramework debugger signal model emitted by Core.
- The current transport path is named-pipe based.
- Runs are durable: a completed run is journalled as NDJSON and can be reopened after the test host exits. The separate-broker design was retired rather than built - see [TransportAndProjection.md](./TransportAndProjection.md).
- The UI must not require extra test-side setup beyond the built-in debugger path and optional pipe-name override.

## 3. System Scope and Context

Relevant collaborators:

- `TestFramework.Core`: emits debugger signals during timeline execution
- `TestFramework.DebugUI.PipeAdapter`: hosts the current pipe-facing adapter and signal routing loop
- `TestFramework.DebugUI.Data`: projects signals into the canonical run state graph
- WPF presentation layer: renders the projected state and exposes user actions such as continuing a breakpoint
- test authors: start the UI, run timelines, and inspect results

The DebugUI sits on the consumer side of debugging. It is not the run engine, and it should not become the hidden owner of test behavior.

## 4. Solution Strategy

The current solution strategy is:

- keep the execution-facing debugger semantics in Core
- accept signals through a dedicated adapter instead of mixing transport code into the WPF window directly
- project protocol messages into a canonical state tree before rendering anything
- keep user-facing docs separate from internal transport plans so ordinary consumers are not forced to learn the transport first

## 5. Building Block View

Main building blocks:

- `TestFramework.DebugUI`: WPF shell, windowing, and user interaction
- `TestFramework.DebugUI.PipeAdapter`: current named-pipe host and signal dispatch layer
- `TestFramework.DebugUI.Data`: reducer and state-query layer for canonical run state
- `WpfStateService`: UI-facing state container

Important internal documents:

- [TransportAndProjection.md](./TransportAndProjection.md): current signal flow, projection rules, and what the retired reliability plan turned into

## 6. Runtime View

Typical runtime flow:

1. The test runtime emits debugger signals.
2. The pipe adapter receives and classifies those signals.
3. The reducer projects them into canonical run state.
4. The WPF UI queries and renders that state.
5. If a step pauses at a breakpoint, the UI issues a continue signal back through the adapter.

Completed runs are journalled to disk and replayed from there, so they survive both the test host and the UI process exiting. Journalling is armed by its marker directory existing, which the launcher creates.

## 7. Deployment View

The current deployment shape is simple:

- one desktop WPF application
- one adapter library for current transport integration
- one data/state projection library

The app runs locally on the developer machine and connects to test processes through the named-pipe transport.

## 8. Cross-Cutting Concepts

- Canonical run state: the UI should reason about `Run -> Stage -> Layer -> Step -> Attempt`, not about raw message ordering.
- Explicit breakpoint workflow: pausing and resuming should stay visible to the user.
- Diagnostics over magic: transport failures should point users toward recovery steps, not just disappear into silent no-op behavior.
- User docs vs internal docs: onboarding and recovery guidance should stay separate from protocol redesign planning.

### State is a tree of slices

`MainState` holds no fields of its own. It composes three `record struct` slices, and each slice has
its actions, reducer, selectors and any effects in the folder beside it:

```
State/                        MainState, MainActions, MainReducer, MainSelectors,
                              MainStore, ReadOnlySelector, RunIngestService
  Runs/       RunsState       (All, SelectedSessionId)
                              + RunSummary, RunHealth, SourceLocation, RunProgress,
                                RerunCommand, ProjectResolution, RunTree, RunBatches
  Board/      BoardState      (ActiveRun, SelectedStep)
                              + RunGraph, RunProjection*, RunTally, ValueDescription,
                                ValueInspection, RunSearch, SearchQuery, SearchPattern
    Comparison/ ComparisonState (Values, Timing)
                              + RunBaseline, TimingComparison, ValueComparison, TextDiff
  Shell/      ShellState      (Transport, AwaitedRerun)
                              + TransportStatus
    Feed/     FeedState       (Entries, UnreadCount)
                              + FeedEntry, FeedSeverity, FeedSource
```

A folder holds its slice and the types that slice owns. A file's namespace matches its folder, so the
`using` list of any consumer states which parts of the state it actually reaches into — the run bar
reads `Runs` and `Board`, the feed panel reads only `Shell.Feed`. The non-slice files are placed by
what they operate on rather than by what they are: `RunSearch` sits under `Board/` because it searches
the graph and its comparison, `RunTree` under `Runs/` because it groups summaries, and `TextDiff`
under `Board/Comparison/` because a value's before-and-after is the only thing that asks for it.

Four rules hold this together, and each of them is a property of Axiom rather than a preference:

- **One reducer per action.** The store maps an action to exactly one handler and throws when two
  reducers claim the same one. So a slice-local transition lives in its slice, and the transitions
  that write more than one slice — ingesting an event, changing the selection, asking for a re-run —
  are owned by `MainReducer`. Effects are the opposite: many may watch one action.
- **Slices are never `readonly record struct`.** Axiom compiles a field copier per struct type, but an
  init-only field cannot be assigned from an expression tree, so a readonly slice falls back to boxed
  reflection on every clone. The compiled copier is cached per type, which is what makes the tree deep
  without making the clone expensive.
- **Bulk stays behind references.** Slices hold scalars and references to immutable classes;
  `RunGraph` and everything under it is never nested into a value type. `StateCloneBudgetTests` is
  what notices if that changes, because the symptom in the app is only a UI that slows as runs grow.
- **Selectors are the transport lanes.** Every binding in the UI goes through a named selector rather
  than an inline lambda. It matters most for the derived ones: "the selected run" and "is a step held
  at a breakpoint" were each open-coded at several call sites, and the second one decides whether the
  window comes to the front.

A lane that is derived rather than stored has no state to write back, so its setter throws instead of
discarding the write — scoping a reducer to one fails at the first dispatch rather than losing every
update through it. Lanes the store must also read through `GetValue`, which takes a function and has
no selector overload, expose the getter the selector is built from so both cannot disagree.

`MainStore` lists the reducers once, so the window and the tests build the same store. Its effect
arguments are optional: a test checking a transition passes none and gets a store that reduces and
nothing more.

## 9. Architecture Decisions

- Keep DebugUI as an inspection surface rather than extending it into a hidden execution runtime.
  Rationale: the framework design favors explicit boundaries between authored test behavior and supporting tooling.

- Move action-triggered async work into effects, and leave transport in the controller.
  Rationale: the comparison against an earlier run was a bare `Task.Run` inside `ShellController` that
  read the store, computed, and dispatched its own answer. As `ComparisonEffects` the request is one
  action and the answer another, both visible to anything watching the store. The staleness guard moved
  with it: the result carries the session it was computed for and the reducer drops it if the selection
  has moved, which closes the window that existed between checking and dispatching. What stayed behind
  is what is genuinely transport — pipe lifecycle, per-session event retention, breakpoint replies, and
  listing recorded runs, which callers expect to have happened by the time the call returns.

- Keep the current pipe adapter documented as current-state architecture.
  Rationale: users need to understand what exists today before future broker work lands.

- Treat the broker transport plan as future architecture, not present guarantee.
  Rationale: the plan is valuable, but it should not be mistaken for shipped durability behavior.

## 10. Quality Requirements

- Readability: a failed or paused run should be understandable without reading raw transport logs first.
- Explicitness: breakpoint, transport, and connection state should stay visible.
- Stability: active-session inspection should remain dependable for normal debugging sessions.
- Diagnosability: when transport or projection fails, the docs should tell a user what to verify next.

## 11. Risks and Technical Debt

- Transport durability is still weaker than the future broker design intends.
- The current WPF surface remains Windows-only.
- Late-attach and replay expectations can be misunderstood if readers treat the redesign plan as already implemented.
- Temporary WPF build artifacts in the repo tree still create noise and should be cleaned separately from this documentation pass.

## 12. Glossary

- Run: one executed timeline instance
- Stage: a named execution group in the timeline
- Layer: a dependency-ready step batch inside a stage
- Attempt: one execution try for a step, including retries
- Pipe Adapter: the current named-pipe host and signal dispatch layer used by DebugUI