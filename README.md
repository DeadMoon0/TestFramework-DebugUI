# TestFramework-DebugUI

TestFramework-DebugUI is the desktop inspection surface for TestFramework timeline runs.

Use it when a normal test log is no longer enough and you need to inspect execution as a structured run tree instead of as raw text.

The DebugUI focuses on these consumer workflows:

- inspect a run as `Run -> Stage -> Layer -> Step -> Attempt`
- review variables, artifacts, logs, and assertions without parsing raw output manually
- look at what a step recorded to be looked at - screenshots, documents, logs - on the step and on the board
- pause at breakpoints and continue intentionally, and ask a paused run for a fresh look
- reopen a recorded run later, after both the test host and the UI have exited

## Install

Download `TestFramework.DebugUI.Setup.exe` from the [latest release](https://github.com/DeadMoon0/TestFramework-DebugUI/releases) and run it. It installs per-user, needs no administrator rights, and offers the .NET 8 desktop runtime if it is missing. Windows will warn that the file is unsigned - **More info -> Run anyway**; there is no certificate behind this project.

What it installs is a small launcher, which fetches and starts the application itself and keeps the last three versions around. Installing it is also what switches on run journalling: Core records a run only when `%USERPROFILE%\.testframework\Debug` exists, and creating that folder is the launcher's first job.

Full detail - assets, folder layout, updating, rolling back, cutting a release - is in [Documentation/INSTALLING.md](./Documentation/INSTALLING.md).

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

`TestFramework.DebugUI.State` holds a canonical state tree plus queries for:

- ordered stage, layer, step, and attempt traversal
- latest-attempt summaries
- aggregated debug output and latest-log views
- assertion history
- breakpoint-aware step inspection
- the run's widgets, and the comparison of this run's values against the last clean run of the same test

## Appearance

Twelve themes ship as six light/dark pairs - Slate, Ember, Tide, Glass, Contrast and Origin - and the picker is in Settings. `slate-dark` is the default.

You can also write your own: a theme file is a built-in with some things changed, so it lists only what it replaces. **Open themes folder** in Settings writes a commented example into `%USERPROFILE%\.testframework\DebugUI	hemes` and opens it.

One thing worth knowing before you reach for the see-through themes: if energy saver is on, or transparency effects are off in Windows, the compositor will not blur and such a theme is dimmed in the picker with the reason in its tooltip. Energy saver is the usual culprit, and it takes the effect away without appearing to change a setting.

See [Documentation/THEMING.md](./Documentation/THEMING.md) for the format, the colour keys, the backdrop recipes and the blur rules.

## Known Limitations

DebugUI is stable both for watching a live run and for reopening a recorded one. What is genuinely limited:

- **Attachment is decided once, at the run's start.** A UI started midway through a run does not join it; that run appears in the list when it finishes and is read from its journal. Every later run in the same suite is picked up, because the check is per run rather than once per process.
- **Journalling is armed by a directory the launcher creates.** A machine where the tool has never been installed records nothing, and nothing says so at run time.
- **Widget files live beside the run, not inside the journal.** A journal moved without its output folder replays the run but cannot show its pictures.
- Malformed or partial transport messages are diagnosable, but not every failure mode has a recovery path that stays entirely inside the UI.

The separate-broker redesign once planned was retired: the journal provides the durability it was for.

## Troubleshooting

- If no run appears, verify the UI started before or during the test run and confirm both sides use the same `TESTFRAMEWORK_DEBUG_PIPE_NAME` value.
- If a breakpoint never resumes, confirm the active step is actually marked as waiting; with two steps of one run waiting at once, each can still be released, but a widget captured while both wait is filed against neither.
- If a run tree appears incomplete, check the DebugUI error guide before assuming the timeline itself is at fault.
- If you are diagnosing transport behaviour rather than a usage error, read [Documentation/TransportAndProjection.md](./Documentation/TransportAndProjection.md).

## Documentation Map

User-facing docs:

- [TestFramework.DebugUI/README.md](./TestFramework.DebugUI/README.md)
- [Documentation/INSTALLING.md](./Documentation/INSTALLING.md)
- [Documentation/THEMING.md](./Documentation/THEMING.md)
- [Documentation/Arc42.md](./Documentation/Arc42.md)
- [Documentation/ERROR-HANDLING-DEBUGUI.md](./Documentation/ERROR-HANDLING-DEBUGUI.md)

Internal transport and implementation docs:

- [Documentation/TransportAndProjection.md](./Documentation/TransportAndProjection.md)

## Breakpoints

When a timeline step hits a debugger breakpoint, the runtime pauses until the UI sends a continue signal.

- the active paused step is surfaced in state via `IsWaitingAtBreakpoint`
- the main window enables `Continue Breakpoint` when a step is waiting
- continuing resumes the most recently paused step in the active run
