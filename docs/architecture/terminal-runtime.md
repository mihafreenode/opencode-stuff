# Terminal Runtime

The canonical interactive terminal is LocalHost-owned. LocalHost creates and supervises the Windows ConPTY and the provider process, assigns terminal output sequence numbers, and enforces the active attachment lease.

## Presentations

The same runtime can be presented through:

- the Windows terminal helper launched by the desktop
- the LocalHost same-machine browser terminal
- a remote browser through RemoteBridge

These components relay input, output, resize, acknowledgement, detach, and stop requests. They do not launch or own the provider. Terminal presentation is currently Windows-only because the canonical PTY implementation is ConPTY.

## Detach, Stop, And Takeover

Detach is not stop. Closing a window, WebSocket, or helper releases the attachment while LocalHost keeps the terminal runtime and provider alive.

Only one attachment may control a runtime. A takeover request must identify the expected current attachment, atomically revoke it, and issue a new lease. This prevents accidental dual writers and stale clients taking control silently.

Stop is explicit. It ends the LocalHost-owned terminal runtime and provider process; it is not inferred from presentation loss.

## Replay, Gaps, And Backpressure

LocalHost sequences canonical output and retains a bounded replay window. A reconnecting presentation supplies its last acknowledged sequence and receives available later output.

If requested output has fallen outside the replay window, LocalHost sends a gap indication rather than inventing or silently omitting continuity. Presentations must disclose the gap and continue from the server's available sequence.

Acknowledgements and bounded queues prevent a slow presentation from consuming unbounded memory. Backpressure policy may disconnect or gap a lagging attachment, but must not stall provider output indefinitely or transfer runtime ownership to the client.

## Restart Semantics

ConPTY handles and child-process supervision belong to the LocalHost process. A LocalHost restart therefore loses the existing terminal runtime and its in-memory replay buffer. Persisted records must report that loss truthfully; they cannot claim the old `TerminalRuntimeId` is still attached.

The provider's own `ProviderSessionId` is separate and may remain usable. LocalHost can create a new terminal runtime and ask the provider to continue that provider session when supported. Provider continuation is not ConPTY continuation.

## Compatibility Path

Older generated `attach-workspace.ps1` and container `screen`/shell-loop paths are compatibility-only for existing generated workspaces. They are not the canonical architecture and must not be extended as a second runtime owner. New interactive behavior belongs in the LocalHost terminal runtime.

See [Interactive Sessions](interactive-sessions.md) and [ADR 0002](../adr/0002-localhost-terminal-runtime.md).
