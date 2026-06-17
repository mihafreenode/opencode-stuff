# Oracle Documentation Strategy

This repository contains Oracle documentation references, not Oracle documentation copies.

## Why

- official Oracle documentation remains authoritative
- curated local indexes reduce onboarding friction for humans and AI agents
- the repository stays lightweight and licensing-conscious
- the approach avoids documentation drift from mirrored or stale copies
- maintenance stays focused on navigation metadata rather than manual content replication

## What This Repository Includes

- local indexes under `docs/reference/`
- knowledge maps and package maps under `docs/reference/*.yaml`
- short repository-owned descriptions and navigation hints
- Oracle skill prompts under `skills/oracle/`
- generated Oracle workspace guidance that points back to the same official sources

## What This Repository Does Not Include

- Oracle manuals copied into the repository
- offline mirrors of Oracle documentation sites
- bundled Oracle PDFs inside the repository or workspace images
- downloaded Oracle documentation cached during provisioning

## Authoritative Sources

- Oracle APEXlang: https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/
- Oracle APEX: https://docs.oracle.com/en/database/oracle/apex/24.2/htmdb/
- Oracle REST Data Services: https://docs.oracle.com/en/database/oracle/oracle-rest-data-services/25.1/orddg/
- Oracle PL/SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/lnpls/
- Oracle Database SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/sqlrf/

## Agent Workflow

1. Check the relevant local index under `docs/reference/`.
2. Open the official Oracle documentation linked there.
3. Prefer Oracle documentation over blogs or forum posts for normative answers.
4. Use APEXlang documentation first when the task is about Oracle APEX application definitions.
5. Use ORDS documentation first for REST, deployment, and gateway topics.
6. Use Oracle SQL and PL/SQL references for database implementation details.
7. Use version-matched Oracle documentation whenever possible.

## Durable Workspace Fit

This strategy aligns with the durable workspace model:

- `workspace.yaml` and generated onboarding files stay readable
- Oracle onboarding stays reproducible without redistributing Oracle content
- local guidance helps agents become effective quickly after repository discovery
- workspace provisioning can include metadata and references without growing the runtime image unnecessarily
