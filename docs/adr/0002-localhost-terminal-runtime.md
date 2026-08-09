# ADR 0002: LocalHost Terminal Runtime

- Status: Accepted
- Date: 2026-08-08

## Context

Direct helper-owned attach tied provider lifetime to one terminal window and could not provide one canonical transcript, replay, attachment takeover, or alternate local and remote presentations.

## Decision

LocalHost owns the canonical Windows ConPTY, provider process, terminal sequencing, replay buffer, attachment leases, and takeover. Windows helpers and browsers are presentation clients only. Detach does not stop; stop is explicit. Provider identity remains separate from OpenCode Stuff session and runtime identities.

PTY data uses the terminal WebSocket/protocol and never MCP.

## Compatibility And Supersession

This decision partially supersedes helper-owned direct attach. Existing generated `attach-workspace.ps1` and container `screen`/shell-loop behavior remain compatibility-only for older workspaces, not a second canonical runtime path.

LocalHost restart loses ConPTY and in-memory replay. A persisted `ProviderSessionId` may allow a new runtime to continue the provider conversation, but does not preserve the old terminal runtime.

## Consequences

The terminal presentation is currently Windows-only. All presentations share exclusive attachment and sequencing semantics. Helpers never own or supervise the provider. See [Terminal Runtime](../architecture/terminal-runtime.md).
