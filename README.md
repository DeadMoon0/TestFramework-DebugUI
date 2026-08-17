# TestFramework-DebugUI

TestFramework-DebugUI is the desktop inspection surface for TestFramework timeline runs.

Use it when a normal test log is no longer enough and you need to inspect execution as a structured run tree instead of as raw text.

The DebugUI focuses on these consumer workflows:

- inspect a run as `Run -> Stage -> Layer -> Step -> Attempt`
- review variables, artifacts, logs, and assertions without parsing raw output manually
- pause at breakpoints and continue intentionally
- keep completed runs visible in the UI process while later runs execute

## Start Here

Use this order if you are new to the debugging surface:

1. Read the package-level guide in [TestFramework.DebugUI/README.md](./TestFramework.DebugUI/README.md).
2. Read the architecture floor in [Documentation/Arc42.md](./Documentation/Arc42.md).
3. Keep [Documentation/ERROR-HANDLING-DEBUGUI.md](./Documentation/ERROR-HANDLING-DEBUGUI.md) open when you are diagnosing connection, transport, or breakpoint failures.

## Quickstart

The normal workflow is:

1. Start the DebugUI app.
2. Run a timeline with the built-in debugger output path enabled.
3. Inspect the active run tree and continue breakpoints from the window when needed.

The runtime already knows the default pipe name. You only need to override it when multiple debugger sessions must stay isolated.

## Connection Model

The core runtime includes the built-in pipe debugger unless another debugger of the same type is already registered.

- default pipe name: `TestFrameworkDebug_79d7aa2d-da07-4c84-b1f2-0639b0009290`
- override via environment variable: `TESTFRAMEWORK_DEBUG_PIPE_NAME`

The UI listens on that pipe, projects the incoming signals into canonical state, and can continue paused breakpoints back through the same transport path.

## What You Can Inspect

The DebugUI data layer exposes a canonical state tree plus queries for:

- ordered stage, layer, step, and attempt traversal
- latest-attempt summaries
- aggregated debug output and latest-log views
- assertion history
- breakpoint-aware step inspection

## Known Limitations

DebugUI is stable as an inspection surface for normal pipe-connected runs, but its transport story still has known limitations:

- transport durability is not yet broker-backed across independent UI restarts
- late-attach and replay behavior are more limited than the future broker plan describes
- malformed or partial transport messages are diagnosable, but not every failure mode has a recovery path that stays entirely inside the UI
- recorded runs are durable and reopen after the test host exits; the separate-broker redesign once planned was retired in favour of the journal

Treat the current implementation as stable for both live debugging sessions and reopening recorded runs.

## Troubleshooting

- If no run appears, verify the UI started before or during the test run and confirm both sides use the same `TESTFRAMEWORK_DEBUG_PIPE_NAME` value.
- If a breakpoint never resumes, confirm the active step is actually marked as waiting and that only one breakpoint is paused at a time.
- If a run tree appears incomplete, check the DebugUI error guide before assuming the timeline itself is at fault.
- If you are diagnosing transport behaviour rather than a usage error, read [Documentation/TransportAndProjection.md](./Documentation/TransportAndProjection.md).

## Documentation Map

User-facing docs:

- [TestFramework.DebugUI/README.md](./TestFramework.DebugUI/README.md)
- [Documentation/Arc42.md](./Documentation/Arc42.md)
- [Documentation/ERROR-HANDLING-DEBUGUI.md](./Documentation/ERROR-HANDLING-DEBUGUI.md)

Internal transport and implementation docs:

- [Documentation/TransportAndProjection.md](./Documentation/TransportAndProjection.md)

## Breakpoints

When a timeline step hits a debugger breakpoint, the runtime pauses until the UI sends a continue signal.

- the active paused step is surfaced in state via `IsWaitingAtBreakpoint`
- the main window enables `Continue Breakpoint` when a step is waiting
- continuing resumes the most recently paused step in the active run
