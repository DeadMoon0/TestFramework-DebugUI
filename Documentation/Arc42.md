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
- The current transport is usable for active sessions, but the durable broker design is still future work.
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

- [PipeAdapterFlow.md](./PipeAdapterFlow.md): current signal flow and reducer behavior
- [ReliableDebugTransportPlan.md](./ReliableDebugTransportPlan.md): future transport redesign plan

## 6. Runtime View

Typical runtime flow:

1. The test runtime emits debugger signals.
2. The pipe adapter receives and classifies those signals.
3. The reducer projects them into canonical run state.
4. The WPF UI queries and renders that state.
5. If a step pauses at a breakpoint, the UI issues a continue signal back through the adapter.

Completed runs remain visible while the current UI process stays alive. Cross-process durable replay is planned but not yet the default runtime contract.

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

## 9. Architecture Decisions

- Keep DebugUI as an inspection surface rather than extending it into a hidden execution runtime.
  Rationale: the framework design favors explicit boundaries between authored test behavior and supporting tooling.

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