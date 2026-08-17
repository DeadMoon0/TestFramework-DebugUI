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
4. Is the issue an active-session problem, or a late-attach/replay expectation that belongs to the future broker plan?

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
- a state-projection problem in the adapter or reducer layer

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
- avoid overlapping breakpoint waits in the same run until the transport redesign work is complete

### Symptom: Continue does nothing useful

Likely causes:

- the paused step is no longer the active waiting step
- the adapter and UI got out of sync after a transport interruption

Recovery:

- rerun the scenario with the UI already open
- reduce the scenario to one breakpoint first
- if the issue repeats, treat it as debugger-infrastructure diagnosis rather than a test-code failure

## Transport Limitation Cases

### Symptom: A completed run is gone after restarting the UI

This is currently a known limitation, not necessarily a defect in your test.

Current behavior:

- completed runs stay available while the current DebugUI process remains alive
- broker-backed durable replay across independent UI restarts is future architecture, not the current guaranteed behavior

Recovery:

- inspect the run before closing the UI
- durable replay is implemented: a journalled run reopens after its test host exits. If a run is missing from the list, the journal marker directory is the thing to check - see [TransportAndProjection.md](./TransportAndProjection.md)

### Symptom: Late attach misses the beginning of the run

This is also part of the current transport limitation envelope.

Recovery:

- start the UI before the run when you need full-fidelity inspection
- use the transport plan as the reference for what future replay semantics should look like

## Diagnostic Logging Guidance

When you are debugging the debugger itself, collect evidence from these layers in order:

1. test-side evidence that the run really executed the expected steps
2. whether the producer and UI agreed on the same pipe name
3. whether the missing behavior is in connection, signal delivery, or state projection
4. reducer/state behavior described in [TransportAndProjection.md](./TransportAndProjection.md)

Do not jump straight to the transport redesign plan unless the current user-facing checks already failed.

## Known Limitation Reminder

The DebugUI is stable for both active-session inspection and reopening recorded runs. The broker-backed transport once planned was retired: the journal provides the durability it was for. See [TransportAndProjection.md](./TransportAndProjection.md).

## See Also

- `Documentation/RunDebuggerFlow.md` in the TestFramework-Core repository (producer side of this transport)
- [Arc42.md](./Arc42.md)
- [TransportAndProjection.md](./TransportAndProjection.md)
