# TestFramework-DebugUI

TestFramework-DebugUI is the desktop debugger frontend for TestFramework timeline runs.

It connects to the built-in named-pipe debugger transport, mirrors the live run into a canonical state tree, and lets you inspect execution at these levels:

- `Run -> Stage -> Layer -> Step -> Attempt`
- variables and artifacts as debugger-friendly JSON envelopes
- structured logs and assertions grouped by execution attempt
- breakpoint waits with an explicit Continue action in the window chrome

## How It Connects

The core runtime always includes the built-in pipe debugger unless another debugger of the same type is already registered.

- default pipe name: `TestFrameworkDebug_79d7aa2d-da07-4c84-b1f2-0639b0009290`
- override via environment variable: `TESTFRAMEWORK_DEBUG_PIPE_NAME`

The UI listens on that pipe, replays persisted session history on startup, and then continues with live updates as new signals arrive.

## State Model

The DebugUI data layer exposes a canonical state tree of `Run -> Stage -> Layer -> Step -> Attempt` plus query helpers for:

- ordered stage, layer, step, and attempt traversal
- latest-attempt summaries
- aggregated debug output and latest-log views
- breakpoint-aware step inspection

## Breakpoints

When a timeline step hits a debugger breakpoint, the runtime now pauses until the UI sends a continue signal back through the broker.

- the active paused step is surfaced in state via `IsWaitingAtBreakpoint`
- the main window enables `Continue Breakpoint` when a step is waiting
- continuing resumes the most recently paused step in the active run
