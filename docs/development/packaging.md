# Packaging and release validation

## Release contract

The release pipeline produces exactly three supported or evaluated runtime packages:

| Runtime identifier | Archive | Status |
| --- | --- | --- |
| `win-x64` | `opencode-workspace-<version>-win-x64.zip` | Primary supported release |
| `linux-x64` | `opencode-workspace-<version>-linux-x64.tar.gz` | Unix evaluation release |
| `osx-arm64` | `opencode-workspace-<version>-osx-arm64.tar.gz` | Unix evaluation release |

All three packages are self-contained. No other RID is part of the supported or evaluated release
contract. Every ZIP and tarball is flat-root: extracting it places `OpenCode.Workspace` (or
`OpenCode.Workspace.exe`), `bin`, `catalog`, `config`, `docs`, and the release files directly in the
selected directory, without an enclosing package directory.

Local and CI builds use the same `OpenCode.Workspace.ReleaseTool` assembler. The assembler owns package
layout, the documentation allowlist, `release-manifest.json`, archive creation, and the adjacent verified
`<archive>.sha256` file. The manifest is at archive root and contains:

```json
{
  "version": "1.2.3",
  "gitCommit": "<commit SHA>",
  "buildTimestamp": "<UTC timestamp>",
  "runtimeIdentifier": "win-x64",
  "selfContained": true
}
```

Release documentation is copied from an explicit allowlist rather than by recursively copying the
repository documentation tree. Development documentation, history, and internal test notes are excluded;
adding a file under `docs/` does not make it package content unless the allowlist names it.

Version selection is deterministic:

- A tag build uses the exact tag text with one leading `v` removed. For example, `v1.2.3` produces version `1.2.3`.
- A local build uses `-Version` when supplied, otherwise a tag pointing exactly at `HEAD`, otherwise `0.1.0-local.<UTC timestamp>`.
- A non-tag CI build uses `0.1.0-ci.<run>`.

The selected version is used unchanged in the package directory, archive filename, checksum filename,
and release manifest.

## Local Windows release

The supported local release command is the Windows-only script:

```powershell
.\tools\build-release.ps1 -Clean
```

From WSL:

```bash
./tools/build-release-from-wsl.sh -Clean
```

The default script requires Windows `dotnet.exe` with the .NET 10 SDK and produces the primary `win-x64`
package. Linux and macOS evaluation packages are built on their native CI hosts.

By default it restores and builds the solution, runs the mandatory test set, publishes self-contained
Release desktop/CLI/LocalHost/MCP/RemoteBridge hosts without symbols, invokes the shared assembler,
validates the package, and creates the archive, manifest, and verified checksum. Any option that skips a
stage produces a development artifact, not a release candidate, and must be recorded when used.

For version `1.2.3`, outputs are:

```text
artifacts/release/win-x64/package/opencode-workspace-1.2.3-win-x64/
artifacts/release/opencode-workspace-1.2.3-win-x64.zip
artifacts/release/opencode-workspace-1.2.3-win-x64.zip.sha256
```

The package contains `OpenCode.Workspace.exe`, `bin/cli`, `bin/local-host`, `bin/mcp`,
`bin/remote-bridge`, `catalog`, `Localization`, allowlisted `docs`, packaged configuration, and
`release-manifest.json`. There is no supported `bin/api` entry point: the API project is packaged as
LocalHost.

## Mandatory release gates

Every release candidate must pass all of these gates for its target RID:

- Core tests
- CLI tests
- Avalonia tests
- RemoteBridge tests
- MCP protocol and extracted-package tests
- API `FastIntegration` tests
- target-platform and assembled-package validation, including archive shape, manifest, checksum, self-contained hosts, and packaged API behavior

Live Docker, non-Oracle environmental smoke, and Oracle environmental suites are optional/manual
validation. Record them separately when the required environment is available; they are not generic
release gates and must not block an otherwise valid release solely because those external prerequisites
are absent.

## Package validation

Package validation verifies required executables and RemoteBridge configuration, checks disabled
RemoteBridge exit, runs CLI smoke/runtime discovery, starts LocalHost outside the repository for
health/template/smoke-definition probes, and runs the extracted-distribution MCP package test. Archive
validation also checks the flat root, manifest fields and values, self-contained runtime payload, and the
adjacent checksum before accepting the artifact.

The duplicate MCP payload is fixed at its project-publish cause. Package guards reject an MCP apphost or
`OpenCode.Workspace.Mcp.runtimeconfig.json` under `bin/local-host`; the only supported MCP host is under
`bin/mcp`.

Manual release-candidate validation must use a freshly extracted archive, not the assembled directory or repository output. Record commit, selected version, archive filename, checksum, date, validator, host version, skipped stages, and results. Confirm:

- archive checksum and extraction into a clean path containing spaces
- flat archive root, valid `release-manifest.json`, no `.pdb` files, and required self-contained runtime files beside each host
- `bin/api` is absent and RemoteBridge remains disabled by default
- MCP apphost and runtime configuration are absent from `bin/local-host`
- startup and workspace index behavior are independent of the checkout
- first-workspace, recovery, Publish, diagnostics, attach, and MCP workflows from [Testing](testing.md)
- backup excludes secrets, `.env`, `.git`, local runtime state, dependencies, and rebuildable output while retaining canonical durable inputs and expected history

There is currently no MSIX build, signing, notarization, or installer pipeline. Do not claim MSIX/signing coverage or ship an unsigned artifact as if it were signed.

## CI behavior

`.github/workflows/ci.yml` builds the `win-x64`, `linux-x64`, and `osx-arm64` matrix, publishes every host
self-contained, runs the mandatory gates, and sends every RID through the shared assembler. Non-tag runs
use `0.1.0-ci.<run>`. Tag runs use the exact tag version without the leading `v` and attach all three
canonical archives and their adjacent verified checksums to the GitHub Release. Each archive contains its
corresponding release manifest.

Only `vX.Y.Z` and `vX.Y.Z-rc.N` are publishable tag forms. A stable tag creates a normal release eligible
for Latest. An RC tag creates a prerelease and is never marked Latest. Each publishable version requires a
checked-in `docs/releases/<version>.md` release body; `docs/history/release-notes.md` remains historical and
is not used as current release metadata.

The tagged dependency graph is:

```text
release-metadata -> package[win-x64, linux-x64, osx-arm64] --\
integration-validation ------------------------------------> staged draft -> verified publish
```

Each package leg runs on its native runner, uploads its archive and sidecar, downloads that exact current-run
artifact, and executes extracted-package acceptance against the downloaded archive. Publication downloads
only the three current-run package artifact containers, requires the exact six-file inventory, and verifies
every checksum again. The release action stages a draft first. The workflow verifies the staged GitHub asset
inventory before changing the draft to either an RC prerelease or a stable release, so a failed upload does
not expose a misleading partial stable release.

Native ownership is Windows for `win-x64`, Ubuntu for `linux-x64`, and macOS arm64 for `osx-arm64`. The
unsigned macOS evaluation binary can produce a Gatekeeper warning; that warning is distinct from package or
startup failure and remains until signing/notarization is implemented.
