# Package Layout

## Distribution Contract

The supported and evaluated release set is limited to these canonical files:

```text
opencode-workspace-<version>-win-x64.zip
opencode-workspace-<version>-win-x64.zip.sha256
opencode-workspace-<version>-linux-x64.tar.gz
opencode-workspace-<version>-linux-x64.tar.gz.sha256
opencode-workspace-<version>-osx-arm64.tar.gz
opencode-workspace-<version>-osx-arm64.tar.gz.sha256
```

`win-x64` is the primary supported package. `linux-x64` and `osx-arm64` are Unix evaluation packages.
All three are self-contained, and no other RID is part of the release contract.

Every archive has a flat root. Extract it into a dedicated directory; the selected directory immediately
contains the following paths rather than another `opencode-workspace-<version>-<rid>` directory:

```text
OpenCode.Workspace.exe
bin/
  local-host/OpenCode.Workspace.LocalHost.exe
  cli/OpenCode.Workspace.Cli.exe
  mcp/OpenCode.Workspace.Mcp.exe
  remote-bridge/OpenCode.Workspace.RemoteBridge.exe
catalog/
config/
  api/appsettings.json
  mcp/appsettings.json
  remote-bridge/appsettings.json
docs/
Localization/
release-manifest.json
README.md
LICENSE
THIRD-PARTY-NOTICES.md
```

Unix hosts use the same paths without the `.exe` suffix. Each published host carries its own
self-contained .NET runtime files. Run the root `OpenCode.Workspace.exe` on Windows or
`OpenCode.Workspace` on Unix; do not move it away from the extracted package.

The adjacent `.sha256` uses the full archive filename and is verified as part of assembly and release
validation. `release-manifest.json` records exactly these properties:

- `version`
- `gitCommit`
- `buildTimestamp`
- `runtimeIdentifier`
- `selfContained`

The local and CI pipelines both use `OpenCode.Workspace.ReleaseTool` as the shared assembler for layout,
metadata, archive creation, and checksums.

## Component Roles

- Root `OpenCode.Workspace.exe`: desktop workspace manager.
- `bin/local-host`: loopback LocalHost API, interactive session owner, and local browser assets.
- `bin/cli`: diagnostics and automation CLI.
- `bin/mcp`: local stdio MCP host.
- `bin/remote-bridge`: opt-in remote browser bridge, disabled by default.
- `catalog`: packaged declarative workspace capabilities and templates.
- `config`: package defaults copied from each host project.

`config/api/appsettings.json` is configuration for the component packaged as `bin/local-host`. It does
not imply a supported `bin/api` directory.

There is no `bin/api` and no `bin/desktop` in the package contract.

## Package Boundaries

Packaged documentation is selected by an explicit ReleaseTool allowlist. Development documentation,
history, and internal test notes are not distributable package content. Repository-wide recursive copying
is not part of the package contract.

The former duplicate MCP publish payload was fixed at the project dependency that caused it. Assembly and
package validation guard against both the MCP apphost and `OpenCode.Workspace.Mcp.runtimeconfig.json`
appearing under `bin/local-host`. MCP is published only under `bin/mcp`.

## Distribution Status

- Windows `win-x64`: self-contained ZIP and verified checksum; primary release path.
- Linux `linux-x64`: self-contained tarball and verified checksum; evaluation path.
- macOS `osx-arm64`: self-contained tarball and verified checksum; evaluation path.
- Desktop terminal attachment: Windows-only in the current implementation.

There is currently no installer, MSIX, package signing, or auto-update contract. Extracted archive
installation is authoritative.
