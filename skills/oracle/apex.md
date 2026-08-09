# Oracle APEX Skill

Purpose:
Guide Oracle APEX Builder, workspace administration, and application review work toward official Oracle references.

When to use:
- creating or reviewing Oracle APEX applications
- answering Builder questions about pages, regions, items, processes, or shared components
- checking administration or deployment guidance for APEX workspaces

Recommended documentation indexes:
- `docs/reference/oracle-knowledge-map.yaml`
- `docs/reference/oracle-apex-index.md`
- `docs/reference/oracle-apexlang-index.md` when the artifact is an `.apx` definition
- `docs/reference/oracle-ords-index.md` for ORDS integration topics

Common workflows:
- open the APEX index first
- choose App Builder, Administration, or Installation guidance based on the task
- switch to APEXlang when reviewing source-controlled application definitions
- use `docs/reference/oracle-apex-api-map.yaml` and `docs/reference/oracle-apex-api-packages.md` for package lookup before searching the full API book

Documentation discovery workflow:
- start at `docs/reference/oracle-knowledge-map.yaml`
- open `docs/reference/oracle-apex-books.md` when you need the right Oracle book first
- switch to `docs/reference/oracle-apex-api-reference.md` for PL/SQL package work

Package lookup workflow:
- identify the package family in `docs/reference/oracle-apex-api-map.yaml`
- confirm the package entry in `docs/reference/oracle-apex-api-packages.md`
- open the official Oracle API deep link for final confirmation

Version compatibility guidance:
- use `docs/reference/oracle-apex-version-archives.md` when the runtime differs from an indexed APEX release
- prefer version-matched API and release-note docs before claiming package availability

Troubleshooting guidance:
- use administration docs for workspace and security issues
- use installation docs for runtime provisioning and upgrade issues
- use release notes when behavior differs across APEX versions

Do not duplicate a fixed official-URL list here. Follow the knowledge map and versioned indexes, then use official Oracle documentation matching the target runtime.
