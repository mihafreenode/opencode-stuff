# Oracle

## What It Is

This capability is the Oracle-specific discovery layer for database, APEX, and source-controlled APEXlang workflows in the workspace.

## Why Use It

Use it when the workspace includes Oracle services or Oracle onboarding assets and you need to understand the official tools, examples, and learning path quickly.

## Available Tools

### SQLcl

Purpose: Oracle command-line client for scripts, schema work, and APEX automation.

Supported workflows: PL/SQL development, schema work, APEX automation.

Common use cases: query demo schemas, run Oracle scripts.

### Data Pump

Purpose: Oracle schema and data export/import workflow.

Supported workflows: schema export, environment snapshots, data movement.

Common use cases: backup onboarding snapshots, refresh sample environments.

### ORDS and APEX

Purpose: Browser-accessible Oracle APEX development and HTTP access.

Supported workflows: Oracle APEX onboarding, REST and browser validation, sample application work.

Common use cases: open APEX Builder, validate ORDS reachability.

### APEXlang

Purpose: Source-controlled Oracle APEX export, validation, review, and import.

Supported workflows: export and import, Git review, source-controlled APEX changes.

Common use cases: review application changes in Git, validate exported APEX artifacts.

## Typical Tasks

- Start with PL/SQL onboarding before moving into APEX and then APEXlang.
- Use SQLcl, ORDS, and Oracle docs to validate the local Oracle environment.
- Review Oracle examples, lifecycle docs, and source-controlled workflows from the generated guides.

## Environment-Aware ORDS Checks

When asked to check whether APEX or ORDS is running, do not assume `localhost:8181`.

Use this decision order:

1. Determine where you are executing.
2. Locate `compose.yaml` and `.env`.
3. Determine the ORDS service name and the correct endpoint.
4. Verify ORDS landing with `GET /ords/_/landing`.
5. Verify APEX runtime with `GET /ords/apex`.
6. If ORDS is unreachable, report the exact endpoint that was tested.

### Inside A Docker Workspace Container

- Detect container execution, for example with `/.dockerenv`.
- Do not assume `localhost` points at ORDS.
- `localhost` inside the workspace container is the workspace container itself.
- Read `compose.yaml` and `.env` to determine the ORDS service name and internal port.
- Prefer the internal Docker network address.

Container examples:

```text
http://oracle-ords:8080/ords/_/landing
http://oracle-ords:8080/ords/apex
```

If Docker Compose CLI is unavailable inside the workspace container, fall back to reading `compose.yaml` directly.

### On The Host

- Read `compose.yaml` and use the published host port.
- Do not hardcode `8181` if the compose file publishes a different port.

Host examples:

```text
http://localhost:<published-port>/ords/_/landing
http://localhost:<published-port>/ords/apex
```

### Verification Rules

- ORDS landing success means `GET /ords/_/landing` returns a healthy response.
- APEX success means `GET /ords/apex` returns a healthy response.
- Do not claim APEX is down until you have first chosen the correct host or container endpoint.
- Report the actual URL that was checked in the result.

## Recommended Learning Path

1. [Practical Git for Oracle Developers](../oracle/practical-git-for-oracle-developers.md)
2. [From Oracle Demo to Oracle Onboarding](../articles/oracle-onboarding.md)
3. [Repository Workflows](repository.md)
4. [Testing](testing.md)
5. [AGENTS.md Guide](../agents-guide.md)
6. [APEXlang](../oracle-tools/apexlang.md)

## Examples

- Follow `docs/oracle-plsql-demo.md`, then `docs/oracle-apex-demo.md`, then `docs/oracle-apexlang-demo.md`.
- Use `docs/oracle-tools/README.md` as the Oracle tool index.
- If the user asks `Can you check if APEX is running?`, first determine whether you are on the host or inside the workspace container, then choose the matching ORDS endpoint before running any `curl` command.

## Related Documentation

- [Oracle Tools Index](../oracle-tools/README.md)
Oracle-specific catalog for SQLcl, Data Pump, ORDS, SQL Developer, APEX export/import, and APEXlang.
- [Oracle Samples](../oracle-samples.md)
Shared sample domain and progression across Oracle workspaces.
- [Oracle PL/SQL Demo](../oracle-plsql-demo.md)
PL/SQL-first onboarding entry point.
- [Oracle APEX Demo](../oracle-apex-demo.md)
Browser-based Oracle APEX onboarding step.
- [Oracle APEXlang Demo](../oracle-apexlang-demo.md)
Source-controlled APEX workflow step.

## Related Capabilities

- [Repository Workflows](repository.md)
- [Testing](testing.md)
