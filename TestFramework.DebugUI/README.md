# TestFramework.DebugUI

`TestFramework.DebugUI` is the desktop WPF application that visualizes TestFramework timeline runs.

Use it when you want a debugger-oriented view of execution instead of reading only console or test-runner output.

## What It Does

The application connects to the built-in debugger transport and projects a run into a structured inspection model:

- `Run -> Stage -> Layer -> Step -> Attempt`, drawn as a board rather than a list
- variables and artifacts as structured values, compared against the last clean run of the same test
- widgets - the files a step recorded to be looked at, such as a screenshot of the page it was on
- logs and assertions grouped by execution context
- breakpoint wait state with an explicit continue action, and a way to ask the paused run for a fresh look

## Quickstart

1. Start the DebugUI app.
2. Run your timeline or test suite with the normal built-in debugger path.
3. Pick the run and inspect it on the board; a step's panel holds its values, log, attempts and widgets.
4. Use the breakpoint continue action when a step pauses.

## When To Use It

- use it during scenario debugging when you need to see step ordering and run state clearly
- use it when artifact or variable changes matter more than raw log text
- use it when breakpoint-driven inspection is easier than adding temporary assertions or console output

## Connection Notes

- default pipe name: `TestFrameworkDebug_79d7aa2d-da07-4c84-b1f2-0639b0009290`
- override: set `TESTFRAMEWORK_DEBUG_PIPE_NAME` on both producer and UI sides when isolation is needed

## Troubleshooting

- If no run appears, verify the test process and the app use the same pipe name.
- If the UI connects but no useful state appears, check [../Documentation/ERROR-HANDLING-DEBUGUI.md](../Documentation/ERROR-HANDLING-DEBUGUI.md).
- If the issue is transport behaviour rather than a usage error, read [../Documentation/TransportAndProjection.md](../Documentation/TransportAndProjection.md).

## Read Next

- architecture: [../Documentation/Arc42.md](../Documentation/Arc42.md)
- error handling and recovery: [../Documentation/ERROR-HANDLING-DEBUGUI.md](../Documentation/ERROR-HANDLING-DEBUGUI.md)
- root module overview: [../README.md](../README.md)