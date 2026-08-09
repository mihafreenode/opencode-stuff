# CLI Reference

The packaged CLI is an advanced diagnostics and automation tool. The package does not install an `opencode` command shim.

Always invoke the packaged executable directly:

| Platform | Executable |
| --- | --- |
| Windows | `bin\cli\OpenCode.Workspace.Cli.exe` |
| Linux/macOS | `bin/cli/OpenCode.Workspace.Cli` |

The examples below use the Windows path. Replace it with `bin/cli/OpenCode.Workspace.Cli` on Linux or macOS.

## Current Help Surface

```powershell
bin\cli\OpenCode.Workspace.Cli.exe doctor
bin\cli\OpenCode.Workspace.Cli.exe doctor --workspace <path>
bin\cli\OpenCode.Workspace.Cli.exe mcp configure <codex|claude|opencode> [--output <path>] [--install-root <path>] [--force]
bin\cli\OpenCode.Workspace.Cli.exe mcp doctor [--install-root <path>] [--json]
bin\cli\OpenCode.Workspace.Cli.exe debug-workspace-discovery
bin\cli\OpenCode.Workspace.Cli.exe runtime list --format json
bin\cli\OpenCode.Workspace.Cli.exe runtime doctor --owner smoke
bin\cli\OpenCode.Workspace.Cli.exe smoke list
bin\cli\OpenCode.Workspace.Cli.exe smoke run <template>
bin\cli\OpenCode.Workspace.Cli.exe smoke run --family <family>
bin\cli\OpenCode.Workspace.Cli.exe smoke run --all
bin\cli\OpenCode.Workspace.Cli.exe smoke cleanup --dry-run
bin\cli\OpenCode.Workspace.Cli.exe smoke cleanup --all
bin\cli\OpenCode.Workspace.Cli.exe smoke cleanup --run-id <run-id>
bin\cli\OpenCode.Workspace.Cli.exe smoke cleanup --format json
bin\cli\OpenCode.Workspace.Cli.exe validate-platform --target linux/amd64
bin\cli\OpenCode.Workspace.Cli.exe validate-platform --target linux/arm64
bin\cli\OpenCode.Workspace.Cli.exe validate-platform --workspace <path> --target linux/arm64
bin\cli\OpenCode.Workspace.Cli.exe validate-platform --target linux/arm64 --output report.md
bin\cli\OpenCode.Workspace.Cli.exe --help
```

The built-in help currently prints these examples with the historical display name `opencode`. That is help text only; no `opencode` shim is included in the release.

## Commands

### `doctor`

Diagnoses host platform, Docker, workspace configuration, local runtime state, and runtime resolution. `--workspace` defaults to the current directory. A completed diagnostic currently exits `0` even when individual diagnostic facts report problems; command parsing or execution exceptions use the general error behavior below.

### `validate-platform`

Requires `--target`. Supports `linux/amd64` and `linux/arm64` validation paths and optional `--workspace` and Markdown `--output`. Exits `0` when the report succeeds and `1` when validation fails.

### `debug-workspace-discovery`

Loads the app-data workspace index and performs runtime inspection. This is a diagnostic command, not an import command. It exits `0` after a completed report.

### `mcp configure`

Prints configuration for `codex`, `claude`, or `opencode`. `--output` writes instead of printing; an existing destination requires `--force`. `--install-root` defaults to the distribution inferred from the CLI location.

### `mcp doctor`

Checks the MCP and LocalHost executables, canonical package layout, writable state, LocalHost health/readiness, MCP protocol exchange, controller registration, and cleanup. `--json` selects JSON. Exits `0` only when every check is passed or skipped; otherwise `1`.

The implementation also accepts `--state-root` for an explicit doctor state directory, although this option is not listed by current help.

### `runtime list` And `runtime doctor`

Inspect owned runtime resources. Filters include `--owner`, `--run-id`, `--workspace`, and `--project`; output supports `--format text|json` plus `--quiet` or `--verbose`. A completed inventory currently exits `0`; evaluate the reported facts rather than treating that exit code as a health verdict.

### `smoke list`

Lists smoke-enabled template definitions, optionally filtered by `--family`, with text or JSON output.

### `smoke run`

Select exactly one template id, `--family <family>`, or `--all`. Additional options are `--parallel`, `--artifacts-root`, `--timeout`, `--keep-workspace`, `--keep-runtime-on-failure`, `--format text|json`, `--quiet`, and `--verbose`.

### `smoke cleanup`

Supports `--dry-run`, `--all`, `--run-id`, output formatting, verbosity, and the internal migration option `--legacy`. Without `--run-id`, cleanup includes all owned smoke resources.

### `interactive-session attach` (internal)

This presentation helper is launched with one-time attachment data by LocalHost/desktop session orchestration. It requires `--session-id`, `--attachment-id`, `--attachment-token`, and `--state-root`. It is not a user-facing attach command and should not be scripted as a stable public interface.

## Exit Behavior

| Exit code | Current behavior |
| --- | --- |
| `0` | Help, completed informational commands, successful validation/smoke/MCP checks; cancellation inside the internal attach helper also returns `0` |
| `1` | Failed platform validation, MCP doctor checks, or smoke validation/cleanup result |
| `2` | Unknown command/option or invalid arguments |
| `3` | Smoke cleanup failure |
| `4` | Smoke lock failure |
| `5` | Smoke resource exhaustion |
| `6` | Unsupported or empty smoke selection |
| `7` | Unhandled command/tooling failure or internal attach failure |
| `130` | Top-level command cancellation |

Smoke outcomes are mapped to the detailed `1` through `7` codes. Error messages are written to stderr; normal reports are written to stdout.
