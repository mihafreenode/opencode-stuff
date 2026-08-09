# Oracle Documentation Strategy

Official Oracle documentation is the normative source for Oracle product behavior. This repository maintains navigation metadata and repository-owned guidance, and may also contain narrowly scoped retrieved or generated material when its provenance, purpose, and licensing status are explicit.

## Why

- official Oracle documentation remains authoritative
- curated local indexes reduce onboarding friction for humans and AI agents
- the repository stays lightweight and licensing-conscious
- the approach avoids treating mirrored or stale copies as authoritative
- maintenance stays focused on navigation metadata rather than manual content replication

## What This Repository Includes

- local indexes under `docs/reference/`
- knowledge maps and package maps under `docs/reference/*.yaml`
- short repository-owned descriptions and navigation hints
- Oracle skill prompts under `skills/oracle/`
- generated Oracle workspace guidance that points back to the same official sources
- optional generated or retrieved reference copies when a checked-in workflow explicitly creates them and records source, version, and review status
- small test fixtures or extracts needed to validate parsers and generators, subject to provenance and licensing review

## Copy And Retrieval Policy

- Do not present repository copies, generated extracts, caches, or fixtures as authoritative Oracle documentation.
- Do not add broad offline mirrors or bundled Oracle manuals by default.
- Optional retrieval workflows may create local or generated copies for a concrete workflow. Record the official source URL, product version, retrieval date or reproducible input, generated status, and whether redistribution has been reviewed.
- Test fixtures may include the minimum structure needed for reliable tests. Every fixture derived from Oracle material requires provenance and licensing review before it is committed or distributed.
- Keep user-downloaded Oracle media and documentation out of version control unless repository policy and the applicable license explicitly permit redistribution.

## Authoritative Sources

- Oracle documentation landing pages and version-matched product manuals linked through `docs/reference/oracle-knowledge-map.yaml`
- Oracle APEXlang 26.1 reference: https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/
- Oracle APEX 24.2 App Builder reference: https://docs.oracle.com/en/database/oracle/apex/24.2/htmdb/
- Oracle REST Data Services 25.1 reference: https://docs.oracle.com/en/database/oracle/oracle-rest-data-services/25.1/orddg/
- Oracle PL/SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/lnpls/
- Oracle Database SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/sqlrf/

The numbered links above are maintained versioned references. They are not a claim that APEX 24.2, ORDS 25.1, APEXlang 26.1, or Database 23 is the newest release or the correct version for every workspace.

## Documentation Discovery

1. Start with `docs/reference/oracle-knowledge-map.yaml`.
2. Choose the product path: APEX specification, APEX runtime API, ORDS deployment, SQL, PL/SQL, administration, or security.
3. Determine the deployed product version before relying on syntax, package availability, screenshots, installation steps, or runtime behavior.
4. Open the repository-owned index and follow its official Oracle links for the matching version.
5. Use `docs/reference/oracle-apex-version-archives.md` when the deployed APEX version differs from an indexed reference.

For APEX package discovery, use `docs/reference/oracle-apex-api-map.yaml` and `docs/reference/oracle-apex-api-packages.md`, then confirm behavior in the version-matched official API reference.

## Agent Workflow

1. Start with `docs/reference/oracle-knowledge-map.yaml` and select the relevant local index.
2. Open the official Oracle documentation linked there.
3. Prefer Oracle documentation over blogs or forum posts for normative answers.
4. Use APEXlang documentation first when the task is about Oracle APEX application definitions.
5. Use ORDS documentation first for REST, deployment, and gateway topics.
6. Use Oracle SQL and PL/SQL references for database implementation details.
7. Use version-matched Oracle documentation whenever possible; do not silently substitute an indexed version for the runtime version.

## Durable Workspace Fit

This strategy aligns with the durable workspace model:

- `workspace.yaml` and generated onboarding files stay readable
- Oracle onboarding stays reproducible without redistributing Oracle content
- local guidance helps agents become effective quickly after repository discovery
- workspace provisioning can include metadata and references without growing the runtime image unnecessarily
