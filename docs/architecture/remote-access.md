# Remote Access

Remote access is an optional presentation path to an existing LocalHost-owned interactive terminal runtime. It does not make LocalHost remote.

```text
remote browser
    -> Cloudflare Access
    -> Cloudflare Tunnel
    -> loopback RemoteBridge
    -> loopback LocalHost
    -> canonical terminal runtime
```

## Trust Boundary

- Cloudflare owns public TLS, user authentication, Access policy, and the outbound tunnel.
- RemoteBridge validates the Access JWT cryptographically and enforces the configured public host, origin, issuer, and audience.
- RemoteBridge exposes only an allowlisted remote session and terminal presentation surface.
- LocalHost credentials stay server-side. The browser receives a short-lived, identity-bound, one-time RemoteBridge grant.
- LocalHost remains loopback-only and owns sessions, attachments, terminal runtimes, transcripts, and provider processes.
- RemoteBridge owns no runtime and duplicates no canonical terminal state.

## Explicit Non-Goals

RemoteBridge is not:

- a public or general LocalHost proxy
- remote workspace administration
- arbitrary HTTP, TCP, or file transfer
- an MCP transport
- an owner of Cloudflare account or tunnel credentials

No PTY stream travels through MCP. Neither LocalHost nor RemoteBridge binds a LAN address, and the tunnel must target RemoteBridge, never LocalHost.

Deployment and setup belong in the [Cloudflare remote access integration guide](../integrations/cloudflare-remote-access.md). See [ADR 0003](../adr/0003-remotebridge-cloudflare.md) for the decision.
