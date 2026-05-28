# Reliable Debug Transport Plan

This document turns the DebugUI transport redesign into an executable migration plan.

## Hard constraints

- The user must not have to do anything on the test side. If the debugger is available, use it. If it is not available, do not use it and do not fail the run because of that.
- The testhost cannot own durable run state. After the timeline run ends, the testhost exits and its memory is gone. Completed runs that should remain inspectable must be stored on the debugger side.
- The UI staying open across many runs is a first-class scenario.
- Opening the UI after a run already completed is a first-class scenario.

## Current problems

The current design has four root issues.

1. Activation is incidental.
  Historically `CommonDebugger` tried to discover `RunDebuggerPiped` by reflection and assembly load side effects. That was fragile and hard to reason about.

2. Transport lifetime is coupled to the WPF window.
   `MainWindow` owns `DebugPipeTranslator`, which owns the pipe host. That means transport availability depends on window startup timing and shutdown behavior.

3. The transport is live-stream only.
   If the consumer misses messages or connects late, there is no durable source of truth to reconstruct a completed run.

4. The system exposes a single implicit connection path.
   That makes reconnect, multi-run persistence, late attach, and observability much harder than they need to be.

## Target architecture

The target architecture has three layers.

### 1. Core event production

Core continues to emit the same debugger semantics:

- init timeline run
- entity transitions
- value updates
- log entries
- assertions
- breakpoint requests and continue
- run finished

Core does not own named pipes, persistence, or debugger-side state. Core only knows how to publish debug events to an optional client boundary.

### 2. Debugger-side broker

The broker becomes the durable owner of debug sessions.

It owns:

- transport endpoint creation
- optional discovery by producers
- session registration
- per-session ordering and sequencing
- buffering
- persistence
- replay
- late attach
- broker health and diagnostics

This is the authoritative source of truth for completed runs.

### 3. DebugUI consumer and projector

DebugUI becomes a broker consumer. It requests snapshots and deltas from the broker and projects them into the state graph. The UI no longer owns the transport host.

## Recommended deployment shape

Use a debugger-owned broker process or long-lived local service started by DebugUI.

- DebugUI start: ensure broker is running, then connect to it.
- Testhost start: opportunistically discover the broker and connect if it exists.
- Testhost without broker: use a no-op client path and run normally.
- Completed runs: persist under the broker so they remain available after testhost exit.

This preserves zero user action on the test side while moving durability to the correct side.

## Target component model

### Core-side components

- `IRunDebugger`
  Keep as the public execution-facing contract for now.

- `PipeRunDebugger`
  Current built-in `IRunDebugger` implementation in Core. It emits the shared pipe protocol directly without reflection or a DebugUI-side sender dependency.

- Broker-aware producer boundary
  If the broker architecture is implemented later, introduce it next to `PipeRunDebugger` instead of reviving a separate sender adapter layer.

### Broker-side components

- `DebugBrokerHost`
  Owns listener startup, session registry, health state, and consumer connections.

- `DebugSessionRegistry`
  Tracks active and completed session metadata.

- `DebugSessionStore`
  Persists canonical completed-run snapshots and incremental event logs on the debugger side.

- `DebugSessionStream`
  Owns sequence numbering, event append, replay, and ack bookkeeping per session.

- `DebugBrokerProtocolServer`
  Exposes broker endpoints to producers and UI consumers.

### DebugUI-side components

- `DebugBrokerClient`
  Connects to the broker, lists sessions, attaches to one session, requests replay or snapshots, and subscribes to deltas.

- `DebugSessionConsumer`
  Owns broker connection lifetime independent of `MainWindow`.

- `DebugRunStateReducer`
  Remains the projector boundary, but becomes replay-safe and session-archive aware.

- `MainState`
  Owns `ActiveRun`, completed-run retention, and transport connection state for the UI.

## Protocol plan

Keep the existing signal payloads where possible, but place them inside a versioned envelope.

### Event envelope

- `ProtocolVersion`
- `SessionId`
- `SequenceNumber`
- `OccurredAtUtc`
- `MessageType`
- `CorrelationId` optional
- `AckSequence` optional
- `Payload`

### Control messages

- `Hello`
- `ProducerAttach`
- `ConsumerAttach`
- `SessionOpened`
- `SessionClosed`
- `Heartbeat`
- `Ack`
- `ReplayFrom`
- `SnapshotRequested`
- `SnapshotChunk` if needed

### Delivery semantics

- Ordering guarantee: in-order per session
- Transport guarantee: at-least-once is acceptable
- Reducer requirement: idempotent application of duplicates
- Recovery mechanism: replay from last acknowledged sequence

## Persistence plan

The broker must persist enough data to reconstruct a completed run after the producer is gone.

Recommended shape:

- one session metadata record per run
- one append-only event log per session
- optional materialized snapshot per session for faster late attach

Recommended behavior:

- during the run, append events and optionally checkpoint snapshots
- on run finish, finalize metadata and ensure durable flush
- on late attach, serve the latest snapshot plus deltas, or replay the event log if no snapshot exists

The testhost must never be the place where completed-run durability lives.

## State model plan

The first slice is already implemented.

- `MainState.CompletedRuns` now retains completed runs on the debugger side of the app state.
- `DebugRunStateReducer` archives the finished active run before replacing it.

That is not enough by itself, because it only survives while the DebugUI process remains alive. The remaining work is to move from in-memory retention to broker-backed persistence.

## Migration plan

### Slice 0. Completed-run retention in DebugUI state

Status: done.

Purpose:

- stop losing a completed run immediately when a later run becomes active in the same UI process

Proof:

- reducer regression tests for finished-run retention pass

### Slice 1. Introduce a broker-ready client boundary in Core

Purpose:

- separate `IRunDebugger` semantics from the concrete pipe transport

Changes:

- add a broker-aware producer boundary only if the broker architecture is still needed
- keep `PipeRunDebugger` as the default optional debugger path in Core
- preserve no-op behavior when no debugger host is available

Proof:

- producer-side tests show that runs still complete when the broker is absent
- producer-side tests show that broker discovery is optional and automatic

### Slice 2. Create a debugger-side persistent session store

Purpose:

- move run durability off the testhost and out of transient UI memory

Changes:

- add `DebugSessionStore`
- persist session metadata and event logs under the debugger side
- add retention policy hooks and storage cleanup rules

Proof:

- a completed run remains available after the testhost exits
- store reload works after restarting DebugUI and the broker

### Slice 3. Extract transport ownership out of `MainWindow`

Purpose:

- remove transport startup races tied to the WPF window lifecycle

Changes:

- add `DebugSessionConsumer`
- move connection lifetime out of `MainWindow`
- keep `MainWindow` as a subscriber to broker-backed state, not the host

Proof:

- UI can close and reopen without corrupting broker session state
- transport disconnect is not a crash reason for the UI

### Slice 4. Add the versioned broker protocol

Purpose:

- make replay, late attach, and reconnect explicit instead of implicit

Changes:

- add event envelope
- add control messages
- add per-session sequence numbers
- add ack and replay support

Proof:

- duplicate delivery is tolerated
- gap detection works
- reconnect from last ack works

### Slice 5. Make the reducer replay-safe

Purpose:

- allow broker replay and snapshot hydration without state corruption

Changes:

- track last applied sequence per session
- ignore duplicates safely
- reject or flag out-of-order gaps
- add snapshot hydration path

Proof:

- snapshot plus delta produces the same result as full ordered replay

### Slice 6. Add late-attach UI flows

Purpose:

- support opening DebugUI after the run already completed

Changes:

- session list in broker client
- attach to completed session
- hydrate `MainState` from persisted broker state

Proof:

- completed run remains inspectable after testhost exit and later UI startup

### Slice 7. Migrate breakpoint flow onto the broker path

Purpose:

- remove the last synchronous control dependency on the raw pipe loop

Changes:

- route breakpoint request and continue through broker protocol
- preserve current semantics for test execution

Proof:

- breakpoint waits and continues still behave correctly under reconnect and session replay boundaries

### Slice 8. Remove direct window-owned pipe hosting

Purpose:

- finish the migration and remove the current architectural smell

Changes:

- retire `DebugPipeTranslator : RunDebuggerHostPiped` as the primary path
- remove direct host startup from `MainWindow`
- keep only a narrow compatibility shim if needed

Proof:

- all targeted DebugUI tests pass on the broker path
- direct pipe path can be disabled without loss of required behavior

## File-by-file implementation map

### Core

- `TestFramework-Core/TestFramework.Core/Debugger/CommonDebugger.cs`
  Keep direct optional activation through `PipeRunDebugger`; introduce broker activation beside it only if broker work proceeds.

- `TestFramework-Core/TestFramework.Core/Debugger/IRunDebugger.cs`
  Keep stable if possible; add new internal client boundary adjacent to it.

- `TestFramework-Core/TestFramework.Core/Debugger/CompositeRunDebugger.cs`
  Keep fan-out and isolation behavior while introducing the broker-aware debugger.

### Pipe adapter and transport

- `TestFramework-Core/TestFramework.Core/Debugger/PipeRunDebugger.cs`
  Current Core-owned producer transport.

- `TestFramework-DebugUI/TestFramework.DebugUI.PipeAdapter/PipeClient.cs`
  Retired from the producer path. The live producer transport now lives in Core.

- `TestFramework-DebugUI/TestFramework.DebugUI.PipeAdapter/ProtocolStream.cs`
  Evolve into a versioned envelope codec if named pipes remain in the first broker implementation.

### DebugUI state and intake

- `TestFramework-DebugUI/TestFramework.DebugUI/MainWindow.xaml.cs`
  Remove transport ownership.

- `TestFramework-DebugUI/TestFramework.DebugUI/DebugPipeTranslator.cs`
  Replace with broker-backed consumer service or reduce to a temporary compatibility layer.

- `TestFramework-DebugUI/TestFramework.DebugUI.Data/DebugRunStateReducer.cs`
  Add sequence-aware replay-safe projection.

- `TestFramework-DebugUI/TestFramework.DebugUI.Data/State/MainState.cs`
  Extend connection and completed-session surfaces as broker features arrive.

## Verification plan

### Producer-side proof

- broker absent: run still succeeds with no user action
- broker appears after producer creation: producer still attaches when the first real event is emitted
- broker restart during run: producer reconnect policy behaves deterministically

### Broker proof

- session registration and closure
- heartbeat and liveness
- replay from last ack
- late attach to completed session
- persistence reload after broker restart

### Reducer proof

- duplicate event suppression
- gap detection
- snapshot and delta equivalence
- preserved path-binding behavior when active run changes

### UI proof

- persistent-open-window sequence:
  `ArtifactVersionJourney_Completes -> ForEachFanOut_Completes -> RetrySample_CreatesMultipleAttempts -> LocalIoEventAndArtifact_Completes`
- open UI after run finished and still inspect the completed run
- disconnect or pipe corruption is not a crash reason

## Recommended order of execution

Implement in this order:

1. Core broker-ready client boundary
2. debugger-side persistent session store
3. window-independent consumer service
4. versioned envelope and replay protocol
5. replay-safe reducer
6. late-attach UI flow
7. breakpoint migration
8. direct pipe path retirement

This order keeps the hardest reliability problems near the front and avoids polishing the old path further than necessary.

## Non-goals

- redesign the timeline execution engine
- redesign the visual appearance of DebugUI
- move durable run ownership back into the testhost
- require manual environment configuration to make debugging work

## Decision summary

- Keep the signal vocabulary.
- Add a versioned broker envelope around it.
- Make attach optional and automatic on the test side.
- Put durability, replay, and late attach on the debugger side.
- Remove transport ownership from `MainWindow`.
- Treat persistent-open-window and late-attach as mandatory scenarios.