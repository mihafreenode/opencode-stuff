# OpenCode Workspace Manager

OpenCode Workspace Manager creates durable development workspaces with replaceable Ubuntu runtimes.
Your repository, documentation, reports, and Save Points remain on disk while containers can be rebuilt.

The application is a workspace manager first. It keeps setup reproducible, makes runtime health visible,
and provides safer Git-backed working flows without making Docker or Git the center of the interface.

## Why Use It?

- Create a prepared workspace from a built-in template.
- Open an existing local Git checkout without replacing its configuration.
- Keep normal work away from protected branches in a Safe Working Copy.
- Rebuild disposable runtime infrastructure without treating the container as the durable asset.
- Record local progress with Save Points and stronger recovery snapshots with Checkpoints.
- Export a portable Backup or explicitly Publish to a configured Git remote.
- Start or resume an interactive OpenCode session after the workspace is ready.

## Platform Support

Windows is the primary supported desktop platform.

| Runtime identifier | Package | Desktop | Terminal attach |
| --- | --- | --- | --- |
| `win-x64` | Self-contained `opencode-workspace-<version>-win-x64.zip` | Primary supported path | Windows Terminal |
| `linux-x64` | Self-contained `opencode-workspace-<version>-linux-x64.tar.gz` | Available for evaluation | Not yet supported |
| `osx-arm64` | Self-contained `opencode-workspace-<version>-osx-arm64.tar.gz` | Available for evaluation | Not yet supported |

These are the only supported or evaluated release runtime identifiers. All archives extract with the
package files directly at the archive root and do not require a separately installed .NET runtime.
Interactive terminal attachment from the desktop application is currently Windows-only.

## Install On Windows

Prerequisites:

- Windows 10 or 11 with hardware virtualization
- WSL2
- Docker Desktop using its WSL2 backend
- Git
- Windows Terminal for interactive session attachment

The Windows release ZIP is self-contained. You do not need a separate .NET runtime, Visual Studio,
or a source checkout.

1. Download `opencode-workspace-<version>-win-x64.zip` and its adjacent verified `.sha256` checksum from the release.
2. Verify the checksum in PowerShell:

   ```powershell
   Get-FileHash .\opencode-workspace-<version>-win-x64.zip -Algorithm SHA256
   Get-Content .\opencode-workspace-<version>-win-x64.zip.sha256
   ```

3. Extract the entire ZIP to a stable folder.
4. Run `OpenCode.Workspace.exe` from the extracted folder.

Do not move only the executable. It uses the packaged `bin`, `catalog`, `config`, `docs`, and
`Localization` directories beside it.

Every package includes `release-manifest.json` at its root with the version, Git commit, build timestamp,
runtime identifier, and self-contained status. See [Package Layout](docs/reference/package-layout.md).

There is currently no installer or MSIX package. See [Getting Started](docs/getting-started.md) for
prerequisite checks and the complete first workflow.

## Create A Workspace

1. Start `OpenCode.Workspace.exe`.
2. Select `Create Workspace`.
3. Choose a name, location, and template.
4. Create the workspace.
5. Select it in the workspace list.
6. Choose `Open Workspace`.

`Open Workspace` prepares or starts the runtime and validates readiness. It does **not** automatically
attach a terminal. After the workspace is ready, create, select, or resume an interactive session and
attach that session separately.

## Open An Existing Repository

Use `Open Existing Repository` for a local Git checkout. The app inspects the checkout and recognizes:

- `workspace.yaml`
- `workspace.yml`
- `.opencode/profile.yaml`
- `.opencode/profile.yml`

Repository-owned configuration remains canonical. Import does not silently replace it with template
defaults. If no supported file exists, the app can create workspace configuration as part of onboarding.

For normal work, create a Safe Working Copy instead of changing `main`, `master`, `release/*`, or another
protected branch directly. The UI calls this a Working Copy; Git implements it as a local branch.

See [Workspaces](docs/user/workspaces.md) for creation, import, status, lifecycle, and current boundaries.

## Open, Then Attach

Workspace readiness and interactive access are intentionally separate:

```text
Create or import workspace
        |
Open Workspace
        |
Generate, provision, start, and validate runtime
        |
Create or select an interactive session
        |
Attach its presentation in Windows Terminal
```

An interactive session connects a workspace to a provider conversation and a terminal runtime. Closing
or detaching a presentation does not mean the durable workspace was deleted. A later presentation can
reconnect to the same session when it remains recoverable.

Use takeover only when another presentation currently owns the session and you intentionally want to
transfer control. Restart the provider when the conversation process itself must be recreated; rebuilding
the workspace runtime is a different operation.

See [Sessions](docs/user/sessions.md).

## Windows Terminal And Browser Views

Windows Terminal and the local browser terminal are presentations of LocalHost-managed interactive
sessions. They are not separate workspaces and should not be treated as independent copies of the work.

- Windows Terminal is the current desktop attach path on Windows.
- The local browser terminal is loopback-only and uses the same local session service.
- Closing a presentation is not the same as stopping or deleting a workspace.
- Remote browser access is a separate, opt-in RemoteBridge deployment and is disabled by default.

Setup details:

- [Local browser terminal](docs/integrations/local-browser-terminal.md)
- [Remote browser terminal through Cloudflare](docs/integrations/cloudflare-remote-access.md)

## Protect And Share Work

| Action | Main purpose | Leaves this machine? |
| --- | --- | --- |
| Save Point | Record meaningful local progress in Git | No |
| Checkpoint | Capture extra local recovery material | No |
| Backup | Export a portable archive | Only where you save/copy it |
| Publish | Send the current Working Copy to its configured remote | Yes |

Publish is always explicit. The app fetches remote state first and stops for conflicts, uncertainty, or
protected-branch risk. It does not force-push or automatically resolve conflicts.

Before Save Point creation, changed and untracked content is checked for likely secrets, unknown hidden
content, and dangerous ignore rules. Reports and deliverables are durable work; caches and rebuildable
dependencies are not.

See [Backup And Publish](docs/user/backup-and-publish.md).

## Workspace Configuration

`workspace.yaml` is the portable source of truth for lasting workspace intent. Generated files such as
`compose.yaml`, `.env`, and scripts under `mounts/config/` are replaceable implementation details.

Machine-local runtime resolution is kept under `.opencode/local/` and is ignored by Git. Do not put
passwords, tokens, private keys, or machine-specific runtime identifiers in `workspace.yaml`.

Reference:

- [Workspace YAML](docs/reference/workspace-yaml.md)
- [Catalogs](docs/reference/catalogs.md)
- [Configuration](docs/reference/configuration.md)
- [Paths And State](docs/reference/paths-and-state.md)

## MCP Integration

The release includes a local stdio MCP server at `bin\mcp\OpenCode.Workspace.Mcp.exe`. It can expose
workspace templates, lifecycle operations, diagnostics, smoke validation, and generated artifacts to
MCP-compatible local clients. It does not expose a public network listener.

Start with [Local MCP setup](docs/integrations/mcp.md). The packaged CLI can print client configuration and run
the MCP doctor; see [CLI Reference](docs/reference/cli.md).

## Oracle And APEX

Oracle workspaces are optional examples and integrations, not platform prerequisites. For Oracle Database,
SQLcl, ORDS, APEX, and APEXlang workflows, start with:

- [Oracle and Oracle APEX integration](docs/integrations/oracle-apex.md)
- [Oracle PL/SQL Demo](docs/oracle-plsql-demo.md)
- [Oracle APEX Demo](docs/oracle-apex-demo.md)
- [Oracle APEXlang Demo](docs/oracle-apexlang-demo.md)
- [Oracle lifecycle workflows](docs/oracle-lifecycle-workflows.md)

The general workspace schema stays in [Workspace YAML](docs/reference/workspace-yaml.md); Oracle-specific
operational guidance remains in the Oracle documentation.

## Documentation

- [Documentation index](docs/index.md)
- [Getting started](docs/getting-started.md)
- [Workspaces](docs/user/workspaces.md)
- [Sessions](docs/user/sessions.md)
- [Backup and publish](docs/user/backup-and-publish.md)
- [Troubleshooting](docs/user/troubleshooting.md)
- [Package layout](docs/reference/package-layout.md)
- [CLI reference](docs/reference/cli.md)
- [Architecture](docs/architecture/overview.md)
- [Local MCP](docs/integrations/mcp.md)

## License

OpenCode Workspace Manager is licensed under the Apache License 2.0. See [LICENSE](LICENSE).

> There is no magic. Only stuff.
