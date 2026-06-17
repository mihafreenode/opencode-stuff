# Oracle ORDS Skill

Purpose:
Direct ORDS configuration, REST, deployment, and troubleshooting work to official Oracle REST Data Services documentation.

When to use:
- configuring ORDS in local Oracle workspaces
- answering REST enablement questions
- debugging ORDS connectivity, configuration, or deployment issues

Recommended documentation indexes:
- `docs/reference/oracle-ords-index.md`
- `docs/reference/oracle-database-index.md` for underlying database setup questions

Common workflows:
- start with ORDS install and configuration guidance
- check REST enablement sections for schema or object exposure tasks
- use database docs alongside ORDS docs when the issue crosses runtime and schema boundaries

Documentation discovery workflow:
- start at `docs/reference/oracle-knowledge-map.yaml`
- open `docs/reference/oracle-ords-index.md`
- use `docs/reference/oracle-database-index.md` when the ORDS problem depends on database configuration

Package lookup workflow:
- if runtime code also uses APEX REST packages, switch to `docs/reference/oracle-apex-api-reference.md`
- use the APEX API map before guessing which APEX package owns the integration behavior

Version compatibility guidance:
- use version-matched ORDS and APEX documentation whenever possible
- review APEX release notes when ORDS behavior changes appear tied to the application runtime version

Troubleshooting guidance:
- keep deployment and gateway questions in ORDS docs first
- move to database docs for grants, users, services, and connectivity prerequisites
- use version archives when examples from the latest docs do not match the local runtime

Official documentation:
- https://docs.oracle.com/en/database/oracle/oracle-rest-data-services/25.1/orddg/
