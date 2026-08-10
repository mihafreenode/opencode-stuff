# Full Oracle Verification Candidate

`v0.1.0-rc.5` is the one release candidate that requires every existing environmental Oracle validation path. Main, other RC tags, stable tags, and manual integration runs retain the normal optional Oracle policy.

The accepted release tag grammar remains `vX.Y.Z` and `vX.Y.Z-rc.N`.

## Optional Oracle path audit

| Category | Project or command | Normal workflow/job | Required configuration | Images and services | Normal missing-prerequisite behavior |
| --- | --- | --- | --- | --- | --- |
| Oracle Smoke | `OpenCode.Workspace.Cli smoke run` for `oracle-plsql-demo`, `oracle-apex-demo`, and `oracle-apexlang-demo` | `integration.yml` / `Oracle Smoke (Sequential)`, enabled by `run_oracle` | Docker, network access, APEX media for APEX templates | `ubuntu:24.04` plus the database and ORDS tag-and-digest references in `catalog/verification/oracle-rc5-toolchain.json`; workspace, Oracle Database, and ORDS | The job is omitted when disabled. Once selected, missing runtime prerequisites fail rather than skip. |
| APEX development-loop prerequisites | `OpenCode.Workspace.Core.Tests` / `OracleApexAssistantIntegrationTests` | Operator-run wrapper only | `OPENCODE_APEX_DEVLOOP_ENABLED` plus workspace root, environment, SQLcl profile, application ID, source path, deployment profile, Builder URL, and application URL; reverse-change opt-in for one test | A reachable development Oracle/APEX environment through the named SQLcl profile; Docker is not directly required | Missing configuration and failed Doctor explicitly skip in normal test execution. The wrapper exits 2 for missing configuration. |
| Packaged Oracle MCP acceptance | `OpenCode.Workspace.Mcp.Tests` / `PackagedMcp_OracleApexlangProvisioning_ReportsProgress_AndCleansUp` | `integration.yml` / `Oracle Smoke (Sequential)` | `OPENCODE_RUN_PACKAGED_ORACLE_MCP=true`; package archive/root is optional normally | Same APEXlang images and services as Oracle Smoke; packaged LocalHost and MCP | Missing enablement explicitly skips. Enabled missing package or runtime prerequisites fail. |
| Windows Oracle Docker diagnostics | `OpenCode.Workspace.Core.Tests` / three focused `OraclePortConflictHandlingTests` | `ci.yml` / `Package win-x64` | Windows host only; scripted process runner, no live Oracle configuration | No live image or service | Off-Windows execution explicitly skips. The release matrix selects these only on Windows. |

No additional Oracle Docker, ORDS, SQLcl, synchronization-live, Assistant-live, or packaged Oracle acceptance suites currently exist.

## Verification mode

The `Full Oracle Verification` job runs only for the exact ref `refs/tags/v0.1.0-rc.5`, after mandatory integration and all three native packages succeed. It sets `OPENCODE_ORACLE_VERIFICATION_MODE=true`.

The immutable verification inputs are defined by `catalog/verification/oracle-rc5-toolchain.json`. The preflight resolves the database and ORDS tags and requires their recorded manifest-list digests, locates only the recorded APEX filename and verifies its SHA-256, and exports the recorded versioned SQLcl URL and SHA-256 for the packaged workspace image build. Verification mode never falls back to `sqlcl-latest.zip` or another `apex*.zip` file. Oracle media remains operator supplied and is not redistributed by this repository.

In verification mode:

- development-loop enablement, configuration, reverse-change opt-in, and Doctor failures fail instead of skip
- packaged Oracle MCP enablement and the exact release archive are required
- four development-loop tests, three Oracle smoke templates, and one packaged Oracle MCP test are selected
- the aggregate required result is 8 selected, 8 executed, 8 passed, 0 failed, and 0 skipped
- publication waits for successful Oracle verification

The packaged test starts the MCP executable from the downloaded `linux-x64` release archive with repository catalog inheritance disabled. It verifies template and tool initialization, APEXlang provisioning, Oracle/APEX/XDB/ORDS health evidence, runtime cleanup, MCP shutdown, and no owner-scoped resources or orphans.

## Preflight

Preflight reports status only and requires:

- the self-hosted `oracle` runner
- Docker daemon access
- resolvable database and ORDS tag-and-digest references from `catalog/verification/oracle-rc5-toolchain.json`
- SQLcl on `PATH`
- official APEX ZIP media in a supported configured location
- all development-loop variables without printing their values
- the downloaded release archive
- at least 40 GB free disk and 8 GB RAM
- successful smoke-owned cleanup, empty smoke inventory, and no smoke-owned orphans

Final cleanup repeats owner-scoped cleanup, inventory, Doctor, and dry-run checks. Packaged acceptance cleans its workspace runtime in `finally`, waits for stop and removal operations, verifies the package's Docker Compose project has no containers, networks, or volumes, and verifies the packaged MCP process exited.

## Existing live proof

Oracle Smoke provides live proof for Oracle Database provisioning, SQLcl PL/SQL execution, APEX installation, XDB validity, ORDS readiness, and APEX route reachability. Packaged Oracle MCP provides live proof for packaged template discovery, workspace creation/provisioning, Oracle-specific readiness evidence, and cleanup.

The four development-loop tests provide live-environment configuration and Doctor prerequisite proof only. Despite their historical names, they do not execute Assistant or synchronization operations.

## Coverage boundaries

| Production path | Existing live Oracle proof | Existing non-live proof |
| --- | --- | --- |
| Synchronization validate, diff, export, import, pull, push, discovery, and connect | None | Core scripted-runtime tests and API/UI contract tests cover orchestration and state behavior. |
| Assistant plan, apply, generated APEX validation, import, repair, rollback, and preview | None | Core tests with fake synchronization plus API/UI contract tests cover planning, safety, repair, rollback, and delegation. |

The verification candidate does not claim live proof for paths that have no existing live tests, and it does not add speculative Oracle tests solely to broaden the candidate.
