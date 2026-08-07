# Local MCP

The local MCP host is included in the normal OpenCode Workspace release archive.

OpenCode Workspace ships a local-only stdio MCP host for AI clients that support the Model Context Protocol.

- local-only
- stdio transport
- single-user trust model
- no OAuth for the packaged local host
- no HTTP listener
- no public network exposure

The packaged MCP host can:

- list templates and smoke definitions
- create, provision, validate, stop, and inspect workspaces
- run smoke validation
- inspect runtime diagnostics
- retrieve workspace and smoke artifacts
- report incremental progress for long-running operations

Long-running tools return an operation record immediately.

- an operation ID does not mean completion
- poll `get_operation`
- use `afterSequence` to receive only new events
- inspect final result, cleanup state, and artifact references

## Release package

The published release archive includes the MCP host with the rest of the supported local entry points.

Example layout:

```text
opencode-workspace-<version>-<rid>/
  bin/
    cli/
    local-host/
    mcp/
  catalog/
  config/
  docs/
```

Packaged MCP executable paths:

| Platform | MCP executable |
| -------- | -------------- |
| Windows | `bin\mcp\OpenCode.Workspace.Mcp.exe` |
| Linux | `bin/mcp/OpenCode.Workspace.Mcp` |
| macOS | `bin/mcp/OpenCode.Workspace.Mcp` |

Example extracted installation locations:

- Windows: `C:\Tools\OpenCode Workspace\`
- Linux: `/home/<user>/tools/opencode-workspace/`
- macOS: `/Users/<user>/tools/opencode-workspace/`

Example absolute MCP executable paths:

- Windows: `C:\Tools\OpenCode Workspace\bin\mcp\OpenCode.Workspace.Mcp.exe`
- Linux: `/home/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp`
- macOS: `/Users/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp`

The Windows package is self-contained and already includes the required .NET runtime files, catalog, and packaged configuration files. No source checkout, Visual Studio, separately installed .NET runtime, or build is required.

## Common installation steps

1. Download the archive for your operating system from the GitHub release.
2. Verify the checksum, if the release publishes one.
3. Extract the archive to a stable location.
4. On Linux or macOS, make sure the MCP executable has execute permission if required.
5. Configure your AI client with the absolute path to the packaged MCP executable.
6. Restart or reload the client.
7. Verify that the MCP server and its tools are visible.

The packaged distribution is the supported end-user installation path for local MCP integration.

Generate configuration from the extracted package instead of editing example paths:

```powershell
bin\cli\OpenCode.Workspace.Cli.exe mcp configure codex --install-root "C:\path\to\extracted-package"
bin\cli\OpenCode.Workspace.Cli.exe mcp configure claude --install-root "C:\path\to\extracted-package"
bin\cli\OpenCode.Workspace.Cli.exe mcp configure opencode --install-root "C:\path\to\extracted-package" --output "$HOME\.config\opencode\opencode-mcp.json"
bin\cli\OpenCode.Workspace.Cli.exe mcp doctor --install-root "C:\path\to\extracted-package" --json
```

`mcp configure` prints copy-ready configuration unless `--output` is supplied. It never overwrites an existing file without `--force`.

Do not point clients at:

- `dotnet run`
- `src/OpenCode.Workspace.Mcp`
- `bin/Debug`
- `bin/Release`
- repository checkout paths

## Codex

Codex supports local stdio MCP servers through both `codex mcp add` and `~/.codex/config.toml`.

Recommended path: `~/.codex/config.toml`

Windows example:

```toml
[mcp_servers.opencode_workspace]
command = "C:\\Tools\\OpenCode Workspace\\bin\\mcp\\OpenCode.Workspace.Mcp.exe"
startup_timeout_sec = 60
tool_timeout_sec = 14400
enabled = true
required = true
```

Linux example:

```toml
[mcp_servers.opencode_workspace]
command = "/home/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp"
startup_timeout_sec = 60
tool_timeout_sec = 14400
enabled = true
required = true
```

macOS example:

```toml
[mcp_servers.opencode_workspace]
command = "/Users/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp"
startup_timeout_sec = 60
tool_timeout_sec = 14400
enabled = true
required = true
```

Alternative CLI registration:

```bash
codex mcp add opencode_workspace -- "/home/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp"
```

Windows PowerShell:

```powershell
codex mcp add opencode_workspace -- "C:\Tools\OpenCode Workspace\bin\mcp\OpenCode.Workspace.Mcp.exe"
```

Verification:

```text
codex mcp list
```

Notes:

- `startup_timeout_sec = 60` gives the packaged MCP host time to start and load its packaged catalog.
- `tool_timeout_sec = 14400` allows long-running Oracle, APEX, and APEXlang operations to continue without the client timing out too early.
- long-running tools still return an operation record immediately; Codex should poll `get_operation`.

## Claude Code

Claude Code supports local stdio MCP servers through the supported CLI.

Recommended scope: `--scope user`

Use user scope for a normal extracted OpenCode Workspace installation that you want available across projects.

Windows PowerShell:

```powershell
claude mcp add --scope user --transport stdio opencode-workspace -- `
  "C:\Tools\OpenCode Workspace\bin\mcp\OpenCode.Workspace.Mcp.exe"
```

Linux:

```bash
claude mcp add --scope user --transport stdio opencode-workspace -- \
  /home/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp
```

macOS:

```bash
claude mcp add --scope user --transport stdio opencode-workspace -- \
  /Users/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp
```

Verification:

```text
claude mcp list
```

Update or remove the server with supported commands:

```text
claude mcp remove opencode-workspace
```

Notes:

- Claude Code starts and stops the packaged MCP process automatically.
- configure the absolute executable path
- do not point Claude Code at repository output or `dotnet run`
- if your Claude Code environment uses a strict tool timeout, set it high enough for Oracle or APEX provisioning

## OpenCode

OpenCode supports local MCP servers through `opencode.json` or `opencode.jsonc`.

Recommended path: global config in `~/.config/opencode/opencode.json`

Windows example:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "opencode_workspace": {
      "type": "local",
      "command": [
        "C:\\Tools\\OpenCode Workspace\\bin\\mcp\\OpenCode.Workspace.Mcp.exe"
      ],
      "enabled": true,
      "timeout": 14400000
    }
  }
}
```

Linux example:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "opencode_workspace": {
      "type": "local",
      "command": [
        "/home/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp"
      ],
      "enabled": true,
      "timeout": 14400000
    }
  }
}
```

macOS example:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "opencode_workspace": {
      "type": "local",
      "command": [
        "/Users/<user>/tools/opencode-workspace/bin/mcp/OpenCode.Workspace.Mcp"
      ],
      "enabled": true,
      "timeout": 14400000
    }
  }
}
```

Verification:

```text
opencode mcp list
```

Notes:

- OpenCode documents file-based MCP configuration as the supported setup path.
- the `timeout` value is in milliseconds
- the packaged MCP host normally does not require custom environment variables
- the packaged distribution already contains the catalog and runtime defaults it needs

## Client configuration notes

For Codex, Claude Code, and OpenCode:

- use an absolute executable path
- do not configure `dotnet run`
- do not point at repository outputs
- do not rely on the current working directory
- no environment variables are normally required for the packaged distribution
- the packaged MCP host resolves its own packaged catalog and configuration
- MCP logs go to stderr
- stdout is reserved for MCP protocol messages
- the client starts and stops the MCP process automatically

## First verification prompt

Use this read-only prompt first:

```text
Use the OpenCode Workspace MCP server.

List the available workspace templates and summarize the
oracle-apexlang-demo template.

Do not create or provision anything yet.
```

Successful execution proves:

- the MCP host launched
- tool discovery worked
- the packaged catalog was found
- the client can invoke OpenCode Workspace tools

## First provisioning prompt

Use this prompt when you want the client to drive provisioning through the packaged MCP host:

```text
Use the OpenCode Workspace MCP tools as the primary interface.

Create an oracle-apexlang-demo workspace named apex-agent-demo.

Start provisioning it and retain the returned operationId.
Poll get_operation using afterSequence until the operation reaches
completed, failed, or cancelled.

While it runs, summarize meaningful new progress events without
starting another provisioning operation.

After completion:

- validate the workspace
- report Oracle status
- report the selected Oracle image
- report XDB status
- report the APEX version and registry status
- report ORDS status
- report available service URLs
- list relevant workspace documentation and artifacts

Do not run Docker Compose manually.
Do not start duplicate provisioning.
Do not delete unrelated Docker resources.
```

If your workflow requires a custom destination root, provide it explicitly when calling `create_workspace`. Otherwise use the default packaged workflow and let the MCP tool manage the workspace in its configured location.

## Long-running operations

### Durable mutation contract

Every MCP mutation returns a durable operation immediately. This applies to `prepare_workspace`, `start_workspace`, `stop_workspace`, `recover_workspace`, `remove_workspace_runtime`, `provision_workspace`, `run_smoke`, and `cancel_operation`. The immediate payload is an `McpOperationModel` with a non-empty operation id, initial queued or running status, phase, timestamps, controller attribution, and artifact references. Call `get_operation` with `afterSequence` until a terminal status is returned; the structured domain result is then in `result`.

| Tool | Operation kind | Terminal result | Cancellation |
| --- | --- | --- | --- |
| `prepare_workspace` | `prepare` | workspace lifecycle result | `cancel_operation` |
| `start_workspace` | `start` | workspace lifecycle result | `cancel_operation` |
| `stop_workspace` | `stop` | workspace lifecycle result | `cancel_operation` |
| `recover_workspace` | `recover` | workspace lifecycle result | `cancel_operation` |
| `remove_workspace_runtime` | `reset-runtime` | workspace lifecycle result | `cancel_operation` |
| `provision_workspace` | `provision` | workspace lifecycle result | `cancel_operation` |
| `run_smoke` | `run_smoke` | `WorkspaceSmokeResult` | `cancel_operation` |
| `cancel_operation` | existing operation | updated operation record | marks cancellation requested; terminal cleanup remains visible through polling |

Canonical flow:

1. call a tool such as `provision_workspace` or `run_smoke`
2. retain `operationId`
3. retain `lastEventSequence`
4. call `get_operation` with `afterSequence`
5. process only newly returned events
6. continue until the status is `Succeeded`, `Failed`, or `Cancelled`
7. inspect the final result, cleanup state, and artifact references

Compact example:

```json
{
  "operationId": "...",
  "status": "Running",
  "currentPhase": "provisioning",
  "progressMessage": "Installing Oracle APEX 26.1.0",
  "lastEventSequence": 24,
  "recentEvents": [
    {
      "sequence": 24,
      "phase": "provisioning",
      "message": "Installing Oracle APEX 26.1.0"
    }
  ]
}
```

Important notes:

- an operation ID does not mean completion
- an empty set of new events may be normal while the operation is still running
- Oracle, APEX, and APEXlang setup may take significant time
- agents must not start a second operation merely because no new event appeared briefly
- detailed logs remain available through artifact references

## Controller sessions and LocalHost

Each stdio MCP process registers a distinct controller session with LocalHost. The session records safe client metadata and attributes operations, but it does not own workspace lifetime. Closing Codex, Claude Code, or OpenCode does not cancel an operation, stop LocalHost, or delete any workspace or interactive session.

MCP clients and Avalonia observe the same canonical LocalHost operation records. Any controller may inspect or request cancellation of a permitted operation; original controller attribution remains unchanged. Restarting an MCP client creates a new controller session and does not duplicate an existing operation.

MCP controller sessions are intentionally separate from interactive OpenCode sessions used by Windows Terminal. MCP does not attach to, stream, take over, or expose the terminal conversation.

The MCP executable discovers `bin/local-host/OpenCode.Workspace.LocalHost.exe` from its own extracted installation root. LocalHost binds loopback only and uses a descriptor with a dynamic loopback port. Stale descriptors are discarded and stdout remains MCP JSON-RPC only; diagnostics are written to stderr or LocalHost logs.

## Main capabilities

### Templates

- list templates
- inspect template details
- inspect smoke definitions

### Workspace lifecycle

- create workspaces
- provision workspaces
- validate workspaces
- stop workspaces
- remove disposable runtime resources

### Runtime diagnostics

- list owned runtime resources
- run runtime doctor
- inspect services and readiness

### Smoke validation

- run one template
- run a family or matrix
- inspect progress
- clean smoke-owned resources safely

### Operations

- poll progress
- inspect phase history
- cancel long-running operations

### Artifacts

- list workspace and smoke artifacts
- retrieve summaries and logs
- process supported Excel artifacts

## Local trust and security

The packaged local MCP host is intended for a single-user developer machine.

- local-only
- stdio-only
- no network listener
- no authentication layer on the packaged local host
- can manage local workspaces and labeled Docker resources
- install only trusted release binaries
- remote or server deployment is not supported by this setup

Normal cleanup targets explicitly owned and labeled resources. It is not intended to remove unrelated Docker resources.

## Troubleshooting

### MCP executable not found

- verify the extracted path
- use an absolute path
- confirm you downloaded the correct OS or RID archive
- on Linux or macOS, verify the executable bit if required

### MCP transport closes immediately

- launch the packaged MCP executable only to inspect stderr
- verify the full release archive was extracted
- ensure no wrapper script writes to stdout
- verify `catalog/` and packaged configuration files are present

### Tools are not visible

- restart or reload the AI client
- run the client MCP list or status command
- verify the configuration syntax
- increase startup timeout if needed

### Operation appears stuck

- call `get_operation`
- use `afterSequence`
- inspect `currentPhase`
- retrieve operation progress artifacts
- run runtime doctor if the operation failed
- use `cancel_operation` when appropriate

### Oracle or APEX provisioning fails

- inspect the operation result
- retrieve provisioning and progress artifacts
- run `run_runtime_doctor`
- check Docker memory and disk
- do not manually start another Oracle environment in parallel

### Cleanup

- use MCP cleanup or CLI smoke cleanup
- do not use `docker system prune` as the normal solution
- do not delete unlabeled resources as a first step

## Further reading

- Codex MCP docs: <https://developers.openai.com/codex/extend/mcp>
- Codex config reference: <https://developers.openai.com/codex/config-file/config-reference>
- Claude Code MCP docs: <https://docs.anthropic.com/en/docs/claude-code/mcp>
- OpenCode config docs: <https://opencode.ai/docs/config/>
- OpenCode MCP server docs: <https://opencode.ai/docs/mcp-servers/>
