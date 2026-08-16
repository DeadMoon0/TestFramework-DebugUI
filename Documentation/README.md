# DebugUI Documentation Map

Use this folder in two passes: user-facing guidance first, transport internals second.

## User-Facing Documents

- [Arc42.md](./Arc42.md): module architecture floor and current runtime boundaries
- [ERROR-HANDLING-DEBUGUI.md](./ERROR-HANDLING-DEBUGUI.md): connection, transport, and breakpoint recovery guidance

## Internal Design Documents

- [PipeAdapterFlow.md](./PipeAdapterFlow.md): current signal routing and reducer projection model
- [ReliableDebugTransportPlan.md](./ReliableDebugTransportPlan.md): future transport redesign plan

If you are debugging a test run, start with the user-facing documents. Read the internal design documents only when the issue is in the debugger infrastructure itself.