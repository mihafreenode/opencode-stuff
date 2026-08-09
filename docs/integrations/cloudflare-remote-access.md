# Cloudflare remote access

Remote access is an opt-in presentation path to an existing canonical interactive terminal. It does not create a second runtime, PTY, session store, workspace control plane, or remote MCP endpoint. See the repository [architecture summary](../architecture/overview.md) and the [local browser terminal protocol](local-browser-terminal.md).

## Architecture

```text
Remote browser (HTTPS/WSS)
    -> Cloudflare Access
    -> Cloudflare Tunnel
    -> RemoteBridge (HTTP/WS on 127.0.0.1:38443)
    -> LocalHost (HTTP/WS on 127.0.0.1:43127)
    -> canonical InteractiveTerminalRuntime
```

Cloudflare owns public TLS, identity authentication, Access policy, and the outbound tunnel. RemoteBridge independently validates the Access JWT and exposes a narrow terminal-presentation route allowlist. LocalHost remains the sole owner of session records, attachment leases, terminal credentials, transcript sequencing, PTY/provider lifetime, replay, and takeover.

Both local hops must bind explicit loopback addresses. Do not publish ports, add inbound firewall rules, bind either service to a LAN address, or target LocalHost directly from `cloudflared`. MCP remains local stdio and is not exposed.

## Prerequisites

- An extracted package with `bin/remote-bridge/`, `bin/local-host/`, and `config/remote-bridge/appsettings.json`.
- A canonical terminal session that works locally first.
- A Cloudflare-managed DNS zone, Zero Trust account, named Tunnel, and dedicated hostname such as `terminal.example.com`.
- Separately installed and managed `cloudflared`.
- A self-hosted Access application with a narrow allow policy for the intended identity.

## Access configuration

1. Create an Access **Self-hosted** application for the exact hostname with no path bypass.
2. Add a narrow **Allow** policy for an email, identity-provider group, or equivalent selector. Do not use `Everyone`.
3. Copy the exact Application Audience (AUD) Tag as `Cloudflare:Audience`.
4. Copy `https://<team-name>.cloudflareaccess.com` as `Cloudflare:TeamDomain`.
5. Configure the exact JWT issuer, normally the same origin without a trailing slash, as `Cloudflare:Issuer`.

RemoteBridge fetches JWKS from `<TeamDomain>/cdn-cgi/access/certs` and requires RS256, exact issuer and audience, valid key identity, future expiration, and valid `nbf` when present. See Cloudflare's [application-token documentation](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/application-token/).

## RemoteBridge configuration

RemoteBridge is disabled by default. Configuration precedence is packaged JSON, user JSON, environment variables, then command-line arguments.

```text
<package>\config\remote-bridge\appsettings.json
%LOCALAPPDATA%\OpenCode.Workspace.Manager\remote-bridge\appsettings.json
```

Prefer the per-user file:

```json
{
  "RemoteAccess": {
    "Enabled": true,
    "PublicOrigin": "https://<remote-hostname>",
    "ListenerUrl": "http://127.0.0.1:38443",
    "LocalHostBaseUrl": "http://127.0.0.1:43127"
  },
  "Cloudflare": {
    "TeamDomain": "https://<team-name>.cloudflareaccess.com",
    "Issuer": "https://<team-name>.cloudflareaccess.com",
    "Audience": "<exact-application-aud-tag>",
    "AccessAssertionHeader": "Cf-Access-Jwt-Assertion",
    "JwksRefreshMinutes": 15,
    "JwksRefreshFailureRetrySeconds": 30
  }
}
```

Equivalent environment variables use .NET double underscores:

```powershell
$env:RemoteAccess__Enabled = "true"
$env:RemoteAccess__PublicOrigin = "https://<remote-hostname>"
$env:RemoteAccess__ListenerUrl = "http://127.0.0.1:38443"
$env:RemoteAccess__LocalHostBaseUrl = "http://127.0.0.1:43127"
$env:Cloudflare__TeamDomain = "https://<team-name>.cloudflareaccess.com"
$env:Cloudflare__Issuer = "https://<team-name>.cloudflareaccess.com"
$env:Cloudflare__Audience = "<exact-application-aud-tag>"
```

These values are identifiers and origins, not credentials. Never place Cloudflare API/tunnel tokens, `cert.pem`, tunnel credential JSON, Access cookies, Access JWTs, or LocalHost attachment secrets in OpenCode Workspace configuration or environment variables.

## Tunnel and startup

Manage tunnel credentials outside OpenCode Workspace. A named-tunnel ingress file should target only RemoteBridge and end with a catchall 404:

```yaml
tunnel: <tunnel-uuid>
credentials-file: C:\Users\<user>\.cloudflared\<tunnel-uuid>.json

ingress:
  - hostname: <remote-hostname>
    service: http://127.0.0.1:38443
  - service: http_status:404
```

```powershell
cloudflared tunnel ingress validate
bin\local-host\OpenCode.Workspace.LocalHost.exe
Invoke-RestMethod http://127.0.0.1:43127/api/v1/health/live
Invoke-RestMethod http://127.0.0.1:43127/api/v1/health/ready
bin\remote-bridge\OpenCode.Workspace.RemoteBridge.exe
cloudflared tunnel --config C:\path\to\cloudflared-config.yml run <tunnel-name-or-uuid>
```

If LocalHost intentionally uses another loopback port, update `LocalHostBaseUrl` before starting RemoteBridge.

## Security and acceptance

- `PublicOrigin` is authoritative; Host and browser Origin must exactly match its HTTPS origin.
- RemoteBridge cryptographically validates `Cf-Access-Jwt-Assertion`; Cloudflare reaching the bridge is not sufficient authorization.
- The route allowlist exposes only session inspection, attachment grants, terminal assets, and the terminal WebSocket. It is not a LocalHost proxy.
- An attachment grant is identity-bound, one-time, short-lived, and tied to session, runtime, and takeover choice.
- LocalHost credentials stay server-side between RemoteBridge and LocalHost.
- The remote browser stores only the last output sequence, not grants or LocalHost credentials.

Validate with an allowed and denied identity. Exercise attach, input, binary output, resize, explicit takeover, reconnect replay or explicit gap, and disconnect without runtime stop. Confirm local browser and Windows Terminal behavior remains unchanged and that no workspace administration or MCP route is reachable.

Troubleshoot boundaries in order: LocalHost health, RemoteBridge configuration/listener, tunnel ingress, Access policy, JWT/JWKS, exact Host/Origin, WebSocket forwarding, canonical session existence, then takeover/replay. Never troubleshoot by using `0.0.0.0`, opening firewall ports, bypassing Access/JWT checks, weakening the allowlist, or tunneling LocalHost.

A real Cloudflare smoke is optional, manual, and not a CI requirement. Record `PASS`, `FAIL`, or `NOT RUN` with date, package version/commit, platform, redacted hostname, policy type, and exercised steps. Never record account credentials, assertions, or tokens.
