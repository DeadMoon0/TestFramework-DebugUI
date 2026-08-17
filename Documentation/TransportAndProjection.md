# Transport and Projection

How a running test reaches the board, and how the board is derived from what arrives.

This replaces two documents retired on 2026-08-17: `PipeAdapterFlow.md`, which described a
`PipeAdapter` project that no longer exists, and `ReliableDebugTransportPlan.md`, whose plan has
largely shipped. What became of that plan is recorded at the end.

---

## The pieces

| Side | Component | Job |
|---|---|---|
| Core | `CommonDebugger` | Decides which debuggers a run gets |
| Core | `DebuggingRunSession` | Queues signals off the run's hot path |
| Core | `PipeRunDebugger` / `PipeClient` | Sends signals to an attached UI |
| Core | `JournalRunDebugger` / `DebugJournal` | Writes the run to disk as NDJSON |
| Core | `OutputRunDebugger` | Renders the run into xUnit test output |
| UI | `PipeRunEventSource` | Accepts one connection per run, answers breakpoints |
| UI | `JournalRunEventSource` | Lists and replays recorded runs |
| UI | `RunIngestService` | Coalesces envelopes and dispatches them to the store |
| UI | `MainReducer` / `RunProjection` | Builds `RunGraph` from signals |
| UI | `RunBoardLayout` | Derives board geometry from `RunGraph` |

## Attaching costs almost nothing

`CommonDebugger` only creates `PipeRunDebugger` when a UI is actually listening. The check is
`PipeAvailability.IsListening`: on Windows a named pipe is a path, so this is a directory probe of
`\\.\pipe\`, cached for 500 ms. No connection is attempted, so a run with no UI pays no connect
timeout. Elsewhere there is nothing cheap to probe, and the fallback is a connect bounded at 250 ms.

It is a probe per run rather than a latch, so a UI started halfway through a suite is picked up by the
next run.

Journalling is independent and gated on its own marker directory existing, so a machine without the
tool installed writes nothing. **The launcher creates that directory** — which is why runs only start
being recorded once the tool has been installed at least once.

## The wire

One connection carries one run. Every signal is wrapped by `DebugEnvelopeCodec.Wrap` into a versioned
envelope carrying a per-connection sequence number, then framed as a 4-byte length followed by UTF-8
JSON. The sequence is what lets a consumer spot gaps and drop duplicates on replay.

Both ends set `PipeOptions.CurrentUserOnly`, so a run under another account cannot feed this UI and a
pipe squatting on the well-known name under another account cannot receive a run's stream.

Signal kinds: `InitTimelineRun`, `EntityTransition`, `ValueUpdate`, `LogEntry`, `Assertion`,
`BreakpointHitRequest` / `BreakpointHitContinue`, `TimelineRunFinished`, `CancelRun`.

### Everything is queued except the breakpoint exchange

`DebuggingRunSession` puts signals on a channel drained by a single consumer, so producing telemetry
never blocks a step. The breakpoint exchange is deliberately **not** queued: it is control flow, not
telemetry, and routing it through the queue would make a step's start wait on logging throughput.

That exchange is a request and its reply, and it must stay a single call — `PipeClient.ExchangeAsync`
registers the waiter *before* sending. Reads happen on their own loop, so a reply can arrive before
the sending continuation resumes; an unmatched reply is **dropped, not queued**, and a caller that
registered late waits out the whole timeout for an answer that already came. That was a real defect:
every step of every attached run risked a 600-second stall. **Never send-then-register on this
transport.**

## Consuming

`PipeRunEventSource` listens with `MaxAllowedServerInstances` and serves each accepted connection on
its own task, so a suite running twenty timelines at once does not make them queue behind each other.

`HandleAsync` applies transport-level meaning before anything reaches the store:

- `TimelineRunFinished` marks the session finished and is passed on.
- `BreakpointHitRequest` is answered. If the UI's pause policy says hold, the request is recorded as
  pending and raised so the UI can offer a release; otherwise a `BreakpointHitContinue` goes back
  immediately and the envelope is **not** raised. A frame that cannot be read is still answered — a
  step must never be held because its consumer was confused.
- Everything else is passed straight through.

`RunIngestService` coalesces envelopes over a short window before dispatching, so a log-heavy run does
not dispatch once per line.

## Only the selected run is projected

Every session is ingested; only the selected one is turned into a `RunGraph`. Unselected live runs are
kept as raw envelopes, bounded at `RetainedEventsPerRun` (20 000) — past that the earliest are
dropped and the board is rebuilt from the journal once the run has finished. Selecting a run therefore
means replaying it: from memory for a live run, from its journal for a recorded one.

Geometry is never stored. `RunBoardLayout.Compute` is a pure function of the graph and the
measurements, so the same graph always lays out the same way and there is no layout state to keep
correct. See the layout engine's own remarks for the routing rules.

## What the 2026-05 reliability plan asked for, and what shipped

The retired `ReliableDebugTransportPlan.md` proposed a separate broker process. That was not built.
Most of what it was *for* was achieved inside the existing two-process shape:

| Plan goal | Outcome |
|---|---|
| Runs survive the test host exiting | **Shipped** — NDJSON journal per run, replayed by `JournalRunEventSource` |
| Concurrent runs do not fight over one pipe instance | **Shipped** — one server instance per run |
| Late attach; no permanent give-up | **Shipped** — per-run availability probe instead of a latch |
| Versioned, framed protocol | **Shipped** — `DebugEnvelope` + length-prefixed UTF-8 |
| A hung UI cannot stall a run forever | **Shipped** — bounded write and wait paths |
| Authenticated channel | **Shipped** — `PipeOptions.CurrentUserOnly` on both ends |
| Separate broker process | **Not built, and not currently wanted** — it bought reliability that the journal now provides, at the cost of a third process to install, version and debug |

If a broker is ever revisited, the honest reason would be something the journal cannot do — such as
one consumer watching runs from several machines — not durability.
