# Configuration

Configuration exists at different ownership levels. Use the narrowest appropriate level and do not put secrets in repository-owned files.

## Workspace Configuration

`workspace.yaml` is the portable source of truth for lasting workspace intent. Existing repositories may instead use `workspace.yml`, `.opencode/profile.yaml`, or `.opencode/profile.yml`; the app preserves the discovered path.

Use it for workspace identity, base image, provider metadata, runtime selection, catalog selections, terminal preferences, agent profile, analytics settings, and optional integration configuration. See [Workspace YAML](workspace-yaml.md).

Do not use it for passwords, tokens, private keys, local container IDs, absolute machine-specific state, or operation timestamps.

## Generated Workspace Configuration

The app generates `compose.yaml`, `.env`, `mounts/config/*`, and `.opencode/local/runtime-state.yaml`. These are implementation or machine-local state. Regenerate them after changing `workspace.yaml`; do not treat manual edits as durable configuration.

## Packaged Host Defaults

The package contains:

- `config/api/appsettings.json`: LocalHost compatibility-named defaults.
- `config/mcp/appsettings.json`: stdio MCP defaults.
- `config/remote-bridge/appsettings.json`: RemoteBridge and Cloudflare defaults.

LocalHost and MCP default to stdio/loopback behavior; MCP HTTP is disabled. RemoteBridge defaults to `RemoteAccess:Enabled=false`, a loopback listener, and empty Cloudflare identity values.

RemoteBridge also reads `%LOCALAPPDATA%\OpenCode.Workspace.Manager\remote-bridge\appsettings.json` as user configuration. Command-line or standard .NET configuration keys can override settings, for example `--RemoteAccess:Enabled=false`. Follow the dedicated [Remote Browser Terminal](../integrations/cloudflare-remote-access.md) guide before enabling it.

## LocalHost State Overrides

Packaged clients normally discover LocalHost using the default app-data state root. Advanced and test workflows can set:

- `localHost__stateRoot`: override LocalHost state root.
- `localHost__distributionRoot`: explicit extracted package root when starting LocalHost.
- `localHost__executableDirectory`: explicit LocalHost executable directory.
- `mcp__workspaceStateRoot`: MCP workspace state root passed to LocalHost.

Double underscores follow .NET environment-variable configuration conventions. These are advanced operational overrides, not normal per-workspace settings.

## Desktop Preferences

The desktop currently offers `System`, `Light`, and `Dark` theme modes and host/language detection. Workspace terminal preferences live in `workspace.yaml`; the app only manages its own Windows Terminal fragment and does not modify unrelated profiles.
