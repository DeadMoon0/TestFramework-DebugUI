# DebugUI PipeAdapter Flow

This document describes how the DebugUI side receives RunDebugger signals from Core, routes them through the pipe host, and projects them into the data/state layer.

## Main pieces

- `RunDebuggerPiped` is the Core-side `IRunDebugger` implementation that writes protocol messages to the pipe.
- `RunDebuggerHostPiped` is the DebugUI-side host loop that accepts connections and dispatches signals by kind.
- `DebugPipeTranslator` is the DebugUI adapter that translates pipe signals into state updates.
- `DebugRunStateReducer` is the data-processing layer. It applies protocol messages to `MainState`, `RunState`, `StageNodeState`, and `StepNodeState`.
- `WpfStateService` is the graph-backed state store used by the data layer and the UI.

## Transport roles

### Core side

`RunDebuggerPiped` implements `IRunDebugger` and sends these signals:

- `InitTimelineRunSignal`
- `EntityTransitionSignal`
- `ValueUpdateSignal`
- `LogEntrySignal`
- `DiagnosticSignal`
- `TimelineRunFinishedSignal`
- `BreakpointHitRequestSignal`

For breakpoints it also waits for:

- `BreakpointHitContinueSignal`

### DebugUI side

`RunDebuggerHostPiped.Begin()` creates the pipe host and runs an infinite receive loop:

1. wait for a connection
2. wait for the next signal
3. dispatch by `SignalKind`
4. call the matching abstract handler

The host currently supports:

- `EntityTransition`
- `InitTimelineRun`
- `TimelineRunFinished`
- `ValueUpdate`
- `LogEntry`
- `Diagnostic`
- `BreakpointHitRequest`

## Translation layer

`DebugPipeTranslator` is the concrete host implementation used by DebugUI.

It routes signals as follows:

- `OnInitTimelineRunAsync(...) -> reducer.ApplyInitTimelineRunAsync(...)`
- `OnEntityTransitionAsync(...) -> reducer.ApplyEntityTransitionAsync(...)`
- `OnValueUpdateAsync(...) -> reducer.ApplyValueUpdateAsync(...)`
- `OnLogEntryAsync(...) -> reducer.ApplyLogEntryAsync(...)`
- `OnDiagnosticAsync(...) -> reducer.ApplyDiagnosticAsync(...)`
- `OnTimelineRunFinishedAsync(...) -> reducer.ApplyTimelineRunFinishedAsync(...)`
- `OnBreakpointHitRequestAsync(...) -> reducer.ApplyBreakpointHitRequestAsync(...)`, then sends `BreakpointHitContinueSignal`

That means `DebugPipeTranslator` is intentionally thin. The real behavior lives in the reducer.

## Reducer projection rules

`DebugRunStateReducer` is the data-processing layer for the protocol.

### 1. InitTimelineRun

`ApplyInitTimelineRunAsync(...)` creates a fresh `RunState` and seeds:

- `SessionId`
- `Name`
- `ProjectPath`
- initial lifecycle values
- run-level `Variables`
- run-level `Artifacts`
- one `StageNodeState` per stage
- one `StepNodeState` per step
- one derived `StageLayerState` per dependency layer inside the stage

The reducer now constructs fresh `StateDictionary` instances during this initialization path so tests and runtime do not depend on cloned default dictionary instances.

### 2. EntityTransition

`ApplyEntityTransitionAsync(...)` updates lifecycle state by `EntityKind`.

Run transitions update:

- `LifecycleState`
- `PreviousLifecycleState`
- `LastTransitionAtUtc`

Stage transitions update:

- lifecycle metadata
- the stage-level execution-layer progression markers

Step transitions update:

- lifecycle metadata
- `AttemptCount` when entering `Running`
- `State` mapped from lifecycle outcome
- step input snapshots when the step enters `Running`
- the owning `StageLayerState`, which stays active until every step in that layer reaches a final state

When a step is in `WaitingForRetry`, the reducer uses `OutcomeState` to preserve the result of the previous attempt.

### 3. ValueUpdate

`ApplyValueUpdateAsync(...)` updates both the run-level value stores and the step-level outputs when the signal is step-scoped.

For variables:

- update `RunState.Variables`
- if `Stage` and `StepId` are present, update `StepNodeState.Outputs`

For artifacts:

- update `RunState.Artifacts`
- if `Stage` and `StepId` are present, update `StepNodeState.Outputs`

The reducer keeps the full `DebugValueEnvelope`, so the UI/data layer has access to the common JSON payload and any artifact-specific custom payload.

### 4. LogEntry

`ApplyLogEntryAsync(...)` appends the structured log entry to the binding-facing step-attempt state graph.

Projection rules:

- the signal is ignored unless it identifies a concrete step attempt
- every explicit log entry is stored in `StepAttemptState.LogEntries`
- `StepAttemptState.LatestLogEntry` is updated

This gives the UI both:

- a rich object model owned by a specific attempt
- a step node with the full ordered attempt collection

### 4a. Diagnostic

`ApplyDiagnosticAsync(...)` stores non-step messages separately at run scope.

Projection rules:

- diagnostics are stored in `RunState.Diagnostics`
- diagnostics are not copied into stage, step, or attempt log collections
- diagnostics are not part of `DebugOut`

### 5. Derived framework logs

The reducer also derives framework-owned log entries from the state-machine signals themselves.

Current derivation rules:

- step `Running` creates or activates a `StepAttemptState` and appends an execution line
- step terminal transitions append pass/fail/timeout/retry lines to the current attempt
- step-scoped `ValueUpdate` signals append variable/artifact update lines to the current attempt
- `BreakpointHitRequest` appends a breakpoint line to the current attempt

This means the UI data layer no longer depends on Core emitting framework log text as a separate signal stream.

### 6. BreakpointHitRequest

`ApplyBreakpointHitRequestAsync(...)` marks the addressed step as waiting at a breakpoint:

- increment `BreakpointHitCount`
- set `IsWaitingAtBreakpoint = true`
- set `LastBreakpointAtUtc`
- append a breakpoint event to the currently active attempt when one exists

After that, `DebugPipeTranslator` immediately sends `BreakpointHitContinueSignal` back to Core.

### 7. TimelineRunFinished

`ApplyTimelineRunFinishedAsync(...)` marks the run as finished:

- `IsFinished = true`
- `FinishedAtUtc = now`

## State model coverage

The current DebugUI data layer exposes the pieces the reducer needs to project the protocol:

- `MainState.ActiveRun`
- `RunState` with lifecycle, run-level values, finish markers, and the canonical stage tree
- `StageNodeState` with lifecycle, direct stage metadata, ordered execution layers, and the step collection for that stage
- `StageLayerState` with parallel-ready step groups and completion tracking for each dependency layer
- `StepNodeState` with direct step metadata, lifecycle, attempts, breakpoints, unified inputs/outputs, mapped `StepState`, and the attempt collection
- `StepAttemptState` with per-attempt log ownership, timestamps, and active state
- `LogEntryState` with direct binding properties for message, severity, timestamp, stage, step, attempt, and assertion scope

The WPF layer should consume the canonical tree through `DebugRunStateQueries` instead of binding the old run structure snapshot or traversing raw dictionaries directly.

Derived UI conveniences such as ordered layer keys, current active layer, latest attempt summary, latest log entry, and aggregated attempt debug text should come from `DebugRunStateQueries`, not persisted state properties.

This keeps the WPF surface separate from the protocol/data-processing layer.

## Testability and dispatcher behavior

The DebugUI data/state tests now run against the extracted `TestFramework.DebugUI.Data` library instead of the WPF app assembly.

For tests, `WpfStateService` uses an immediate mutation dispatcher so that:

- state mutations happen synchronously
- callback dispatch can also run synchronously
- background worker-thread crashes do not make the test host nondeterministic

The WPF app still uses the normal host-side behavior. The synchronous dispatch path is only activated when the configured dispatcher also implements mutation dispatch.

## Practical flow summary

End-to-end, the current flow is:

1. Core emits `InitTimelineRun` with the full run structure.
2. DebugUI creates the initial canonical run tree.
3. Core emits run, stage, and step `EntityTransition` signals as execution advances.
4. Core emits `ValueUpdate` signals whenever variables or artifacts change.
5. Core emits `LogEntry` signals whenever `ScopedLogger` formats an event.
6. Core emits `BreakpointHitRequest`; DebugUI records the breakpoint state and replies with `BreakpointHitContinue`.
7. Core emits `TimelineRunFinished` after the run closes.
8. The reducer leaves DebugUI with a fully queryable state graph for the data layer and UI.