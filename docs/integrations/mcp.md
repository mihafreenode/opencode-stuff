# Local MCP integration

This is the authoritative setup and architecture guide for the packaged OpenCode Workspace MCP server.

## Boundary

`OpenCode.Workspace.Mcp` is a local, single-user stdio adapter. An MCP client starts `bin/mcp/OpenCode.Workspace.Mcp` (`.exe` on Windows), which discovers or starts the packaged `bin/local-host/OpenCode.Workspace.LocalHost` process. LocalHost owns the canonical workspace inventory, controller sessions, and durable operation records.

- stdout contains MCP protocol frames only; do not add banners, logs, or wrapper output
- MCP has no HTTP listener, remote deployment, authentication boundary, or public exposure
- MCP has no terminal or PTY API and cannot attach to, stream, resize, or take over an interactive terminal
- MCP exposes workspace lifecycle and provisioning capabilities, but no Oracle discovery, synchronization, or Oracle Assistant tools
- RemoteBridge and Cloudflare do not expose MCP

The host reads a healthy LocalHost descriptor when one exists. Otherwise, startup is coordinated by a state-root startup lock, stale descriptors are discarded, and one client starts LocalHost on a dynamic loopback port. The executable is resolved from the extracted distribution, normally `bin/local-host/`. LocalHost remains the shared control plane even when several desktop or MCP clients are active.

## Package layout

```text
<install-root>/
  bin/
    cli/OpenCode.Workspace.Cli[.exe]
    local-host/OpenCode.Workspace.LocalHost[.exe]
    mcp/OpenCode.Workspace.Mcp[.exe]
  catalog/
  config/mcp/appsettings.json
```

Use an absolute path under a stable extracted package. Do not configure `dotnet run`, a source project, repository build output, or a path that depends on the client's working directory.

## Configure clients

Run these commands from the extracted package root on Windows:

```powershell
bin\cli\OpenCode.Workspace.Cli.exe mcp configure codex --install-root "C:\path\to\extracted-package"
bin\cli\OpenCode.Workspace.Cli.exe mcp configure claude --install-root "C:\path\to\extracted-package"
bin\cli\OpenCode.Workspace.Cli.exe mcp configure opencode --install-root "C:\path\to\extracted-package" --output "$HOME\.config\opencode\opencode-mcp.json"
bin\cli\OpenCode.Workspace.Cli.exe mcp doctor --install-root "C:\path\to\extracted-package" --json
```

On Linux or macOS, use the same arguments with `bin/cli/OpenCode.Workspace.Cli` and POSIX paths:

```bash
bin/cli/OpenCode.Workspace.Cli mcp configure codex --install-root "/path/to/extracted-package"
bin/cli/OpenCode.Workspace.Cli mcp configure claude --install-root "/path/to/extracted-package"
bin/cli/OpenCode.Workspace.Cli mcp configure opencode --install-root "/path/to/extracted-package" --output "$HOME/.config/opencode/opencode-mcp.json"
bin/cli/OpenCode.Workspace.Cli mcp doctor --install-root "/path/to/extracted-package" --json
```

`mcp configure` prints copy-ready Codex TOML, a Claude registration command, or OpenCode JSON. `--output` writes a file atomically and refuses to replace an existing file unless `--force` is supplied. The generated defaults use a 60-second startup timeout where supported and a four-hour tool timeout.

`mcp doctor` checks the package layout, writable state root, descriptor discovery, LocalHost health/readiness, MCP initialization, tools/resources, stdout framing, controller registration/disconnection, and cleanup of a doctor-owned LocalHost. Use `--state-root <path>` to test an existing state root.

## Controller and multi-client behavior

Each stdio process creates a distinct controller session and registers safe client metadata with LocalHost. Operations carry the initiating controller attribution, but a controller does not own workspace lifetime.

- Codex, Claude Code, OpenCode, and Avalonia can observe the same LocalHost operations
- closing or restarting one MCP client does not cancel an operation or stop a workspace
- restarting a client creates a new controller session; it does not duplicate an existing operation
- a permitted client may inspect or request cancellation of an operation started by another client
- controller sessions are separate from interactive agent sessions and terminal attachment leases

When an MCP process started its own LocalHost sidecar, orderly stdio shutdown releases that ownership connection. Durable workspace state and operation records remain LocalHost concerns, not MCP-process memory.

## Canonical operations

Workspace preparation, start, provisioning, recovery, stop, runtime removal, smoke runs, and smoke matrices are started through LocalHost and return an operation record immediately. An operation ID is not completion. Other MCP mutations are not necessarily durable-operation starters; use protocol discovery and each tool result as the exact contract.

1. Retain `operationId` and `lastEventSequence`.
2. Poll `get_operation` with `afterSequence`.
3. Process only newly returned events.
4. Stop at `Succeeded`, `Failed`, or `Cancelled`.
5. Inspect the final structured result, cancellation state, cleanup result, and artifact references.

MCP-visible operation status values are `Pending`, `Running`, `Succeeded`, `Failed`, and `Cancelled`. Canonical LocalHost additionally has `Interrupted`, which the current MCP compatibility mapper projects as `Failed`. MCP exposes LocalHost cancellation state as `cancellationRequested: true` when the canonical state is `Requested` or `Cancelled`. Brief periods with no new events are normal and must not trigger a duplicate operation.

## Tools and resources

Use protocol discovery as the exact contract instead of copying a static exhaustive list. Current tool groups cover template/smoke discovery, workspace inventory and lifecycle, owned runtime diagnostics, smoke execution and cleanup, operation polling/cancellation, constrained artifact access, and Excel artifact processing. Stable examples include `list_workspace_templates`, `create_workspace`, `provision_workspace`, `run_smoke`, `get_operation`, and `cancel_operation`.

Resources expose server health, workspace and operation inventories/snapshots, template details, runtime inventory/doctor output, smoke summaries, and validated artifacts under `opencode://...` URIs. Artifact access is constrained to configured workspace and smoke roots; path traversal and arbitrary filesystem browsing are not exposed.

Oracle discovery, synchronization, and Oracle Assistant operations are intentionally not part of the packaged MCP product surface. Packaged Oracle-specific verification uses `LocalClient` against the canonical `LocalHost` routes.

## LocalHost routing

MCP routes its supported workspace lifecycle, provisioning, smoke, operation, runtime inventory/doctor, artifact, cleanup, and Excel capabilities through LocalHost. There is no supported in-process fallback for these paths.

## Security and troubleshooting

Install only trusted release binaries. The local MCP host can create workspaces and mutate explicitly owned local runtime resources under the current OS user.

- If startup fails, verify the complete package was extracted and both `bin/mcp` and `bin/local-host` exist.
- If framing fails, remove any wrapper output from stdout; diagnostics must use stderr or LocalHost logs.
- If tools are absent, reload the client and run `mcp doctor` from the same installation.
- If an operation appears stuck, poll with `afterSequence`, inspect its phase and artifacts, then request cancellation if appropriate.
- Do not use `docker system prune` as MCP cleanup; use ownership-aware cleanup operations.
