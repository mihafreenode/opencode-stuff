# Interactive Sessions

This page is authoritative for interactive identity and ownership. The identities are deliberately separate because they have different owners and lifetimes.

## Identities

| Identity | Meaning | Owner | Lifetime |
| --- | --- | --- | --- |
| `ControllerSessionId` | A desktop, MCP, CLI, or other controller registration used for attribution and permissions | LocalHost | Identifies one controller lifecycle; reconnecting creates a new controller session while the disconnected record may remain for attribution |
| `InteractiveAgentSessionId` | The OpenCode Stuff record for an interactive agent conversation bound to a workspace instance | LocalHost | Survives presentation detach and controller disconnect; may become stopped or failed while its record remains under retention policy |
| `TerminalRuntimeId` | One LocalHost-owned ConPTY and provider-process runtime serving an interactive session | LocalHost | From terminal runtime start until explicit stop, provider exit, failure, or LocalHost process loss |
| `AttachmentId` | An exclusive presentation lease for terminal input/output | LocalHost | From grant/attach until detach, takeover, expiry, disconnect completion, or runtime stop |

`ProviderSessionId` is separate from all four identities. It is the provider's own conversation/session identity, learned or verified from the provider. It may continue across replacement terminal runtimes when the provider supports restoration. It is never an attachment credential, controller identity, or substitute for `InteractiveAgentSessionId`.

`WorkspaceInstanceId` identifies the machine-local workspace instance. It scopes sessions and operations but is not one of the four interactive identities and must never replace `TerminalRuntimeId`.

## Ownership Rules

- LocalHost creates and persists interactive-session records.
- LocalHost starts and owns terminal runtimes and provider processes.
- A controller requests actions but does not own the resulting session or provider.
- A presentation owns only its live UI connection; LocalHost owns the attachment lease and credentials.
- Attachment credentials are short-lived, scoped, and stored only as hashes by LocalHost.
- Only one active attachment controls a terminal runtime. Takeover is explicit and revokes the prior attachment.
- Detach releases presentation ownership without stopping the terminal runtime.

## Typical Lifetime

1. A controller registers and receives a `ControllerSessionId`.
2. It creates or selects an `InteractiveAgentSessionId` for a workspace instance.
3. LocalHost starts or reuses a `TerminalRuntimeId` and launches or restores the provider.
4. LocalHost grants an `AttachmentId` to one presentation.
5. The presentation detaches or is taken over; the runtime and provider continue.
6. A later presentation receives a new attachment and replays output from a known sequence.
7. Explicit stop or terminal failure ends the terminal runtime. The interactive record and `ProviderSessionId` may remain available for restoration.

See [Terminal Runtime](terminal-runtime.md) for restart, replay, and backpressure behavior.
