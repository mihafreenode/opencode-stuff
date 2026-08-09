# ADR 0003: RemoteBridge With Cloudflare

- Status: Accepted
- Date: 2026-08-08

## Context

Remote browser terminal access needs public authentication and transport without expanding LocalHost's trust boundary or creating another terminal owner.

## Decision

Cloudflare Access and Tunnel terminate the public trust boundary and forward the protected hostname to loopback RemoteBridge. RemoteBridge independently validates Access identity and exposes only an allowlisted terminal presentation surface. It exchanges a short-lived one-time browser grant for server-side LocalHost attachment credentials.

LocalHost and RemoteBridge remain loopback-only. The tunnel targets RemoteBridge, never LocalHost.

## Rejected Alternatives

- public, LAN-bound, or directly tunneled LocalHost
- RemoteBridge as a general LocalHost, HTTP, TCP, or file proxy
- remote MCP or PTY transport through MCP
- a RemoteBridge-owned provider or terminal runtime

## Consequences

RemoteBridge does not provide remote workspace administration and does not own Cloudflare account credentials. LocalHost remains the sole canonical runtime owner. See [Remote Access](../architecture/remote-access.md) and the [integration setup guide](../integrations/cloudflare-remote-access.md).
