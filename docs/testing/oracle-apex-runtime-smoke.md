# Oracle APEX Runtime Smoke

This smoke validation is manual and optional.

It is not part of the default CI or unit-test suite because Oracle and APEX provisioning is slow, network-dependent, and requires Docker plus Oracle-provided downloads.

## Requirements

- Docker
- network access to Oracle-provided sources
- enough time for Oracle Database Free, SQLcl, and ORDS setup

## Runtime Validation Host Selection

When validating runtime behavior from WSL, first check whether Docker is reachable from the current shell.

```bash
docker version
```

If WSL reports that it cannot connect to `/var/run/docker.sock`, do not immediately classify this as a product defect.

Check Docker from the Windows host:

```powershell
powershell.exe -NoProfile -Command "docker version"
```

If Windows Docker Desktop is reachable, continue runtime validation through Windows PowerShell.

Use the Windows host as the authoritative validation path for:

- WPF application behavior
- Docker Desktop orchestration
- Oracle runtime smoke tests
- Windows Terminal integration
- SQL Developer integration

Capture and report:

- WSL Docker result
- Windows Docker result
- selected validation host
- reason for selection

## Checked-In Smoke Tooling

Prefer the checked-in smoke runner over ad-hoc temporary projects or large inline PowerShell commands.

## Runtime Validation Ladder

Always validate in this order:

```text
Static Tests
    ↓
Windows Solution Tests
    ↓
Smoke Runner Dry Run
    ↓
Live Runtime Smoke
    ↓
Manual Validation
```

Do not start Oracle containers if static validation is failing.

Do not start Oracle containers if Windows-host solution tests are failing.

Do not classify runtime issues before dry-run validation succeeds.

Each stage should reduce uncertainty before moving to the next.

Example commands:

```powershell
dotnet run --project tools/OracleRuntimeSmoke -- --template oracle-plsql-demo --dry-run
dotnet run --project tools/OracleRuntimeSmoke -- --template oracle-apex-demo --dry-run
dotnet run --project tools/OracleRuntimeSmoke -- --template oracle-apexlang-demo --dry-run
```

Windows wrapper:

```powershell
scripts/testing/oracle-runtime-smoke.ps1 -Template oracle-apex-demo -DryRun
```

The smoke runner classifies failures separately as:

- Validation Tooling Failure
- Environment Failure
- Product Failure
- Oracle Runtime Failure

## What It Validates

- creates and provisions an `oracle-apex-demo` workspace
- waits for Oracle database readiness
- waits for ORDS readiness
- checks that the APEX login URL responds with HTTP success or redirect
- runs a SQLcl test query inside the workspace container

## Command

```bash
dotnet run --project tools/OracleRuntimeSmoke -- --template oracle-apex-demo
```

This smoke test goes beyond static generation tests. Remaining live-runtime risk should be evaluated with this script or an equivalent manual end-to-end Oracle validation pass.
