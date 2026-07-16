# Local MCP

OpenCode Workspace ships a local-only stdio MCP host in the extracted release distribution.

Windows packaged path:

```text
C:\Tools\OpenCodeWorkspace\bin\mcp\opencode-workspace-mcp.exe
```

Linux or macOS packaged path:

```text
~/tools/opencode-workspace/bin/mcp/opencode-workspace-mcp
```

Long-running tools such as `run_smoke`, `run_smoke_matrix`, and `provision_workspace` return an operation immediately.

- receiving an operation id does not mean success
- poll `get_operation`
- send `afterSequence` to receive only new progress events
- terminal states are `Succeeded`, `Failed`, and `Cancelled`
- cleanup may continue briefly after cancellation
- detailed logs are exposed through operation artifact references

Provisioning flow:

```text
1. call create_workspace
2. call provision_workspace
3. retain operationId
4. call get_operation with afterSequence
5. continue polling until completed, failed, or cancelled
6. inspect validate_workspace, workspace resources, and artifact references
```

Example polling pattern:

```json
{
  "operationId": "<id>",
  "afterSequence": 42
}
```

Oracle, APEX, and APEXlang setup can take a substantial amount of time. A short interval without a new event does not necessarily indicate failure while the local runtime is still provisioning or validating.

OpenCode example:

```json
{
  "mcpServers": {
    "opencode-workspace": {
      "command": "C:\\Tools\\OpenCodeWorkspace\\bin\\mcp\\opencode-workspace-mcp.exe"
    }
  }
}
```

Claude Code example:

```json
{
  "mcpServers": {
    "opencode-workspace": {
      "command": "~/tools/opencode-workspace/bin/mcp/opencode-workspace-mcp"
    }
  }
}
```

Codex example:

```json
{
  "mcpServers": {
    "opencode-workspace": {
      "command": "~/tools/opencode-workspace/bin/mcp/opencode-workspace-mcp"
    }
  }
}
```
