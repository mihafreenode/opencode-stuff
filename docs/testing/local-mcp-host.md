# Local MCP Host

`OpenCode.Workspace.Mcp` exposes local OpenCode Workspace capabilities through a local-only MCP server.

## Trust Model

- single-user local machine only
- default transport is `stdio`
- no authentication in v1
- no public listener in v1
- stdout is reserved for MCP protocol traffic
- logs go to stderr through `Microsoft.Extensions.Logging`

This trust model is suitable only for local agent integrations such as OpenCode, Codex, and Claude Code running under the same OS user.

## Project Structure

```text
OpenCode.Workspace.Core
          ↑
src/OpenCode.Workspace.Mcp
```

The MCP host references Core and AppSupport only.

It does not depend on:

- Avalonia UI
- CLI formatting or parsing
- OracleRuntimeSmoke executable logic

## Transport

Current transport:

- `stdio`

HTTP configuration is present but disabled by default. No loopback listener is started in v1.

## Contract

- `contractVersion: "1"`
- stable snake_case tool names
- camelCase structured fields
- ISO-8601 UTC timestamps
- enum strings are explicit and stable

Long-running operations are in-memory only and do not survive MCP process restart.

## Tools

Discovery:

- `list_workspace_templates`
- `get_workspace_template`
- `list_smoke_definitions`

Workspace:

- `list_workspaces`
- `get_workspace`
- `create_workspace`
- `provision_workspace`
- `validate_workspace`
- `stop_workspace`
- `remove_workspace_runtime`

Smoke:

- `run_smoke`
- `run_smoke_matrix`
- `list_smoke_resources`
- `cleanup_smoke_resources`

Runtime:

- `list_runtime_resources`
- `run_runtime_doctor`

Operations:

- `get_operation`
- `list_operations`
- `cancel_operation`

Artifacts:

- `list_workspace_artifacts`
- `get_workspace_artifact`
- `list_smoke_artifacts`
- `get_smoke_artifact`

Excel workflow:

- `process_excel_artifact`

## Resources

- `opencode://server/health`
- `opencode://templates/<template-id>`
- `opencode://workspaces/<workspace-id>`
- `opencode://operations/<operation-id>`
- `opencode://smoke/<run-id>/summary`
- `opencode://smoke-matrices/<matrix-run-id>/summary`
- `opencode://runtime/inventory`
- `opencode://artifacts/<artifact-id>`

## Operations

Long-running operation fields:

- `contractVersion`
- `operationId`
- `operationResourceUri`
- `kind`
- `status`
- `createdUtc`
- `startedUtc`
- `completedUtc`
- `currentPhase`
- `phaseHistory`
- `progressMessage`
- `workspaceId`
- `smokeRunId`
- `smokeMatrixRunId`
- `artifactDirectory`
- `failureClassification`
- `failureMessage`
- `cleanupFailureClassification`
- `cleanupFailureMessage`
- `cancellationRequested`

Operations are in-memory only.

Observed smoke phases from protocol validation:

- `queued`
- `preflightCleanup`
- `creatingWorkspace`
- `provisioning`
- `validating`
- `cleaningUp`
- `verifyingCleanup`
- `completed`

## Cancellation

- MCP cancellation requests flow through operation-specific cancellation tokens
- provision and smoke operations keep Core cleanup behavior
- cancelled status remains distinct from cleanup warnings
- forced process termination may leave owned resources, but later preflight cleanup can recover them

## Artifact Access Policy

- workspace artifact access is limited to the selected workspace `artifacts/` root
- smoke artifact access is limited to the configured smoke artifact root
- path traversal is rejected
- arbitrary filesystem reads are not exposed
- text files under the configured max size are returned inline
- large or binary files are returned through validated metadata and resource URIs

## Excel Round-Trip

`process_excel_artifact`:

1. validates the source `.xlsx`
2. preserves the source workbook
3. copies it to an allowed output root
4. adds an `OpenCode Result` worksheet
5. returns output metadata, checksums, resource URI, and diagnostics

This is a deterministic local workflow proof only.

## Proven Flow

1. start the host with `dotnet run --project src/OpenCode.Workspace.Mcp`
2. connect an MCP client over stdio and call `list_workspace_templates`
3. start a lightweight smoke run with `run_smoke` for `empty-workspace`
4. poll `get_operation` until the operation reaches a terminal state
5. read `opencode://smoke/<run-id>/summary`
6. list smoke artifacts with `list_smoke_artifacts`
7. start another smoke run and cancel it with `cancel_operation`
8. confirm `list_smoke_resources` and `run_runtime_doctor` return no smoke findings afterward
9. process an `.xlsx` file with `process_excel_artifact`
10. read the returned workbook through its validated artifact resource URI

Notes:

- operations are in-memory only
- restarting the MCP host loses operation records
- smoke and workspace artifacts remain on disk
- abandoned smoke runtimes remain recoverable through the existing preflight cleanup path
- stdio stdout must never contain logs or banners

## Configuration

`src/OpenCode.Workspace.Mcp/appsettings.json`:

```json
{
  "mcp": {
    "transport": "stdio",
    "http": {
      "enabled": false,
      "host": "127.0.0.1",
      "port": 0
    },
    "operations": {
      "cleanupTimeout": "00:05:00",
      "retention": "01:00:00"
    },
    "artifacts": {
      "maxReadBytes": 10485760
    }
  }
}
```

## Agent Setup

Windows PowerShell:

```text
dotnet run --project src/OpenCode.Workspace.Mcp
```

WSL or Linux/macOS:

```text
dotnet run --project src/OpenCode.Workspace.Mcp
```

OpenCode example:

```json
{
  "mcpServers": {
    "opencode-workspace": {
      "command": "dotnet",
      "args": ["run", "--project", "src/OpenCode.Workspace.Mcp"]
    }
  }
}
```

Claude Code example:

```json
{
  "mcpServers": {
    "opencode-workspace": {
      "command": "dotnet",
      "args": ["run", "--project", "src/OpenCode.Workspace.Mcp"]
    }
  }
}
```

Codex example:

```json
{
  "mcpServers": {
    "opencode-workspace": {
      "command": "dotnet",
      "args": ["run", "--project", "src/OpenCode.Workspace.Mcp"]
    }
  }
}
```

## Limitations

- local-only
- stdio-only in v1
- no durable operation history across process restarts
- no arbitrary Docker control
- no arbitrary filesystem browsing
- no workspace directory deletion tool in v1
- no remote auth or multi-user trust boundary in v1
