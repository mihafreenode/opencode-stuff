# LocalHost

LocalHost is the loopback control plane and local runtime owner for a machine. It is not merely an API sidecar in front of desktop-owned services. The authoritative workspace orchestration backend, `WorkspaceOrchestrator`, runs inside LocalHost.

## Responsibilities

LocalHost owns canonical shared operations for:

- workspace discovery, creation, import, preparation, start, stop, repair, and removal
- generation, validation, diagnostics, and applied-state updates
- Save Point, synchronization, recovery, and other shared workspace mutations
- workspace instances and operation attribution
- controller and interactive-session records
- terminal runtimes, provider processes, attachments, transcripts, replay, and takeover

Core implements portable domain behavior. LocalHost establishes process ownership, serialization, persistence, discovery, and the boundary used by multiple clients.

## Workspace Instances

A `WorkspaceInstanceId` identifies LocalHost's machine-local instance record for a durable workspace. It binds canonical workspace identity to resolved local paths and runtime state. It is not a terminal identity and must not be substituted for `TerminalRuntimeId`.

## Controllers And Operations

Each connected automation or presentation controller registers a controller session. Operations retain the initiating `ControllerSessionId` for attribution, but they are owned by LocalHost rather than by the client process. A controller may disconnect without deleting the operation or workspace.

Operation records are persisted under LocalHost state so desktop and MCP clients observe the same operation identity, status, progress, result, and cancellation request. Persistence supports discovery and diagnosis after a client or LocalHost restart; it does not imply that an interrupted external process can always resume. Startup reconciliation must report the truthful terminal state of interrupted work.

## Interactive Sessions

Interactive agent sessions are durable LocalHost records that coordinate a terminal runtime, provider identity, attachment lease, and recovery metadata. Their identities and lifetimes are defined in [Interactive Sessions](interactive-sessions.md). The terminal process boundary is defined in [Terminal Runtime](terminal-runtime.md).

## Process And Discovery

LocalHost binds an explicit loopback address, writes a machine-local descriptor containing its instance and endpoint information, and is discovered or started by local clients. Clients must validate descriptors and readiness rather than assuming a fixed process or port. Stale descriptors are discarded.

The desktop may start a LocalHost process and track that process for lifecycle convenience, or discover one that is already running. This does not transfer backend ownership to the desktop. Active operations can outlive the controller that requested them.

## Network Boundary

LocalHost accepts local loopback clients only. It must not bind a LAN address or be placed directly behind a public tunnel or reverse proxy. Same-machine HTTP and WebSocket transport are implementation boundaries, not permission to expose the API.

Remote terminal presentation uses [RemoteBridge](remote-access.md). MCP remains local stdio control and carries no PTY stream.

## Client Boundaries

The Avalonia workspace and **Runtime Resources** flows use LocalHost for shared reads and mutations. Native presentation actions such as opening a service URL remain local to the desktop.

MCP is a controller and projection adapter. Smoke runs and cleanup, workspace creation and lifecycle work, and Excel artifact generation start attributed LocalHost operations. Workspace and smoke artifact reads use typed LocalHost queries. MCP may serialize and format returned models, but it does not choose shared artifact destinations, run smoke/runtime services, or write generated workbooks in-process.

CLI diagnostics and smoke commands may run Core locally when they do not need canonical shared mutable state. Commands that observe or mutate shared operations, sessions, or runtime ownership use LocalHost.
