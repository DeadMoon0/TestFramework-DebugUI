# DebugUI Extension Error Handling

This document supplements Core's own error-handling material. Core documents its exception design
(`FriendlyMessage` / `RecoverySteps` / `AvailableOptions`) in its package README, and the producer
side of the debugger in `Documentation/RunDebuggerFlow.md` in the TestFramework-Core repository.
Deliberately named rather than linked: a relative path out of this repository 404s on GitHub.

Use it when the timeline itself may be fine, but the debugging surface is not behaving as expected.

## First Triage

Check these in order before assuming a deeper transport defect:

1. Is the DebugUI app running?
2. Does the test process use the same `TESTFRAMEWORK_DEBUG_PIPE_NAME` value as the UI?
3. Did the run actually include debugger output, or are you only expecting console formatting from `OutputRunDebugger`?
4. Is the issue an active-session problem, or a run that was never watched live and has to be reopened from its journal?

## Connection And Startup Failures

### Symptom: No run appears in the UI

Likely causes:

- the UI was not started when the run executed
- the UI and producer are listening on different pipe names
- the test run never emitted debugger signals on this process

Recovery:

- start the UI first, then re-run the test
- verify `TESTFRAMEWORK_DEBUG_PIPE_NAME` is either unset on both sides or set to the same value on both sides
- if you are only using console-oriented debug output helpers, remember that they are not the same thing as the DebugUI transport path

### Symptom: The UI connects but the state tree stays empty or partial

Likely causes:

- malformed or incomplete transport payloads
- a producer-side failure before initialization completed
- a projection problem in `PipeRunEventSource` or in the reducer

Recovery:

- rerun once with the UI already open
- confirm the run reaches debugger initialization at all
- if the problem looks like infrastructure rather than user setup, inspect [TransportAndProjection.md](./TransportAndProjection.md) to see which signal kind should have created the missing state

## Breakpoint Failures

### Symptom: A run pauses and never resumes

Likely causes:

- the step is waiting at a breakpoint and the user has not continued it
- the UI lost the active paused state due to a connection problem
- another breakpoint is already active and the host rejects concurrent paused-breakpoint ownership

Recovery:

- confirm the active step is marked as waiting at a breakpoint
- use the explicit continue action from the UI
- with two steps of one run waiting at once, the UI can still release each of them, but a widget captured while both wait cannot be filed against either - so reduce to one breakpoint when the evidence matters

### Symptom: Continue does nothing useful

Likely causes:

- the paused step is no longer the active waiting step
- the run and the UI got out of sync after a transport interruption

Recovery:

- rerun the scenario with the UI already open
- reduce the scenario to one breakpoint first
- if the issue repeats, treat it as debugger-infrastructure diagnosis rather than a test-code failure

## Transport Limitation Cases

### Symptom: A completed run is missing after restarting the UI

A finished run is journalled to disk and reopens after both the test host and the UI have exited, so a
run that is missing was most likely never recorded.

Likely causes:

- the journal's marker directory does not exist, which is what arms journalling - the launcher creates
  it, so a machine where the tool has never been installed records nothing
- the run's output folder was cleaned between the run and the attempt to reopen it

Recovery:

- check the marker directory before assuming the run was lost - see
  [TransportAndProjection.md](./TransportAndProjection.md)
- a run whose journal exists but whose output folder is gone still opens; its widget files do not, since
  they live beside the run rather than inside the journal

### Symptom: A run that was already going when the UI started never appeared live

Whether a run gets a pipe debugger at all is decided once, at the run's start, by asking whether a UI is
listening. A UI started midway through therefore does not join the run in progress - it is not a partial
attach, the run simply has no live consumer.

Recovery:

- start the UI first when you want to watch a specific run
- otherwise wait: the run is journalled, and it appears in the list once it has finished
- the probe is per run rather than once per process, so every *later* run in the same suite is picked up

### Symptom: The camera button recorded nothing

Asking a paused run for a fresh look is answered by whatever capture sources the run's packages
registered, and the acknowledgement says how many widgets were recorded and why none were.

Likely causes:

- the run's packages register no capture source, so there is nothing to ask
- the source refused: the UI pack will not photograph a browser session that is being held open for
  inspection by `TESTFRAMEWORK_UI_PAUSE_ON_FAILURE`, because somebody is already looking at that page
- the source threw, which is logged and swallowed rather than allowed to disturb the paused run

Recovery:

- read the reason in the feed - the acknowledgement carries it
- a source that threw says so in the run's own log rather than in the acknowledgement
- for the pause-on-failure case, release the held session first

A related case is a capture that *is* recorded but appears against no step: the widget is filed against
the one step that is waiting, and with two of them waiting there is no single right answer, so it is
filed with no step at all.

## Diagnostic Logging Guidance

When you are debugging the debugger itself, collect evidence from these layers in order:

1. test-side evidence that the run really executed the expected steps
2. whether the producer and UI agreed on the same pipe name
3. whether the missing behavior is in connection, signal delivery, or state projection
4. reducer/state behavior described in [TransportAndProjection.md](./TransportAndProjection.md)

Do not jump straight to the transport internals unless the user-facing checks above have already failed.

## Known Limitation Reminder

The DebugUI is stable for both active-session inspection and reopening recorded runs. The broker-backed transport once planned was retired: the journal provides the durability it was for. What remains genuinely limited is live attachment - a run decides at its start whether anyone is listening, and never revisits that. See [TransportAndProjection.md](./TransportAndProjection.md).

## See Also

- `Documentation/RunDebuggerFlow.md` in the TestFramework-Core repository (producer side of this transport)
- [Arc42.md](./Arc42.md)
- [TransportAndProjection.md](./TransportAndProjection.md)
