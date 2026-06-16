# Oracle APEX Runtime Smoke

This smoke validation is manual and optional.

It is not part of the default CI or unit-test suite because Oracle and APEX provisioning is slow, network-dependent, and requires Docker plus Oracle-provided downloads.

## Requirements

- Docker
- network access to Oracle-provided sources
- enough time for Oracle Database Free, SQLcl, and ORDS setup

## What It Validates

- creates and provisions an `oracle-apex-demo` workspace
- waits for Oracle database readiness
- waits for ORDS readiness
- checks that the APEX login URL responds with HTTP success or redirect
- runs a SQLcl test query inside the workspace container

## Command

```bash
scripts/smoke-oracle-apex-runtime.sh
```

This smoke test goes beyond static generation tests. Remaining live-runtime risk should be evaluated with this script or an equivalent manual end-to-end Oracle validation pass.
