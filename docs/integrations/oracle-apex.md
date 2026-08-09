# Oracle and Oracle APEX Integration

This is the authoritative map for Oracle Database and Oracle APEX integration in OpenCode Workspace Manager. Use it to determine what the product implements, what depends on local Oracle prerequisites, and what remains experimental. Follow the dedicated guides for operational detail.

## Capability Status

### Implemented

- **Oracle Database Free workspace runtime:** the Oracle templates generate Docker Compose services, persistent database storage, sample schema assets, health checks, and connection guidance.
- **SQLcl tooling:** provisioning installs SQLcl from Oracle-provided sources and generates wrappers and tutorial commands for SQL and APEX automation.
- **ORDS and APEX provisioning plan:** APEX-capable templates generate ORDS services, configuration, readiness checks, and APEX installation steps.
- **Explicit APEX synchronization:** configured applications support status, validation, export, import, diff, pull, and push operations with drift and environment checks. These operations are user- or Assistant-initiated.
- **Oracle Assistant:** the desktop and OpenCode Workspace MCP paths can plan and review APEX changes, apply approved plans, validate or import generated source, propose repairs, and roll back an Assistant execution. Environment and deployment safeguards still apply.
- **APEXlang workspace support:** templates, reference indexes, generated source locations, SQLcl export/import commands, and Assistant-oriented editing support exist for APEXlang application definitions.
- **Oracle onboarding templates:** `oracle-plsql-demo`, `oracle-apex-demo`, and `oracle-apexlang-demo` provide a progressive database-to-APEX learning path.

`manual`, `watch-safe`, and `watch-live` are synchronization policy names. They influence what an explicit synchronization or Oracle Assistant operation may do. They do not start an autonomous file-system or database watcher, polling service, or background synchronization loop.

### Optional Or Environment-Dependent

- **Oracle APEX media:** users must obtain an official APEX ZIP and place it in the documented local download location. The repository does not redistribute it.
- **Live APEX and ORDS runtime:** success depends on Docker Desktop, compatible host resources, network access during first provisioning, Oracle media, image compatibility, and available ports.
- **SQL Developer:** integration is available when SQL Developer is installed on Windows; SQLcl and SQL*Plus remain terminal alternatives.
- **Customer Oracle access:** TNS files, wallets, credentials, permissions, and customer network access are user-supplied and must remain outside version control.
- **Application source:** the generated `Customer Orders Demo` APEX file is a tutorial placeholder and source location. The schema is supplied, but a complete generated APEX application with reports, grids, charts, and dashboards is not shipped.
- **Media and other Oracle resources:** downloads may have Oracle license terms, authentication requirements, or version compatibility constraints. Provisioning must not imply that the repository owns or can redistribute those resources.

### Experimental Or Scaffolding

- **Oracle SQLcl MCP:** the catalog contains an Oracle SQLcl MCP entry for environments where `sql -mcp` is available. It is experimental/catalog-level integration and is not the OpenCode Workspace MCP server, which provides workspace lifecycle and Oracle Assistant tools.
- **Data Pump helpers:** generated `export-datapump.sh` and `import-datapump.sh` are conceptual scaffolding. They print suggested `expdp` and `impdp` commands; they do not execute an export or import.
- **Standalone APEX shape validation:** generated `scripts/validate-apex.sh` checks that a file exists and resembles an APEX artifact. It is not authoritative APEXlang semantic validation, SQLcl `apex validate`, compilation, or runtime validation.
- **Development-loop wrapper:** `scripts/testing/oracle-apex-development-loop.ps1` checks required local environment variables and runs the Oracle APEX Assistant integration-test filter. It does not perform the complete Doctor, prompt, plan, validate, import, preview, or rollback workflow.
- **End-to-end live validation:** the checked-in runtime smoke runner covers important provisioning and readiness probes, but complete interactive Builder, Assistant, synchronization, and deployment scenarios still require environment-specific validation.

## Integration Flow

```text
Oracle Database Free
    -> SQLcl and schema scripts
    -> user-supplied APEX media
    -> ORDS and APEX runtime
    -> explicit export / validate / diff / import
    -> Git review and Save Point
```

APEX Builder remains the browser development environment. APEXlang makes supported application definitions reviewable as source. Oracle Assistant operates on repository source through reviewable plans and explicit execution; it does not remove the need for version-matched validation or human review.

## Official Documentation Strategy

1. Start with `docs/reference/oracle-knowledge-map.yaml`.
2. Identify the actual Oracle Database, APEX, APEXlang, SQLcl, and ORDS versions in the target environment.
3. Use the corresponding repository index to locate official Oracle documentation for those versions.
4. Treat links labeled `24.2`, `25.1`, `26.1`, or another release as versioned references, not as a claim that the release is universally current.
5. Use Oracle documentation as the normative product source. Repository-owned indexes, optional retrieval copies, generated extracts, and test fixtures must retain provenance and receive licensing review before redistribution.

See [Oracle Documentation Strategy](../oracle-documentation-strategy.md) for the full source, copy, and fixture policy.

## Dedicated Guides

- [Oracle team onboarding](../oracle/team-onboarding.md)
- [Oracle PL/SQL onboarding](../oracle-plsql-demo.md)
- [Detailed Oracle PL/SQL runtime guide](../oracle-demo.md)
- [Oracle APEX demo](../oracle-apex-demo.md)
- [Oracle APEXlang demo](../oracle-apexlang-demo.md)
- [Oracle lifecycle workflows](../oracle-lifecycle-workflows.md)
- [Oracle tools](../oracle-tools/README.md)
- [APEX export and import](../oracle-tools/apex-export-import.md)
- [APEXlang](../oracle-tools/apexlang.md)
- [Oracle APEX runtime smoke](../testing/oracle-apex-runtime-smoke.md)
- [Oracle APEX development-loop validation](../testing/oracle-apex-development-loop.md)
- [Oracle reference knowledge map](../reference/oracle-knowledge-map.yaml)
- [Oracle Database reference index](../reference/oracle-database-index.md)
- [Oracle PL/SQL reference index](../reference/oracle-plsql-index.md)
- [Oracle APEX reference index](../reference/oracle-apex-index.md)
- [Oracle APEXlang reference index](../reference/oracle-apexlang-index.md)
- [ORDS reference index](../reference/oracle-ords-index.md)
