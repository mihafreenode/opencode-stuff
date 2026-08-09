# Testing

## Validation order

Use this order so later stages do not hide basic failures:

1. Static and portable tests.
2. Windows-host solution tests.
3. Smoke-runner dry run.
4. Live runtime smoke.
5. Manual desktop validation.

Treat failures in checked-in smoke runners, diagnostics, or recovery tools separately as validation-tooling failures.

## Solution tests

Portable or native-host validation:

```bash
dotnet test OpenCode.Workspace.slnx
```

Windows validation from WSL:

```bash
WINPWD=$(wslpath -w "$PWD")
powershell.exe -NoProfile -Command "Set-Location '$WINPWD'; dotnet test OpenCode.Workspace.slnx"
```

Report Linux/WSL and Windows results separately. Tests that use process-global desktop state must restore it and avoid unsafe parallel execution. External-process tests need cancellation, explicit timeout, process-tree termination, and disposal. A run is not successful if `dotnet test` does not return or leaves testhost/helper processes behind.

Docker tests must check prerequisites early, skip clearly when unavailable, use explicit timeouts, and clean owned resources. From WSL, check both environments when needed:

```bash
docker version
powershell.exe -NoProfile -Command "docker version"
```

Windows Docker Desktop is authoritative for the Windows product. Do not classify an unavailable WSL Docker socket as a product failure when Windows Docker works.

## Integration runners

Use the current checked-in integration wrappers from the repository root. PowerShell uses `-Suite`; the
shell wrapper takes the suite as its first argument:

```powershell
.\tools\test-integration.ps1 -Suite fast
.\tools\test-integration.ps1 -Suite mcp
.\tools\test-integration.ps1 -Suite api
```

```bash
./tools/test-integration.sh fast
./tools/test-integration.sh mcp
./tools/test-integration.sh api
```

The release gate includes Core, CLI, Avalonia, RemoteBridge, MCP protocol/package, API
`FastIntegration`, target-platform, and assembled-package validation. Use the release build or CI target
for the complete gate; an individual integration-wrapper suite is not a substitute for package and
platform validation.

The `live`, `non-oracle`, and `oracle` suites require environmental prerequisites and are optional/manual.
They provide useful evidence when the environment is available, but Docker, non-Oracle, and Oracle
environmental suites are not generic release gates.

## Platform compatibility

Use the packaged CLI name rather than the historical `opencode` shorthand:

```bash
bin/cli/OpenCode.Workspace.Cli doctor
bin/cli/OpenCode.Workspace.Cli validate-platform --target linux/amd64
bin/cli/OpenCode.Workspace.Cli validate-platform --target linux/arm64 --output report.md
```

On Windows use `bin\cli\OpenCode.Workspace.Cli.exe`. Native execution is preferred. `validate-platform` distinguishes Buildx build support from runtime execution support; either can succeed while the other is unavailable.

```bash
docker buildx ls
docker run --rm --platform linux/arm64 ubuntu:24.04 uname -m
```

Expected ARM64 output is `aarch64`. An `exec format error` means the current host lacks working ARM64 emulation; it does not prove the workspace fails on real ARM64 hardware. Enable QEMU/binfmt or validate on native ARM64. Buildx and emulation increase confidence but do not replace Windows ARM64, Linux ARM64, or Apple Silicon validation.

Generated runtime files include metadata for resolved runtime, target platform, and compatibility mode. Reports may be retained under `artifacts/platform-validation/`.

## Release testing

Before tagging, run every mandatory gate and validate freshly extracted packages as described in
[Packaging](packaging.md). Windows is the primary product-validation host; `linux-x64` and `osx-arm64`
packages are evaluation paths. Exercise the Windows contract at minimum:

- launch from a clean path containing spaces, outside repository output
- existing and missing workspace-index startup
- Create Workspace, Open Workspace, separate interactive-session attachment, Save Point, Backup, and remove-from-list
- recovery with a preserved user file and regenerated managed runtime files
- Publish to a disposable local bare remote without force push
- diagnostics for Git, Docker/Compose, Windows Terminal, fonts, OpenCode CLI, catalog, architecture, and runtime platform
- MCP configuration/doctor and a non-destructive discovery call
- RemoteBridge disabled-by-default behavior and local security coverage

Confirm that each candidate has its canonical filename, flat archive root, adjacent verified checksum,
and `release-manifest.json`, and that every packaged host is self-contained. Confirm that `bin/local-host`
contains neither an MCP apphost nor `OpenCode.Workspace.Mcp.runtimeconfig.json`.

The real Cloudflare smoke is optional, manual, and not a CI requirement. Live Docker, non-Oracle, and
Oracle smoke runs are also optional and must be reported separately. Live Oracle validation follows its dedicated checked-in tooling and documentation; do not
replace it with ad-hoc runners.
