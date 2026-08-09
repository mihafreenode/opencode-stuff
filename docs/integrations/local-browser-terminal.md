# Local browser terminal

The LocalHost browser terminal is a same-machine presentation client for an existing canonical `InteractiveTerminalRuntime`. LocalHost owns the PTY, provider process, bounded output buffer, transcript sequence, and exclusive attachment lease. The page does not launch OpenCode or create a second runtime.

Open a session at:

```text
http://127.0.0.1:<local-host-port>/terminal/<interactive-agent-session-id>
```

## Security boundary

- LocalHost accepts listener URLs only on explicit loopback IP addresses.
- Terminal WebSockets require a loopback `Host` and an `Origin` exactly matching the LocalHost page origin.
- Browser API mutations carrying `Origin` are rejected unless Host is loopback and Origin is same-origin.
- The page requests an attachment through the canonical API. Its short-lived token remains in page memory and is sent only in the first WebSocket message, never in a URL, referrer, history, or log.
- LocalHost persists only token hashes. Closing the socket releases the presentation lease without stopping the runtime.
- This protects against ordinary cross-origin initiation, not a malicious process running as the same local user.

Never put LocalHost behind a tunnel, proxy, LAN binding, or public listener. Use the separately authenticated [Cloudflare remote access](cloudflare-remote-access.md) path, whose tunnel targets RemoteBridge rather than LocalHost.

## Protocol

The client requests WebSocket subprotocol `opencode-terminal-v1` at:

```text
/api/v1/local-host/interactive-agent-sessions/{sessionId}/terminal/ws
```

The first JSON `hello` supplies `interactiveAgentSessionId`, `terminalRuntimeId`, `attachmentId`, `attachmentToken`, and `afterSequence`; LocalHost rejects mismatches. Typed controls include `attached`, `output`, `resize`, `ack`, `gap`, `detach`, `stop`, `runtime_state`, `error`, `ping`, and `pong`.

Terminal data is binary. A server `output` control announces the canonical sequence and byte length of the next binary frame. Client binary frames are terminal input. The server and browser do not UTF-8 decode or rewrite PTY bytes.

## Replay, flow control, and takeover

- Reconnect uses the runtime's canonical `EarliestSequence` and `LatestSequence`; output that aged out produces an explicit `gap`.
- Resize messages update the canonical runtime dimensions.
- The WebSocket adds no second output queue. It reads the bounded runtime buffer with at most one unacknowledged chunk and one timed send in flight.
- A client that cannot send or acknowledge within the bound is disconnected; the provider runtime continues.
- Attachment is exclusive. A second presentation receives an attached conflict and must explicitly request takeover; takeover detaches the old presentation without restarting the runtime.
- Disconnect, refresh, replay gap, and presentation takeover do not stop the PTY or provider process.

The page stores only its last observed output sequence in session storage. Session, runtime, provider, dimensions, state, and attachment authority always come from LocalHost. xterm.js, HTML, CSS, and JavaScript are packaged locally under a restrictive CSP with no CDN dependency.
