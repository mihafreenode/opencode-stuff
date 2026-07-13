using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

internal static class OracleWorkspaceGeneratedContent
{
    public static IReadOnlyDictionary<string, string> Generate(
        WorkspaceDefinition definition,
        WorkspaceRuntimeStateRecord? runtimeState,
        Func<string, string> withGeneratedHeader,
        Func<string, string> withGeneratedSqlHeader,
        Func<string, string> withGeneratedScriptHeader)
    {
        var kind = OracleWorkspaceFamily.Detect(definition);
        if (kind == OracleWorkspaceKind.None)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("docs", "oracle-plsql-demo.md")] = withGeneratedHeader(OraclePlSqlDemoDoc()),
            [Path.Combine("docs", "oracle-samples.md")] = withGeneratedHeader(OracleSamplesDoc()),
            [Path.Combine("docs", "oracle-documentation-strategy.md")] = withGeneratedHeader(OracleDocumentationStrategyDoc()),
            [Path.Combine("docs", "oracle-documentation-discovery.md")] = withGeneratedHeader(OracleDocumentationDiscoveryDoc()),
            [Path.Combine("docs", "team-onboarding.md")] = withGeneratedHeader(TeamOnboardingDoc(kind)),
            [Path.Combine("docs", "oracle-lifecycle-workflows.md")] = withGeneratedHeader(OracleLifecycleWorkflowsDoc(kind)),
            [Path.Combine("docs", "sharing-oracle-workspaces.md")] = withGeneratedHeader(SharingOracleWorkspacesDoc(kind)),
            [Path.Combine("docs", "oracle-tools", "README.md")] = withGeneratedHeader(OracleToolsIndexDoc()),
            [Path.Combine("docs", "oracle-tools", "sqlcl.md")] = withGeneratedHeader(SqlclToolDoc()),
            [Path.Combine("docs", "oracle-tools", "data-pump.md")] = withGeneratedHeader(DataPumpToolDoc()),
            [Path.Combine("docs", "oracle-tools", "ords.md")] = withGeneratedHeader(OrdsToolDoc()),
            [Path.Combine("docs", "oracle-tools", "apex-export-import.md")] = withGeneratedHeader(ApexExportImportToolDoc()),
            [Path.Combine("docs", "oracle-tools", "apexlang.md")] = withGeneratedHeader(ApexLangToolDoc()),
            [Path.Combine("docs", "oracle-tools", "sql-developer.md")] = withGeneratedHeader(SqlDeveloperToolDoc()),
            [Path.Combine("skills", "oracle", "plsql.md")] = OraclePlSqlSkillDoc(),
            [Path.Combine("skills", "oracle", "database.md")] = OracleDatabaseSkillDoc(),
            [Path.Combine("scripts", "update-oracle-doc-index.ps1")] = UpdateOracleDocIndexPowerShellScript(),
            [Path.Combine("scripts", "update-oracle-doc-index.sh")] = withGeneratedScriptHeader(UpdateOracleDocIndexShellScript()),
            [Path.Combine("scripts", "update-oracle-navigation-index.ps1")] = UpdateOracleNavigationIndexPowerShellScript(),
            [Path.Combine("scripts", "update-oracle-navigation-index.sh")] = withGeneratedScriptHeader(UpdateOracleNavigationIndexShellScript()),
        };

        if (kind is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang)
        {
            var ordsBaseUrl = $"http://localhost:{WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId)}/ords";
            var apexLoginUrl = ordsBaseUrl + "/apex";
            files[Path.Combine("docs", "oracle-apex-demo.md")] = withGeneratedHeader(OracleApexDemoDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-books.md")] = withGeneratedHeader(OracleApexBooksDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-api-reference.md")] = withGeneratedHeader(OracleApexApiReferenceDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-administration.md")] = withGeneratedHeader(OracleApexAdministrationDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-installation.md")] = withGeneratedHeader(OracleApexInstallationDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-release-notes.md")] = withGeneratedHeader(OracleApexReleaseNotesDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-version-archives.md")] = withGeneratedHeader(OracleApexVersionArchivesDoc());
            files[Path.Combine("docs", "reference", "oracle-apex-api-map.yaml")] = withGeneratedHeader(OracleApexApiMapYaml());
            files[Path.Combine("docs", "reference", "oracle-apex-api-packages.md")] = withGeneratedHeader(OracleApexApiPackagesDoc());
            files[Path.Combine("skills", "oracle", "apex.md")] = OracleApexSkillDoc();
            files[Path.Combine("skills", "oracle", "ords.md")] = OracleOrdsSkillDoc();
            files[Path.Combine("scripts", "health-check-database.sh")] = withGeneratedScriptHeader(HealthCheckDatabaseScript());
            files[Path.Combine("scripts", "health-check-ords.sh")] = withGeneratedScriptHeader(HealthCheckOrdsScript());
            files[Path.Combine("scripts", "health-check-apex.sh")] = withGeneratedScriptHeader(HealthCheckApexScript());
            files[Path.Combine("scripts", "health-check-sqlcl.sh")] = withGeneratedScriptHeader(HealthCheckSqlclScript());
            files[Path.Combine("scripts", "sqlcl.sh")] = withGeneratedScriptHeader(SqlclWrapperScript());
            files[Path.Combine("scripts", "open-ords.ps1")] = OpenOrdsScript(ordsBaseUrl);
            files[Path.Combine("scripts", "open-apex.ps1")] = OpenApexScript(apexLoginUrl);
            files[Path.Combine("scripts", "open-sql-worksheet.ps1")] = OpenSqlWorksheetScript();
            files[Path.Combine("tutorial", "oracle", "init", "03-customers-schema.sql")] = withGeneratedSqlHeader(CustomersSchemaSql());
            files[Path.Combine("tutorial", "oracle", "init", "04-customers-sample-data.sql")] = withGeneratedSqlHeader(CustomersSampleDataSql());
            files[Path.Combine("tutorial", "oracle", "scripts", "health-check-database.sql")] = withGeneratedSqlHeader(HealthCheckDatabaseSql());
            files[Path.Combine("tutorial", "oracle", "scripts", "health-check-pdb.sql")] = withGeneratedSqlHeader(HealthCheckPdbSql());
        }

        if (kind == OracleWorkspaceKind.ApexLang)
        {
            files[Path.Combine("docs", "oracle-apexlang-demo.md")] = withGeneratedHeader(OracleApexLangDemoDoc());
            files[Path.Combine("docs", "apexlang-introduction.md")] = withGeneratedHeader(ApexLangIntroductionDoc());
            files[Path.Combine("docs", "oracle-tools", "apexlang-hello-world.md")] = withGeneratedHeader(ApexLangHelloWorldDoc());
            files[Path.Combine("docs", "reference", "oracle-apexlang-navigation.md")] = withGeneratedHeader(OracleApexLangNavigationDoc());
            files[Path.Combine("skills", "oracle", "apexlang.md")] = OracleApexLangSkillDoc();
            foreach (var tutorialFile in OracleApexGuidedTourBuilder.BuildFiles())
            {
                files[tutorialFile.Key] = tutorialFile.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || tutorialFile.Key.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    ? tutorialFile.Value
                    : withGeneratedHeader(tutorialFile.Value);
            }
            foreach (var reference in OracleApexSyntaxReferenceBuilder.BuildFiles(ReadingApexlangSyntaxSource(), "https://docs.oracle.com/en/database/oracle/apex/26.1/apxdc/reading-apexlang-syntax.html", "26.1"))
            {
                files[reference.Key] = withGeneratedHeader(reference.Value);
            }
            files[Path.Combine("apex", "application.apx")] = ApexApplicationStub();
            files[Path.Combine("sql", "customers-reference.sql")] = withGeneratedSqlHeader(CustomersReferenceSql());
            files[Path.Combine("sql", "hello-apexlang", "generate-hello-apexlang.sql")] = withGeneratedSqlHeader(GenerateHelloApexLangSql());
            files[Path.Combine("sql", "hello-apexlang", "validate-hello-apexlang.sql")] = withGeneratedSqlHeader(ValidateHelloApexLangSql());
            files[Path.Combine("sql", "hello-apexlang", "import-hello-apexlang.sql")] = withGeneratedSqlHeader(ImportHelloApexLangSql());
            files[Path.Combine("sql", "hello-apexlang", "export-hello-apexlang.sql")] = withGeneratedSqlHeader(ExportHelloApexLangSql());
            files[Path.Combine("scripts", "export-apex.sh")] = withGeneratedScriptHeader(ExportApexScript());
            files[Path.Combine("scripts", "import-apex.sh")] = withGeneratedScriptHeader(ImportApexScript());
            files[Path.Combine("scripts", "validate-apex.sh")] = withGeneratedScriptHeader(ValidateApexScript());
            files[Path.Combine("scripts", "export-schema.sh")] = withGeneratedScriptHeader(ExportSchemaScript());
            files[Path.Combine("scripts", "import-schema.sh")] = withGeneratedScriptHeader(ImportSchemaScript());
            files[Path.Combine("scripts", "export-datapump.sh")] = withGeneratedScriptHeader(ExportDataPumpScript());
            files[Path.Combine("scripts", "import-datapump.sh")] = withGeneratedScriptHeader(ImportDataPumpScript());
            files[Path.Combine("scripts", "export-apexlang.sh")] = withGeneratedScriptHeader(ExportApexLangScript());
            files[Path.Combine("scripts", "apexlang-hello-world.sh")] = withGeneratedScriptHeader(ApexLangHelloWorldScript());
            files[Path.Combine("scripts", "apexlang-hello-world.ps1")] = ApexLangHelloWorldPowerShellScript();
        }

        return files;
    }

    private static string OracleDocumentationStrategyDoc() => """
## Oracle Documentation Strategy

This workspace includes Oracle documentation references, not Oracle documentation copies.

### Principles

- official Oracle documentation remains authoritative
- local indexes improve onboarding and agent navigation
- the workspace stays lightweight and licensing-safe
- no Oracle manuals, mirrors, or offline copies are included

### Start Here

- `docs/reference/oracle-plsql-index.md`
- `docs/reference/oracle-database-index.md`

If this workspace includes Oracle APEX or ORDS, also use:

- `docs/reference/oracle-apex-index.md`
- `docs/reference/oracle-ords-index.md`

If this workspace includes source-controlled Oracle APEX definitions, start with:

- `docs/reference/oracle-apexlang-index.md`
- `docs/reference/oracle-apexlang-navigation.md`

### Agent Workflow

1. Check the local index under `docs/reference/`.
2. Open the official Oracle documentation linked from that index.
3. Prefer Oracle documentation over blogs and forum posts for normative answers.
4. Use `skills/oracle/` for task-oriented repository guidance.
""";

    private static string OracleDocumentationDiscoveryDoc() => """
## Oracle Documentation Discovery

This workspace uses a lightweight Oracle discovery layer so humans and AI agents can navigate official Oracle sources without mirroring Oracle documentation.

### Start Here

1. `docs/reference/oracle-knowledge-map.yaml`
2. the most relevant topic index under `docs/reference/`
3. the official Oracle links from that index

### Version Guidance

Use version-matched documentation whenever possible.

If the runtime version differs from the latest Oracle documentation examples, use:

- `docs/reference/oracle-apex-version-archives.md`
- `docs/reference/oracle-apex-release-notes.md`

### Package Discovery

Use these files for fast APEX package lookup:

- `docs/reference/oracle-apex-api-map.yaml`
- `docs/reference/oracle-apex-api-packages.md`
""";

    private static string OracleApexReferenceIndexDoc() => """
## Oracle APEX Reference Index

Purpose:
Curated starting points for Oracle APEX Builder, administration, and deployment workflows.

Intended use:

- onboarding to Oracle APEX workspaces
- locating official Builder and admin guidance quickly
- helping agents choose authoritative Oracle references first

Primary documentation:

- Oracle APEX App Builder User's Guide: https://docs.oracle.com/en/database/oracle/apex/24.2/htmdb/
- Oracle APEX Administration Guide: https://docs.oracle.com/en/database/oracle/apex/24.2/aeadm/
- Oracle APEX Installation Guide: https://docs.oracle.com/en/database/oracle/apex/24.2/htmig/

Recommended sections:

- Getting Started
- Creating Applications
- Pages and Regions
- Items and Validations
- Shared Components
- Security and Authentication
- Deployment and Export

Navigation hints for agents:

- use App Builder docs for page, region, item, and process questions
- use Administration docs for workspace and instance topics
- use Installation docs for runtime provisioning questions
""";

    private static string OracleApexBooksDoc() => """
## Oracle APEX Books

Purpose:
Curated entry points into Oracle APEX book-style documentation so users and agents can choose the right official guide quickly.

When to use:

- starting from a broad Oracle APEX topic instead of a specific package or page feature
- selecting between Builder, administration, installation, API, and release-note guides
- onboarding when the official documentation layout matters first

Official Oracle URLs:

- Oracle APEX documentation landing page: https://docs.oracle.com/en/database/oracle/apex/
- Oracle APEX 24.2 documentation set: https://docs.oracle.com/en/database/oracle/apex/24.2/
- Oracle APEX API Reference 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/
- Oracle APEX App Builder User's Guide 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/htmdb/
- Oracle APEX Administration Guide 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/aeadm/
- Oracle APEX Installation Guide 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/htmig/

Common troubleshooting scenarios:

- you know the product area but not the exact document name
- you need to separate Builder, admin, install, and API questions
- you need version-aware book selection before quoting guidance

Related Oracle documents:

- `docs/reference/oracle-apex-index.md`
- `docs/reference/oracle-apex-api-reference.md`
- `docs/reference/oracle-apex-version-archives.md`
""";

    private static string OracleApexApiReferenceDoc() => """
## Oracle APEX API Reference

Purpose:
Curated entry points for Oracle APEX PL/SQL package documentation and API-oriented troubleshooting.

When to use:

- looking up APEX PL/SQL packages such as `APEX_UTIL`, `APEX_JSON`, or `APEX_WEB_SERVICE`
- deciding which package family fits a runtime task
- checking official API docs before implementing helper code around APEX internals

Official Oracle URLs:

- Oracle APEX API Reference 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/
- Oracle APEX API Reference 24.2 package index: https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/toc.htm
- Oracle APEXlang 26.1 docs for application definitions: https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/

Common troubleshooting scenarios:

- package name known, behavior unknown
- multiple packages appear relevant and you need the official package family first
- runtime code uses an APEX package and the task requires version-aware confirmation

Related Oracle documents:

- `docs/reference/oracle-apex-api-packages.md`
- `docs/reference/oracle-apex-api-map.yaml`
- `docs/reference/oracle-plsql-index.md`
- `docs/reference/oracle-apex-version-archives.md`
""";

    private static string OracleApexAdministrationDoc() => """
## Oracle APEX Administration Reference

Purpose:
Curated official entry points for Oracle APEX workspace, instance, and security administration.

When to use:

- workspace creation and instance administration tasks
- authentication, authorization, and security policy questions
- operational troubleshooting specific to APEX administration

Official Oracle URLs:

- Oracle APEX Administration Guide 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/aeadm/
- Oracle APEX App Builder User's Guide 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/htmdb/
- Oracle Database Security Guide 23: https://docs.oracle.com/en/database/oracle/oracle-database/23/dbseg/

Common troubleshooting scenarios:

- workspace provisioning succeeded but the expected APEX admin step is unclear
- authentication behavior needs confirmation from official docs
- instance-level settings and workspace-level settings are being confused

Related Oracle documents:

- `docs/reference/oracle-apex-index.md`
- `docs/reference/oracle-apex-installation.md`
- `docs/reference/oracle-database-index.md`
""";

    private static string OracleApexInstallationDoc() => """
## Oracle APEX Installation Reference

Purpose:
Curated entry points for Oracle APEX installation, upgrade, and runtime setup topics.

When to use:

- local runtime provisioning questions
- installation and upgrade planning
- validating version-matched setup steps against official Oracle guidance

Official Oracle URLs:

- Oracle APEX Installation Guide 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/htmig/
- Oracle APEX download page: https://www.oracle.com/tools/downloads/apex-downloads.html
- Oracle REST Data Services docs: https://docs.oracle.com/en/database/oracle/oracle-rest-data-services/25.1/orddg/

Common troubleshooting scenarios:

- the runtime version is known but the installation steps need confirmation
- ORDS and APEX setup responsibilities need to be separated clearly
- users need to verify that local provisioning does not imply repository redistribution of Oracle media

Related Oracle documents:

- `docs/reference/oracle-apex-index.md`
- `docs/reference/oracle-apex-version-archives.md`
- `docs/reference/oracle-ords-index.md`
""";

    private static string OracleApexReleaseNotesDoc() => """
## Oracle APEX Release Notes Reference

Purpose:
Curated official entry points for Oracle APEX release notes and version-specific behavior changes.

When to use:

- checking whether a feature or package changed between versions
- confirming new capabilities, deprecations, or upgrade notes
- investigating behavior differences between the local runtime and the latest docs

Official Oracle URLs:

- Oracle APEX Release Notes 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/htmrn/
- Oracle APEX documentation landing page: https://docs.oracle.com/en/database/oracle/apex/

Common troubleshooting scenarios:

- documentation examples do not match the deployed runtime
- a package or UI feature appears in one version but not another
- an upgrade path needs official version notes before changing workspace guidance

Related Oracle documents:

- `docs/reference/oracle-apex-version-archives.md`
- `docs/reference/oracle-apex-api-reference.md`
- `docs/reference/oracle-apex-installation.md`
""";

    private static string OracleApexVersionArchivesDoc() => """
## Oracle APEX Version Archives

Purpose:
Help users and agents locate Oracle documentation that matches the runtime version they are actually using.

Use version-matched documentation whenever possible.

Current version documentation:

- Oracle APEX 24.2 docs: https://docs.oracle.com/en/database/oracle/apex/24.2/
- Oracle APEX API Reference 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/
- Oracle APEX Release Notes 24.2: https://docs.oracle.com/en/database/oracle/apex/24.2/htmrn/

Previous version documentation:

- Oracle APEX 23.2 docs: https://docs.oracle.com/en/database/oracle/apex/23.2/
- Oracle APEX 23.1 docs: https://docs.oracle.com/en/database/oracle/apex/23.1/
- Oracle APEX 22.2 docs: https://docs.oracle.com/en/database/oracle/apex/22.2/

Archive landing page:

- Oracle APEX documentation landing page: https://docs.oracle.com/en/database/oracle/apex/

Navigation hints for agents:

- identify the runtime version before quoting package behavior or UI structure
- prefer version-matched APEX and API docs over the latest release when they differ
- use release notes to explain cross-version differences
""";

    private static string OracleApexApiMapYaml() => """
runtime:
  - APEX_APPLICATION
  - APEX_UTIL
  - APEX_SESSION

json:
  - APEX_JSON

collections:
  - APEX_COLLECTION

rest:
  - APEX_WEB_SERVICE
  - APEX_EXEC

mail:
  - APEX_MAIL

files:
  - APEX_DATA_EXPORT
  - APEX_DATA_PARSER

workflow:
  - APEX_WORKFLOW
  - APEX_HUMAN_TASK

plugins:
  - APEX_PLUGIN
  - APEX_JAVASCRIPT

security:
  - APEX_ACL
  - APEX_AUTHENTICATION

ai:
  - APEX_AI
""";

    private static string OracleApexApiPackagesDoc() => """
## Oracle APEX API Packages

Purpose:
Quick package discovery catalog for Oracle APEX runtime development without copying Oracle documentation text.

Use this catalog to identify likely package families, then open the official Oracle package page.

Package: `APEX_APPLICATION`

Use when:

- reading application request context
- working with page state and request flow
- investigating runtime execution context

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_APPLICATION.html

Package: `APEX_UTIL`

Use when:

- common APEX utility operations are needed
- session, URL, and helper workflows are involved
- looking for general-purpose APEX runtime helpers

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_UTIL.html

Package: `APEX_SESSION`

Use when:

- creating or attaching APEX session context programmatically
- background or integration code needs explicit session handling

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_SESSION.html

Package: `APEX_JSON`

Use when:

- parsing JSON
- generating JSON
- REST integrations

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_JSON.html

Package: `APEX_COLLECTION`

Use when:

- temporary collection storage is needed inside APEX runtime workflows
- wizard-style or session-scoped data staging is involved

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_COLLECTION.html

Package: `APEX_WEB_SERVICE`

Use when:

- calling external web services from APEX PL/SQL
- SOAP or REST-style integration code needs official package guidance

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_WEB_SERVICE.html

Package: `APEX_EXEC`

Use when:

- data source execution or remote data access patterns are involved
- modern data access helpers are needed inside APEX code

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_EXEC.html

Package: `APEX_MAIL`

Use when:

- sending email from APEX
- checking queue or notification workflows

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_MAIL.html

Package: `APEX_DATA_EXPORT`

Use when:

- generating downloadable export files
- export workflows require official package guidance

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_DATA_EXPORT.html

Package: `APEX_DATA_PARSER`

Use when:

- parsing uploaded file content
- spreadsheet or CSV ingestion workflows are involved

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_DATA_PARSER.html

Package: `APEX_WORKFLOW`

Use when:

- workflow execution or inspection is part of the application design

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_WORKFLOW.html

Package: `APEX_HUMAN_TASK`

Use when:

- human task orchestration features are involved

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_HUMAN_TASK.html

Package: `APEX_PLUGIN`

Use when:

- plugin development or plugin runtime hooks are involved

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_PLUGIN.html

Package: `APEX_JAVASCRIPT`

Use when:

- server-generated JavaScript helpers or JS integration hooks are involved

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_JAVASCRIPT.html

Package: `APEX_ACL`

Use when:

- access control list features are part of application security

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_ACL.html

Package: `APEX_AUTHENTICATION`

Use when:

- authentication flows or custom authentication helpers are under review

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_AUTHENTICATION.html

Package: `APEX_AI`

Use when:

- AI-assisted features or related package capabilities need official version-aware review

Reference:

- https://docs.oracle.com/en/database/oracle/apex/24.2/aeapi/APEX_AI.html
""";

    private static string OracleKnowledgeMapYaml() => """
apex:
  specification:
    - oracle-apexlang-index.md
    - oracle-apexlang-navigation.md
  books:
    - oracle-apex-books.md
  runtime:
    - oracle-apex-api-reference.md
    - oracle-apex-api-packages.md
    - oracle-apex-api-map.yaml
  administration:
    - oracle-apex-administration.md
  installation:
    - oracle-apex-installation.md
  release_notes:
    - oracle-apex-release-notes.md
  version_archives:
    - oracle-apex-version-archives.md

ords:
  deployment:
    - oracle-ords-index.md

database:
  sql:
    - oracle-plsql-index.md
    - oracle-database-index.md
""";

    private static string OracleApexLangReferenceIndexDoc() => """
## Oracle APEXlang Reference Index

Purpose:
Structured entry point for Oracle APEXlang, Oracle's Open Application Specification Language for Oracle APEX.

Intended use:

- reviewing source-controlled Oracle APEX application definitions
- helping agents navigate APEXlang structure before editing `.apx` files
- comparing Builder concepts with exported application specifications

Primary documentation:

- Oracle APEXlang documentation: https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/

Recommended sections:

- Introduction
- Application Definition
- Pages
- Regions
- Items
- Processes
- Authentication
- Authorization
- Shared Components

Navigation hints for agents:

- start here before editing `apex/application.apx`
- use `docs/reference/oracle-apexlang-navigation.md` for the major-section map
- switch to general Oracle APEX docs for Builder questions outside the application definition format
""";

    private static string OracleApexLangNavigationDoc() => """
## Oracle APEXlang Navigation

Official documentation:
https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/

### Core Concepts

- Application Definition
- Pages
- Regions
- Items
- Processes
- Shared Components

### Security

- Authentication
- Authorization

### Deployment

- Import
- Export
- Versioning

### Agent Hints

- use this file as a short map, then open the official Oracle page for details
- prefer APEXlang docs before Builder docs when the source artifact is an `.apx` file
""";

    private static string OracleOrdsReferenceIndexDoc() => """
## Oracle ORDS Reference Index

Purpose:
Curated entry points for Oracle REST Data Services configuration, deployment, and REST enablement topics.

Intended use:

- ORDS onboarding for local Oracle workspaces
- deployment and runtime troubleshooting
- REST and gateway questions that should use Oracle documentation first

Primary documentation:

- Oracle REST Data Services Installation, Configuration, and Development Guide: https://docs.oracle.com/en/database/oracle/oracle-rest-data-services/25.1/orddg/

Recommended sections:

- Installing and Configuring ORDS
- Command-Line Configuration
- Database Connection Setup
- REST-Enabled SQL
- Enabling REST for Schemas and Objects
- Troubleshooting and Logging

Navigation hints for agents:

- use ORDS docs first for REST, URL, deployment, and runtime topics
- combine with database docs when the issue crosses runtime and schema boundaries
""";

    private static string OraclePlSqlReferenceIndexDoc() => """
## Oracle PL/SQL Reference Index

Purpose:
Curated official references for PL/SQL language, packages, stored procedures, triggers, and error handling.

Intended use:

- implementing and reviewing PL/SQL code
- answering language and package behavior questions
- helping agents stay on official Oracle semantics for procedural work

Primary documentation:

- PL/SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/lnpls/
- PL/SQL Packages and Types Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/arpls/
- Database Error Messages: https://docs.oracle.com/en/database/oracle/oracle-database/23/errmg/

Recommended sections:

- Procedures and Functions
- Packages
- Triggers
- Dynamic SQL
- Exception Handling

Navigation hints for agents:

- use the language reference for syntax and semantics
- use the packages reference for built-in package behavior
- use the error messages reference when the task includes `ORA-` diagnostics
""";

    private static string OracleDatabaseReferenceIndexDoc() => """
## Oracle Database Reference Index

Purpose:
Curated official starting points for Oracle Database concepts, SQL, administration, and security.

Intended use:

- grounding Oracle workspace work in official database documentation
- answering schema, SQL, storage, and admin questions
- helping agents separate database concerns from APEX and ORDS concerns

Primary documentation:

- Oracle Database Concepts: https://docs.oracle.com/en/database/oracle/oracle-database/23/cncpt/
- Oracle Database SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/sqlrf/
- Oracle Database Administrator's Guide: https://docs.oracle.com/en/database/oracle/oracle-database/23/admin/
- Oracle Database Security Guide: https://docs.oracle.com/en/database/oracle/oracle-database/23/dbseg/

Recommended sections:

- Architecture and Multitenant Concepts
- Users, Schemas, and Privileges
- SQL Statements and Data Definition
- Transactions and Concurrency
- Security Fundamentals

Navigation hints for agents:

- use the SQL Language Reference for DDL, DML, and query behavior
- use Concepts for terminology and architecture questions
- use Admin or Security guides for operational setup and privilege topics
""";

    private static string OracleApexSkillDoc() => """
# Oracle APEX Skill

Purpose:
Guide Oracle APEX Builder, workspace administration, and application review work toward official Oracle references.

When to use:

- creating or reviewing Oracle APEX applications
- answering Builder questions about pages, regions, items, or processes
- checking administration or deployment guidance for APEX workspaces

Recommended documentation indexes:

- `docs/reference/oracle-apex-index.md`
- `docs/reference/oracle-apexlang-index.md` when the artifact is an `.apx` definition
- `docs/reference/oracle-ords-index.md` for ORDS integration topics

Common workflows:

- open the APEX index first
- choose App Builder, Administration, or Installation guidance based on the task
- switch to APEXlang when reviewing source-controlled application definitions

Documentation discovery workflow:

- start at `docs/reference/oracle-knowledge-map.yaml`
- open `docs/reference/oracle-apex-books.md` when you need the right Oracle book first
- switch to `docs/reference/oracle-apex-api-reference.md` for PL/SQL package work

Package lookup workflow:

- identify the package family in `docs/reference/oracle-apex-api-map.yaml`
- confirm the package entry in `docs/reference/oracle-apex-api-packages.md`
- open the official Oracle API deep link for final confirmation

Version compatibility guidance:

- use `docs/reference/oracle-apex-version-archives.md` when the runtime is not on the latest APEX release
- prefer version-matched API and release-note docs before claiming package availability

Troubleshooting guidance:

- use administration docs for workspace and security issues
- use installation docs for runtime provisioning and upgrade issues
- use release notes when behavior differs across APEX versions

Official documentation:

- https://docs.oracle.com/en/database/oracle/apex/24.2/htmdb/
- https://docs.oracle.com/en/database/oracle/apex/24.2/aeadm/
- https://docs.oracle.com/en/database/oracle/apex/24.2/htmig/
""";

    private static string OracleApexLangSkillDoc() => """
# Oracle APEXlang Skill

Purpose:
Help humans and agents navigate Oracle APEXlang before editing or reviewing application specification files.

When to use:

- working with `apex/application.apx`
- reviewing source-controlled Oracle APEX application definitions
- mapping Builder concepts to APEXlang sections

Recommended documentation indexes:

- `docs/reference/oracle-apexlang-index.md`
- `docs/reference/oracle-apexlang-navigation.md`
- `docs/reference/oracle-apex-index.md` for Builder concepts outside the specification format

Common workflows:

- inspect the workspace index before planning a change
- consult `.opencode/skills/apexlang/references/syntax-basics.md` and the other local syntax references before editing source
- use `.opencode/knowledge/apex-developers-companion/prompts/compact-context.md` for conceptual and workflow guidance when the exact language reference is not enough
- build a semantic plan and review assumptions, warnings, unresolved questions, affected symbols, and expected files
- require explicit approval for destructive or potentially conflicting plans
- execute APEXlang changes only through the semantic planner, code action service, or semantic editor workflow
- do not edit raw `.apx` text directly when the semantic workflow supports the requested change

Documentation discovery workflow:

- start at `docs/reference/oracle-knowledge-map.yaml`
- open `docs/reference/oracle-apexlang-index.md`
- use `docs/reference/oracle-apexlang-navigation.md` for fast section discovery

Package lookup workflow:

- if the change also touches runtime PL/SQL packages, switch to `docs/reference/oracle-apex-api-reference.md`
- use `docs/reference/oracle-apex-api-map.yaml` to classify package families before editing helper code

Version compatibility guidance:

- use version-matched APEXlang and APEX docs whenever the runtime version is known
- check `docs/reference/oracle-apex-version-archives.md` if exported structures differ from the latest examples

Troubleshooting guidance:

- if a concept is hard to place, map it back to Builder terminology with `docs/reference/oracle-apex-index.md`
- use release notes when structure or naming appears version-specific
- for real source-shape questions, retrieve the smallest relevant local reference first:
  - `.opencode/skills/apexlang/references/identifiers-and-scopes.md`
  - `.opencode/skills/apexlang/references/component-references.md`
  - `.opencode/skills/apexlang/references/embedded-languages.md`

Official documentation:

- https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/
- https://docs.oracle.com/en/database/oracle/apex/26.1/apxdc/reading-apexlang-syntax.html
""";

    private static string ReadingApexlangSyntaxSource() => """
## Component Syntax
<p>Components use a block form with a component type, optional identifier, and parentheses. Properties inside the block may be omitted when Builder defaults are acceptable.</p>
<p>Arrays use repeated child entries or grouped values instead of ad hoc inline syntax. Source files usually follow exported naming conventions such as application roots, page files, and shared component folders.</p>

## Identifiers And Scope
<p>Identifiers name components within their local scope. Page items, regions, navigation entries, and shared components have different uniqueness boundaries, so reuse is allowed only where the owning scope differs.</p>

## Component References
<p>Use <code>@</code> for local component references and <code>@/</code> for Global Page or Universal Theme references. Keep the symbolic reference form so later export, review, and validation can resolve the intended target.</p>

## Properties And Property Groups
<p>Properties use <code>name: value</code>. Property groups keep related settings together and should stay structurally intact when editing exported source.</p>

## Embedded Languages
<p>Use fenced blocks for embedded SQL, PL/SQL, JavaScript, HTML, and CSS. Distinguish <code>javascript-browser</code> from <code>javascript-mle</code> because they execute in different runtimes.</p>
<pre><code class="language-sql">select *
from demo_customers;
</code></pre>
<pre><code class="language-plsql">begin
    null;
end;
</code></pre>
<pre><code class="language-javascript-browser">apex.message.showPageSuccess("Saved");
</code></pre>
<pre><code class="language-javascript-mle">export function transform(row) {
  return row;
}
</code></pre>
<pre><code class="language-html"><div class="hero">Hello</div>
</code></pre>
<pre><code class="language-css">.hero { color: #123456; }
</code></pre>
""";

    private static string OracleOrdsSkillDoc() => """
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
""";

    private static string OraclePlSqlSkillDoc() => """
# Oracle PL/SQL Skill

Purpose:
Keep Oracle PL/SQL implementation, review, and debugging work anchored to official language and package references.

When to use:

- explaining, refactoring, or debugging PL/SQL
- validating syntax, package usage, and trigger behavior
- investigating `ORA-` errors in procedural code

Recommended documentation indexes:

- `docs/reference/oracle-plsql-index.md`
- `docs/reference/oracle-database-index.md`

Common workflows:

- check language semantics in the PL/SQL Language Reference
- confirm built-in package behavior in the Packages and Types Reference
- cross-check `ORA-` messages in the Error Messages reference

Official documentation:

- https://docs.oracle.com/en/database/oracle/oracle-database/23/lnpls/
- https://docs.oracle.com/en/database/oracle/oracle-database/23/arpls/
- https://docs.oracle.com/en/database/oracle/oracle-database/23/errmg/
""";

    private static string OracleDatabaseSkillDoc() => """
# Oracle Database Skill

Purpose:
Route schema, SQL, administration, and security questions to official Oracle Database references.

When to use:

- implementing or reviewing SQL and schema changes
- answering user, privilege, and operational questions
- separating core database concerns from APEX and ORDS concerns

Recommended documentation indexes:

- `docs/reference/oracle-database-index.md`
- `docs/reference/oracle-plsql-index.md` for procedural code

Common workflows:

- start with SQL Language Reference for query and DDL questions
- use Concepts for terminology and architecture questions
- use Admin or Security guides for operational setup and privilege topics

Official documentation:

- https://docs.oracle.com/en/database/oracle/oracle-database/23/cncpt/
- https://docs.oracle.com/en/database/oracle/oracle-database/23/sqlrf/
- https://docs.oracle.com/en/database/oracle/oracle-database/23/admin/
- https://docs.oracle.com/en/database/oracle/oracle-database/23/dbseg/
""";

    private static string UpdateOracleDocIndexPowerShellScript() => """
# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES
# Source inputs: workspace.yaml and catalog manifests under catalog/.
# User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.
param()

Write-Host "Oracle documentation index update placeholder"
Write-Host "This script must only manage repository-owned metadata and official links."
Write-Host "Allowed future work: refresh curated links, validate links, generate navigation summaries."
Write-Host "Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content."
""";

    private static string UpdateOracleDocIndexShellScript() => """
set -euo pipefail

printf '%s\n' 'Oracle documentation index update placeholder'
printf '%s\n' 'This script must only manage repository-owned metadata and official links.'
printf '%s\n' 'Allowed future work: refresh curated links, validate links, generate navigation summaries.'
printf '%s\n' 'Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content.'
""";

    private static string UpdateOracleNavigationIndexPowerShellScript() => """
# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES
# Source inputs: workspace.yaml and catalog manifests under catalog/.
# User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.
param()

Write-Host "Oracle navigation index update placeholder"
Write-Host "Allowed future work: validate links, detect broken links, refresh version references, generate package indexes."
Write-Host "Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content."
""";

    private static string UpdateOracleNavigationIndexShellScript() => """
set -euo pipefail

printf '%s\n' 'Oracle navigation index update placeholder'
printf '%s\n' 'Allowed future work: validate links, detect broken links, refresh version references, generate package indexes.'
printf '%s\n' 'Forbidden: downloading, mirroring, caching, or redistributing Oracle documentation content.'
""";

    private static string OraclePlSqlDemoDoc() => """
## Oracle PL/SQL Demo

Use this workspace when the goal is learning PL/SQL in a safe local Oracle environment with reproducible setup.

Read more:

- `docs/capabilities/oracle.md`
- `docs/capabilities/repository.md`

### Included

- Oracle Database Free
- SQLcl and SQL*Plus validation
- Sample schema and tutorial queries
- Oracle-focused OpenCode skills
- Shared `Customers / Products / Orders` sample domain

### First Steps

1. Start the workspace from the app.
2. Run `scripts/verify-oracle-demo.sh`.
3. Open `tutorial/oracle/START-HERE-ORACLE.md`.

### Onboarding Flow

```text
Clone Repository
    ↓
Open Repository
    ↓
Workspace Discovered
    ↓
Provision Environment
    ↓
Read Docs
    ↓
Run Tutorial
```

### Progression

This template is the first step in the Oracle onboarding path:

```text
Oracle PL/SQL Demo
    ↓
Oracle APEX Demo
    ↓
Oracle APEXlang Demo
```

## Try It Yourself

1. Run `scripts/verify-oracle-demo.sh`.
2. Open `tutorial/oracle/scripts/03-sample-queries.sql` in SQLcl.
3. Query `demo_customers`, `demo_products`, and `demo_order_summary_v`.
4. Execute `demo_customer_api.get_customer(1)` and `demo_customer_api.get_order_total(1)`.
5. Insert a new order and confirm the trigger recalculates `order_total`.

## Related Topics

- [Sample Domain](oracle-samples.md)
- [SQLcl](oracle-tools/sqlcl.md)
- [Data Pump](oracle-tools/data-pump.md)
- [SQL Developer](oracle-tools/sql-developer.md)
- [Schema Export / Import](oracle-tools/apex-export-import.md)
""";

    private static string OracleApexDemoDoc() => """
## Oracle APEX Demo

This workspace extends the Oracle PL/SQL demo with Oracle APEX and ORDS so onboarding can continue from database objects into local low-code application development.

Read more:

- `docs/capabilities/oracle.md`
- `docs/oracle-tools/README.md`

### Included

- Oracle Database Free
- Oracle APEX runtime through the database service when official Oracle APEX media is supplied
- Oracle REST Data Services at `http://localhost:8181/ords`
- SQLcl for database and APEX command-line workflows
- Sample `Customer Orders Demo` schema objects and sample data

### Oracle APEX Media

This workspace does not redistribute Oracle APEX media.

Before provisioning a real APEX runtime:

1. Download the official Oracle APEX ZIP from Oracle.
2. Place it under `.local/oracle/downloads/apex/`.
3. Rename it to `apex.zip`, or keep an official filename such as `apex_24.2_en.zip`.
4. Reprovision or start the workspace again.

Supported paths:

- `.local/oracle/downloads/apex/apex.zip`
- `.local/oracle/downloads/apex/apex_*.zip`
- `.local/oracle/downloads/apex/apex*.zip`

The repository does not include Oracle APEX media.

### Suggested Flow

1. Start the Oracle workspace.
2. Open ORDS with `scripts/open-ords.ps1`.
3. Open APEX Administration Services with `scripts/open-apex.ps1`.
4. Run `scripts/health-check-database.sh`, `scripts/health-check-ords.sh`, and `scripts/health-check-apex.sh` inside the workspace runtime.

This workspace focuses on the traditional Oracle APEX Builder workflow in a reproducible local onboarding environment.

## Customer Orders Demo

Use the shared sample domain to build a small `Customer Orders Demo` application:

- Interactive Report: orders list
- Interactive Grid: product maintenance
- Chart: orders by month
- Dashboard: customers, products, orders, total sales
- Export: CSV and Excel-style report export where supported by APEX

## Try It Yourself

1. Open ORDS and then open APEX Administration Services.
2. Sign in as the APEX instance administrator and create a workspace.
3. Create an orders report on `demo_order_summary_v`.
4. Add a product-maintenance grid on `demo_products`.
5. Add a chart for orders by month.
6. Export the report data as CSV.

## Related Topics

- [Sample Domain](oracle-samples.md)
- [ORDS](oracle-tools/ords.md)
- [APEX Export / Import](oracle-tools/apex-export-import.md)
- [SQL Developer](oracle-tools/sql-developer.md)

## Additional Learning Resources

For users who want a structured Slovenian introduction to traditional Oracle APEX development, see:

"Malokodno programiranje z APEX-om: Prirocnik s prakticnimi primeri"
University of Maribor

This open-access book covers practical Oracle APEX development using the traditional APEX Builder workflow and complements this workspace.

This workspace provides the reproducible environment and automation, while the book provides a guided learning path with practical examples.
""";

    private static string OracleApexLangDemoDoc() => """
## Oracle APEXlang Demo

This workspace demonstrates a source-controlled Oracle APEX workflow.

Read more:

- `docs/capabilities/oracle.md`
- `docs/oracle-tools/README.md`

Oracle now uses `APEXlang` as the name for the Open Application Specification Language introduced in APEX 26.1.

It extends Oracle APEX rather than replacing the Builder workflow.

This workspace demonstrates a source-controlled APEX workflow:

```text
APEX Builder
    ↓
Export
    ↓
Git
    ↓
Review
    ↓
Validate
    ↓
Import
    ↓
Deploy
```

### Included Structure

- `apex/application.apx`
- `exports/apexlang/hello-apexlang/`
- `scripts/export-apex.sh`
- `scripts/import-apex.sh`
- `scripts/validate-apex.sh`
- `scripts/apexlang-hello-world.sh`
- `docs/oracle-tools/apexlang-hello-world.md`
- `docs/apexlang-introduction.md`

Use the generated scripts as the official-tooling starting point for repeatable export, review, validation, and import.

This workspace also provisions a minimal `Hello APEXlang` application automatically and exports the resulting APEXlang package into `exports/apexlang/hello-apexlang/`.

## Try It Yourself

1. Run `scripts/apexlang-hello-world.sh`.
2. Review `exports/apexlang/hello-apexlang/`.
3. Validate the exported package with `scripts/validate-apex.sh exports/apexlang/hello-apexlang/application.apx`.
4. Review the exported changes in Git.
5. Re-import the application definition with `scripts/import-apex.sh exports/apexlang/hello-apexlang`.

## Related Topics

- [Sample Domain](oracle-samples.md)
- [APEXlang](oracle-tools/apexlang.md)
- [APEX Export / Import](oracle-tools/apex-export-import.md)
- [SQLcl](oracle-tools/sqlcl.md)
- [Team Onboarding](team-onboarding.md)
""";

    private static string OracleSamplesDoc() => """
## Oracle Samples

All Oracle demos use one shared sample domain:

```text
Customers
    ↓
Orders
    ↓
Products
```

### Objects

- `DEMO_CUSTOMERS`
- `DEMO_PRODUCTS`
- `DEMO_ORDERS`
- `DEMO_ORDER_SUMMARY_V`
- `DEMO_CUSTOMER_API`

### Progression

- Oracle PL/SQL Demo uses the domain to explain tables, views, procedures, triggers, and package APIs.
- Oracle APEX Demo uses the same domain for browser-based reports, grids, charts, and dashboards.
- Oracle APEXlang Demo uses the same application definition for export, validate, review, and import workflows.

### Learning Goals

1. find a customer
2. inspect products
3. create an order
4. review order totals
5. export and validate application changes
""";

    private static string TeamOnboardingDoc(OracleWorkspaceKind kind)
    {
        var lines = new List<string>
        {
            "## Team Onboarding",
            string.Empty,
            "The repository is the source of truth for Oracle workspace onboarding.",
            string.Empty,
            "### Expected Flow",
            string.Empty,
            "```text",
            "Clone Repository",
            "    ↓",
            "Open Existing Repository",
            "    ↓",
            "Workspace Discovered",
            "    ↓",
            "Review Configuration",
            "    ↓",
            "Provision Environment",
            "    ↓",
            "Read Docs",
            "    ↓",
            "Run Tutorial",
            "    ↓",
            "Start Learning",
            "```",
            string.Empty,
            "### Connecting to a Workspace Session",
            string.Empty,
            "Most onboarding exercises assume the user is connected to an OpenCode session rather than a root shell.",
            string.Empty,
            "Typical workflow:",
            string.Empty,
            "```bash",
            "su opencode",
            "cd /workspace",
            "opencode -s resume",
            "```",
            string.Empty,
            "Useful commands:",
            string.Empty,
            "```bash",
            "opencode sessions",
            "opencode -s <session-id>",
            "```",
            string.Empty,
            "Suggested first questions:",
            string.Empty,
            "- What capabilities are available?",
            "- What onboarding docs exist?",
            "- What tools are installed?",
            string.Empty,
            "### Using Docker Desktop Exec",
            string.Empty,
            "Docker Desktop Exec is a valid way to access a workspace.",
            string.Empty,
            "You may be attached to:",
            string.Empty,
            "- root shell",
            "- opencode user shell",
            "- OpenCode session",
            string.Empty,
            "OpenCode sessions provide the best onboarding experience.",
            string.Empty,
            "### Durable Inputs",
            string.Empty,
            "- `workspace.yaml`",
            "- `compose.yaml`",
            "- `.env.example`",
            "- `sql/`",
            "- `apex/`",
            "- `scripts/`",
            "- `docs/`",
            "- `AGENTS.md`",
            string.Empty,
            "### Capability Discovery",
            string.Empty,
            "Start here:",
            string.Empty,
            "- `docs/capabilities/README.md`",
            "- `docs/capabilities/oracle.md`",
            "- `docs/oracle-plsql-demo.md`",
        };

        if (kind is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang)
        {
            lines.Add("- `docs/oracle-apex-demo.md`");
        }

        if (kind == OracleWorkspaceKind.ApexLang)
        {
            lines.Add("- `docs/oracle-apexlang-demo.md`");
        }

        lines.Add("- `docs/oracle-tools/README.md`");
        lines.Add("- `docs/oracle-samples.md`");
        lines.Add("- `docs/troubleshooting/workspace-sessions.md`");
        lines.Add(string.Empty);
        lines.Add("No manual recreation of Oracle settings should be required when the repository already contains those files.");

        return string.Join("\n", lines);
    }

    private static string OracleLifecycleWorkflowsDoc(OracleWorkspaceKind kind) => $"""
## Oracle Lifecycle Workflows

This workspace favors official Oracle tooling and readable repository artifacts over custom deployment formats.

### End-To-End Flow

```text
Local Development
    ↓
APEX Builder
    ↓
Export
    ↓
Validate
    ↓
Git Review
    ↓
Import
    ↓
Deploy
```

### Database Lifecycle

- Export Schema: `scripts/export-schema.sh`
- Import Schema: `scripts/import-schema.sh`
- Export Data Pump: `scripts/export-datapump.sh`
- Import Data Pump: `scripts/import-datapump.sh`

Data Pump becomes useful when you need onboarding snapshots, environment refreshes, or schema backups rather than day-to-day object-level review.

### APEX Lifecycle

- Export Application: `scripts/export-apex.sh`
- Import Application: `scripts/import-apex.sh`
- Validate APEXlang: `scripts/validate-apex.sh`
- Export APEXlang: `scripts/export-apexlang.sh`

Use APEX export/import for application movement, and APEXlang when you want the same application definition to become reviewable and automatable in Git.

### Local Onboarding vs Shared Staging

Local onboarding should happen first. Shared staging should be introduced after a developer can already explain the schema, the application, and the export/import path safely.

### Read More

- [SQLcl](oracle-tools/sqlcl.md)
- [Data Pump](oracle-tools/data-pump.md)
- [ORDS](oracle-tools/ords.md)
- [APEX Export / Import](oracle-tools/apex-export-import.md)
- [APEXlang](oracle-tools/apexlang.md)

### Workspace Kind

Current Oracle workspace kind: `{kind}`
""";

    private static string SharingOracleWorkspacesDoc(OracleWorkspaceKind kind) => $"""
## Sharing Oracle Workspaces

Oracle workspaces are intended to be reproducible onboarding repositories.

### Why This Helps

- environment setup is scripted
- onboarding notes travel with the repository
- AGENTS.md captures local conventions
- generated scripts keep official Oracle commands visible

### Current Variant

This repository was generated as the `{kind}` Oracle workspace variant.
""";

    private static string ApexLangIntroductionDoc() => """
## APEXlang Introduction

APEXlang keeps APEX application changes reviewable in Git instead of leaving the full history only inside the builder.

Oracle refers to this language as the Open Application Specification Language, or APEXlang.

Use this workspace to practice:

1. change the app in APEX Builder
2. export the application
3. review the diff in Git
4. validate the exported source
5. import the same source back into a local runtime

The goal is not to replace Oracle tooling. The goal is to make official tooling easier to reproduce, share, and onboard.
""";

    private static string OracleToolsIndexDoc() => """
## Oracle Tools Index

This index catalogs Oracle tooling that is included directly or documented for onboarding.

| Tool | What It Is | Licensing Notes | Onboarding Relevance |
| --- | --- | --- | --- |
| [SQLcl](sqlcl.md) | Oracle command-line database and APEX automation client | Public Oracle tooling, used with Oracle Database Free | First tool to learn in PL/SQL onboarding |
| [Data Pump](data-pump.md) | Oracle schema and data export/import mechanism | Included with Oracle Database Free | Useful after basic schema understanding |
| [ORDS](ords.md) | Oracle REST Data Services and APEX delivery layer | Used with Oracle APEX and Oracle Database | Required for APEX onboarding |
| [APEX Export / Import](apex-export-import.md) | Official application export/import workflow | Included in APEX and SQLcl workflows | Needed for APEX and APEXlang progression |
| [APEXlang](apexlang.md) | Open Application Specification Language for Oracle APEX | Oracle APEX 26.1+ terminology and workflow guidance | Advanced source-control onboarding |
| [SQL Developer](sql-developer.md) | Oracle desktop IDE for browsing and querying | Documentation-only, separate local install | Optional visual learning path |
| Oracle APEX reporting features | Interactive reports, grids, charts, dashboards | Available within Oracle APEX | Introduced through the Oracle APEX Demo |
""";

    private static string SqlclToolDoc() => """
## SQLcl

### What It Is

SQLcl is Oracle's command-line SQL and scripting client for database work, script execution, and APEX-related automation.

### Why Someone Would Use It

- connect to the demo database quickly
- run schema setup scripts
- execute package procedures
- export APEX applications
- automate repeatable onboarding steps

### How It Fits Into The Demo

- Oracle PL/SQL Demo -> first interactive tool for schema exploration and procedures
- Oracle APEX Demo -> supports database checks behind the browser workflow
- Oracle APEXlang Demo -> supports export/import automation around the application lifecycle

### Example Commands

```bash
sql demo_user/demo_password@//oracle-demo:1521/FREEPDB1
@tutorial/oracle/scripts/03-sample-queries.sql
exec demo_customer_api.get_customer(1)
```

### Relationship To Other Tools

- SQLcl executes scripts and database automation
- Data Pump moves schema/data in larger units
- APEX export/import moves application definitions

### Licensing Notes

- public Oracle tooling
- used with Oracle Database Free
- no separate demo-specific license assumed here

### Onboarding Exercise

1. connect with SQLcl
2. run the sample queries script
3. execute `demo_customer_api.get_order_total(1)`
4. add one order and rerun the summary view query
""";

    private static string DataPumpToolDoc() => """
## Data Pump

### What It Is

Data Pump (`expdp` / `impdp`) is Oracle's standard schema and data export/import mechanism.

### Why Someone Would Use It

- create onboarding snapshots
- refresh a local test environment
- move schema/data between Oracle instances
- create repeatable backup or restore exercises

### How It Fits Into The Demo

- Oracle PL/SQL Demo -> schema backup and restore example
- Oracle APEX Demo -> move supporting data separately from the application
- Oracle APEXlang Demo -> keep schema/data movement separate from source-controlled application definition

### Example Commands

```bash
expdp demo_user/demo_password@FREEPDB1 schemas=DEMO_USER directory=DATA_PUMP_DIR dumpfile=demo_user.dmp logfile=demo_user-exp.log
impdp demo_user/demo_password@FREEPDB1 schemas=DEMO_USER directory=DATA_PUMP_DIR dumpfile=demo_user.dmp logfile=demo_user-imp.log
```

### Relationship To Other Tools

- Data Pump exports schema/data
- APEX Export exports application definitions
- APEXlang makes application definitions reviewable in Git

### Licensing Notes

- included with Oracle Database Free database tooling
- no separate APEX-specific license needed for the concept

### Onboarding Exercise

1. review the generated Data Pump scripts
2. export the demo schema
3. drop or truncate a demo object in a disposable environment
4. import the dump and verify the object returns
""";

    private static string OrdsToolDoc() => """
## ORDS

### What It Is

Oracle REST Data Services (ORDS) is the HTTP delivery layer that serves Oracle APEX and Oracle REST endpoints.

### Why Someone Would Use It

- reach the APEX login page
- validate that browser-based Oracle development is available
- troubleshoot local HTTP access to Oracle services
- verify application URLs before onboarding sessions

### How It Fits Into The Demo

- Oracle APEX Demo -> required delivery layer for APEX Builder
- Oracle APEXlang Demo -> same delivery layer while export/import stays source-controlled

### Example Commands

```bash
curl -fsSL http://localhost:8181/ords
 curl -fsSL http://localhost:8181/ords/apex
```

### Relationship To Other Tools

- ORDS serves APEX over HTTP
- SQLcl handles command-line database work
- APEX export/import moves the application definition behind that runtime

### Licensing Notes

- documented as part of Oracle APEX onboarding
- used with Oracle Database Free and Oracle APEX in this demo family

### Onboarding Exercise

1. run the ORDS health-check script
2. open the ORDS base URL
3. open the APEX login URL
4. compare the generated URLs with the `.env` values
""";

    private static string ApexExportImportToolDoc() => """
## APEX Export / Import

### What It Is

APEX export/import is Oracle's official workflow for moving an application definition between environments.

### Why Someone Would Use It

- back up the current application definition
- move an application between local environments
- validate application changes before deployment
- hand the application to another teammate or environment

### How It Fits Into The Demo

- Oracle APEX Demo -> introduces export/import as the next step after Builder basics
- Oracle APEXlang Demo -> uses the same workflow as the source-controlled lifecycle foundation

### Example Commands

```bash
scripts/export-apex.sh
scripts/validate-apex.sh
scripts/import-apex.sh
```

### Relationship To Other Tools

- APEX export/import moves application definitions
- Data Pump moves schema/data
- APEXlang adds reviewable source-control workflow around application exports

### Licensing Notes

- included in Oracle APEX workflow guidance
- implemented here through SQLcl-oriented scripts

### Onboarding Exercise

1. export the Customer Orders Demo application
2. validate the exported file
3. review the exported artifact in Git
4. re-import the same application into the local environment
""";

    private static string ApexLangToolDoc() => """
## APEXlang

### What It Is

APEXlang is Oracle's Open Application Specification Language for Oracle APEX applications.

### Why Someone Would Use It

- review application changes in Git
- automate validation around exported application definitions
- make onboarding repositories more self-describing
- support AI-assisted work with readable application definitions

### How It Fits Into The Demo

- Oracle APEXlang Demo -> advanced stage after PL/SQL and APEX basics
- provisions and exports a minimal `Hello APEXlang` application automatically

### Example Commands

```bash
scripts/apexlang-hello-world.sh
scripts/validate-apex.sh exports/apexlang/hello-apexlang/application.apx
scripts/import-apex.sh exports/apexlang/hello-apexlang
```

### Relationship To Other Tools

- APEXlang complements APEX Builder
- APEX export/import is still the official movement path
- SQLcl remains the automation entry point

### Licensing Notes

- terminology and workflow guidance tied to Oracle APEX
- no separate licensing claim made here beyond the APEX environment itself

### Onboarding Exercise

1. run `scripts/apexlang-hello-world.sh`
2. open `exports/apexlang/hello-apexlang/pages/p00001-home.apx`
3. validate the export
4. review the diff in Git
5. re-import the application definition locally
""";

    private static string ApexLangHelloWorldDoc() => """
## APEXlang Hello World

This workspace provisions a minimal APEXlang application automatically for the `oracle-apexlang-demo` template.

Resulting package:

- `exports/apexlang/hello-apexlang/`

The workflow uses the safe SQLcl launcher only:

- `/workspace/scripts/sqlcl.sh`
- `scripts/sqlcl.sh`

Do not call the broken SQLcl symlink path directly.

### Verify SQLcl

```bash
scripts/sqlcl.sh -version
```

Expected output contains:

```text
SQLcl: Release 26.1.2.0 Production Build: 26.1.2.132.1334
```

### Verify APEX 26.1 Registry State

Run from the Windows host:

```powershell
docker exec <workspace-container> bash -lc "sqlplus -S 'sys/${ORACLE_PASSWORD}@//oracle-demo:1521/FREEPDB1 as sysdba' <<'SQL'
SET PAGESIZE 100
SET LINESIZE 200
COLUMN comp_id FORMAT A10
COLUMN comp_name FORMAT A40
COLUMN version FORMAT A20
COLUMN status FORMAT A12
SELECT comp_id, comp_name, version, status
FROM dba_registry
WHERE comp_id = 'APEX';
EXIT
SQL"
```

Expected output contains:

```text
APEX       Oracle APEX      26.1.0    VALID
```

### Provision Or Refresh Hello APEXlang

Inside the workspace container:

```bash
scripts/apexlang-hello-world.sh
```

From Windows PowerShell:

```powershell
./scripts/apexlang-hello-world.ps1
```

Expected output contains:

```text
[oracle-apexlang] SQLcl verified.
[oracle-apexlang] APEX registry verified: APEX|Oracle APEX|26.1.0|VALID
[oracle-apexlang] Hello APEXlang package generated.
[oracle-apexlang] Hello APEXlang package validated.
[oracle-apexlang] Hello APEXlang application imported.
[oracle-apexlang] Hello APEXlang package exported.
```

### Validate The Result

```bash
scripts/sqlcl.sh -S testschema/<testschema-password>@//oracle-demo:1521/FREEPDB1 @sql/hello-apexlang/validate-hello-apexlang.sql
```

Verify the exported page file:

```text
exports/apexlang/hello-apexlang/pages/p00001-home.apx
```

Expected region block:

```text
region app-name (
    name: Home
    title: Hello from APEXlang
    type: staticContent
```
""";

    private static string SqlDeveloperToolDoc() => """
## SQL Developer

### What It Is

SQL Developer is Oracle's desktop IDE for browsing schema objects, running queries, and executing PL/SQL visually.

### Why Someone Would Use It

- browse tables and views visually
- run ad hoc SQL queries
- inspect package specs and bodies
- help first-time Oracle users who prefer a GUI over SQLcl first

### How It Fits Into The Demo

- Oracle PL/SQL Demo -> optional visual path for exploring `DEMO_CUSTOMERS`, `DEMO_PRODUCTS`, `DEMO_ORDERS`, and `DEMO_ORDER_SUMMARY_V`
- Oracle APEX Demo -> useful for checking schema objects behind the browser application

### Example Commands

```text
Host: localhost
Port: 1521
Service: FREEPDB1
Username: demo_user
Password: demo_password
```

### Relationship To Other Tools

- SQL Developer is optional and GUI-oriented
- SQLcl is the scripted and automation-friendly path
- APEX Builder handles browser application design

### Licensing Notes

- documentation-only in this demo family
- not a provisioning dependency
- separate local install by the developer

### Onboarding Exercise

1. connect using the generated demo connection details
2. browse the three demo tables
3. query `DEMO_ORDER_SUMMARY_V`
4. inspect `DEMO_CUSTOMER_API`
""";

    private static string CustomersSchemaSql() => """
CREATE TABLE demo_customers (
    customer_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    customer_name VARCHAR2(200) NOT NULL,
    email_address VARCHAR2(320) NOT NULL UNIQUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE TABLE demo_products (
    product_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    product_name VARCHAR2(200) NOT NULL,
    unit_price NUMBER(10,2) NOT NULL,
    active_flag CHAR(1) DEFAULT 'Y' NOT NULL CHECK (active_flag IN ('Y', 'N'))
);

CREATE TABLE demo_orders (
    order_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    customer_id NUMBER NOT NULL REFERENCES demo_customers(customer_id),
    product_id NUMBER NOT NULL REFERENCES demo_products(product_id),
    quantity NUMBER(10) NOT NULL,
    order_total NUMBER(12,2) NOT NULL,
    status_code VARCHAR2(30) DEFAULT 'NEW' NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE OR REPLACE VIEW demo_order_summary_v AS
SELECT o.order_id,
       c.customer_name,
       p.product_name,
       o.quantity,
       o.order_total,
       o.status_code,
       o.created_at
  FROM demo_orders o
  JOIN demo_customers c ON c.customer_id = o.customer_id
  JOIN demo_products p ON p.product_id = o.product_id;

CREATE OR REPLACE PACKAGE demo_customer_api AS
    PROCEDURE get_customer(p_customer_id IN demo_customers.customer_id%TYPE);
    PROCEDURE create_order(
        p_customer_id IN demo_orders.customer_id%TYPE,
        p_product_id IN demo_orders.product_id%TYPE,
        p_quantity IN demo_orders.quantity%TYPE,
        p_status_code IN demo_orders.status_code%TYPE DEFAULT 'NEW');
    FUNCTION get_order_total(p_order_id IN demo_orders.order_id%TYPE) RETURN NUMBER;
END demo_customer_api;
/

CREATE OR REPLACE PACKAGE BODY demo_customer_api AS
    PROCEDURE get_customer(p_customer_id IN demo_customers.customer_id%TYPE) AS
        l_customer_name demo_customers.customer_name%TYPE;
        l_email_address demo_customers.email_address%TYPE;
    BEGIN
        SELECT customer_name, email_address
          INTO l_customer_name, l_email_address
          FROM demo_customers
         WHERE customer_id = p_customer_id;

        DBMS_OUTPUT.PUT_LINE('Customer: ' || l_customer_name || ' <' || l_email_address || '>');
    END get_customer;

    PROCEDURE create_order(
        p_customer_id IN demo_orders.customer_id%TYPE,
        p_product_id IN demo_orders.product_id%TYPE,
        p_quantity IN demo_orders.quantity%TYPE,
        p_status_code IN demo_orders.status_code%TYPE DEFAULT 'NEW') AS
        l_unit_price demo_products.unit_price%TYPE;
    BEGIN
        SELECT unit_price
          INTO l_unit_price
          FROM demo_products
         WHERE product_id = p_product_id;

        INSERT INTO demo_orders (customer_id, product_id, quantity, order_total, status_code)
        VALUES (p_customer_id, p_product_id, p_quantity, ROUND(l_unit_price * p_quantity, 2), p_status_code);
    END create_order;

    FUNCTION get_order_total(p_order_id IN demo_orders.order_id%TYPE) RETURN NUMBER AS
        l_total demo_orders.order_total%TYPE;
    BEGIN
        SELECT order_total INTO l_total FROM demo_orders WHERE order_id = p_order_id;
        RETURN l_total;
    END get_order_total;
END demo_customer_api;
/
""";

    private static string CustomersSampleDataSql() => """
INSERT INTO demo_customers (customer_name, email_address) VALUES ('Ava Novak', 'ava.novak@example.test');
INSERT INTO demo_customers (customer_name, email_address) VALUES ('Luka Horvat', 'luka.horvat@example.test');
INSERT INTO demo_customers (customer_name, email_address) VALUES ('Mia Kovac', 'mia.kovac@example.test');

INSERT INTO demo_products (product_name, unit_price, active_flag) VALUES ('Starter Subscription', 49.00, 'Y');
INSERT INTO demo_products (product_name, unit_price, active_flag) VALUES ('Analytics Bundle', 149.00, 'Y');
INSERT INTO demo_products (product_name, unit_price, active_flag) VALUES ('Implementation Workshop', 299.00, 'Y');

INSERT INTO demo_orders (customer_id, product_id, quantity, order_total, status_code) VALUES (1, 1, 2, 98.00, 'NEW');
INSERT INTO demo_orders (customer_id, product_id, quantity, order_total, status_code) VALUES (2, 3, 1, 299.00, 'PAID');
COMMIT;
""";

    private static string HealthCheckDatabaseSql() => """
SELECT 'Listener reachable' AS status FROM dual;
EXIT;
""";

    private static string HealthCheckPdbSql() => """
SET HEADING OFF
SET FEEDBACK OFF
SELECT open_mode FROM v$pdbs WHERE name = 'FREEPDB1';
EXIT;
""";

    private static string CustomersReferenceSql() => """
SELECT *
FROM demo_order_summary_v
ORDER BY order_id;
""";

    private static string ApexApplicationStub() => """
-- Oracle APEX application source placeholder
-- This generated starter file marks the source-controlled location for APEX exports.
-- Replace it with a real SQLcl or APEX export after the first builder-based export.
--
-- Suggested sample application:
--   Name: Customer Orders Demo
--   Main views: Interactive Report, Interactive Grid, Chart, Dashboard
--   Tables: DEMO_CUSTOMERS, DEMO_PRODUCTS, DEMO_ORDERS
--   View: DEMO_ORDER_SUMMARY_V
--   Package: DEMO_CUSTOMER_API
""";

    private static string HealthCheckDatabaseScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
connection=${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}
sqlcl_script="$workspace_root/scripts/sqlcl.sh"

"$sqlcl_script" -S "$connection" @"$workspace_root/tutorial/oracle/scripts/health-check-database.sql"
"$sqlcl_script" -S "$connection" @"$workspace_root/tutorial/oracle/scripts/health-check-pdb.sql"
""";

    private static string HealthCheckOrdsScript() => """
ords_url=${ORACLE_ORDS_BASE_URL:-http://oracle-ords:8080/ords}
curl -fsSL "$ords_url" >/dev/null
printf 'ORDS reachable at %s\n' "$ords_url"
""";

    private static string HealthCheckApexScript() => """
apex_url=${ORACLE_APEX_LOGIN_URL:-http://oracle-ords:8080/ords/apex}
curl -fsSL "$apex_url" >/dev/null
printf 'APEX login reachable at %s\n' "$apex_url"
""";

    private static string HealthCheckSqlclScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
"$workspace_root/scripts/sqlcl.sh" -version
""";

    private static string SqlclWrapperScript() => """
sqlcl_root=${SQLCL_ROOT:-/opt/sqlcl/sqlcl}
sqlcl_bin="${sqlcl_root}/bin/sql"

if [ ! -x "${sqlcl_bin}" ]; then
  printf 'SQLcl launcher not found at %s\n' "${sqlcl_bin}" >&2
  exit 1
fi

# SQLcl 26.1.x resolves its classpath correctly only when started from the
# actual SQLcl home instead of the broken symlinked launcher path.
cd "${sqlcl_root}"
exec "${sqlcl_bin}" "$@"
""";

    private static string ExportApexScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output_path=${1:-"$workspace_root/exports/apexlang/hello-apexlang"}
connection="testschema/${ORACLE_DEMO_PASSWORD:-demo_password}@//oracle-demo:1521/FREEPDB1"
sqlcl_script="$workspace_root/scripts/sqlcl.sh"

mkdir -p "$workspace_root/exports/apexlang"
"$sqlcl_script" -S "$connection" <<SQL
apex export -applicationid 101 -split -exptype APEXLANG -dir $(dirname "$output_path") -force
exit
SQL
""";

    private static string ImportApexScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
input_path=${1:-"$workspace_root/exports/apexlang/hello-apexlang"}
connection="testschema/${ORACLE_DEMO_PASSWORD:-demo_password}@//oracle-demo:1521/FREEPDB1"
sqlcl_script="$workspace_root/scripts/sqlcl.sh"

test -d "$input_path" -o -f "$input_path"
"$sqlcl_script" -S "$connection" <<SQL
apex import -workspace TEST -schema TESTSCHEMA -id 101 -name "Hello APEXlang" -input "$input_path"
exit
SQL
""";

    private static string ValidateApexScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
input_file=${1:-"$workspace_root/apex/application.apx"}

test -f "$input_file"
grep -Eq 'APEX application|wwv_flow|application' "$input_file"
printf 'Validated %s\n' "$input_file"
""";

    private static string ExportSchemaScript() => """
connection=${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}
sql -S "$connection" <<'SQL'
ddl demo_customers
ddl demo_products
ddl demo_orders
ddl demo_order_summary_v
ddl demo_customer_api
exit
SQL
""";

    private static string ImportSchemaScript() => """
input_file=${1:?Provide a schema SQL file.}
connection=${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}
sql -S "$connection" @"$input_file"
""";

    private static string ExportDataPumpScript() => """
printf 'Use Oracle Data Pump from the Oracle Database Free service for full exports.\n'
printf 'Recommended starting point: expdp demo_user/demo_password@FREEPDB1 schemas=DEMO_USER directory=DATA_PUMP_DIR dumpfile=demo_user.dmp logfile=demo_user-exp.log\n'
""";

    private static string ImportDataPumpScript() => """
printf 'Use Oracle Data Pump from the Oracle Database Free service for full imports.\n'
printf 'Recommended starting point: impdp demo_user/demo_password@FREEPDB1 schemas=DEMO_USER directory=DATA_PUMP_DIR dumpfile=demo_user.dmp logfile=demo_user-imp.log\n'
""";

    private static string ExportApexLangScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
"$workspace_root/scripts/apexlang-hello-world.sh"
"$workspace_root/scripts/validate-apex.sh" "$workspace_root/exports/apexlang/hello-apexlang/application.apx"
""";

    private static string GenerateHelloApexLangSql() => """
apex generate -workspace TEST -schema TESTSCHEMA -id 101 -name "Hello APEXlang" -alias HELLO-APEXLANG -dir /workspace/exports/apexlang -force
exit
""";

    private static string ValidateHelloApexLangSql() => """
apex validate -workspace TEST -input /workspace/exports/apexlang/hello-apexlang
exit
""";

    private static string ImportHelloApexLangSql() => """
apex import -workspace TEST -schema TESTSCHEMA -id 101 -name "Hello APEXlang" -input /workspace/exports/apexlang/hello-apexlang
exit
""";

    private static string ExportHelloApexLangSql() => """
apex export -applicationid 101 -split -exptype APEXLANG -dir /workspace/exports/apexlang -force
exit
""";

    private static string ApexLangHelloWorldScript() => """
set -euo pipefail

workspace_root=/workspace
sqlcl_script="$workspace_root/scripts/sqlcl.sh"
generate_sql="$workspace_root/sql/hello-apexlang/generate-hello-apexlang.sql"
validate_sql="$workspace_root/sql/hello-apexlang/validate-hello-apexlang.sql"
import_sql="$workspace_root/sql/hello-apexlang/import-hello-apexlang.sql"
export_sql="$workspace_root/sql/hello-apexlang/export-hello-apexlang.sql"
export_root="$workspace_root/exports/apexlang/hello-apexlang"
home_page_apx="$export_root/pages/p00001-home.apx"
sys_password=${ORACLE_PASSWORD:-}
testschema_password=${ORACLE_DEMO_PASSWORD:-demo_password}
sys_connection="sys/${sys_password}@//oracle-demo:1521/FREEPDB1 as sysdba"
testschema_connection="testschema/${testschema_password}@//oracle-demo:1521/FREEPDB1"
enabled=${ORACLE_APEXLANG_HELLO_WORLD_ENABLED:-true}

""" + OracleSqlExecutionScriptSupport.BuildShellLibrary() + """

fail() {
  printf '[oracle-apexlang] ERROR: %s\n' "$1" >&2
  exit 1
}

if [ "${enabled}" != 'true' ]; then
  printf '[oracle-apexlang] Hello APEXlang provisioning is disabled.\n'
  exit 0
fi

for required in "$sqlcl_script" "$generate_sql" "$validate_sql" "$import_sql" "$export_sql"; do
  [ -f "$required" ] || fail "Expected file is missing: $required"
done

[ -n "$sys_password" ] || fail 'ORACLE_PASSWORD is required for Hello APEXlang provisioning.'

tmp_root=$(mktemp -d)
trap 'rm -rf "$tmp_root"' EXIT

apex_registry_sql="$tmp_root/apex-registry.sql"
cat > "$apex_registry_sql" <<'SQL'
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SET VERIFY OFF
SET TRIMSPOOL ON
SELECT comp_id || '|' || comp_name || '|' || version || '|' || status
FROM dba_registry
WHERE comp_id = 'APEX';
EXIT
SQL

testschema_sql="$tmp_root/testschema-ready.sql"
cat > "$testschema_sql" <<SQL
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE
    l_exists NUMBER := 0;
BEGIN
    SELECT COUNT(*) INTO l_exists FROM dba_users WHERE username = 'TESTSCHEMA';
    IF l_exists = 0 THEN
        EXECUTE IMMEDIATE 'CREATE USER testschema IDENTIFIED BY "${testschema_password}" QUOTA UNLIMITED ON USERS';
    ELSE
        EXECUTE IMMEDIATE 'ALTER USER testschema IDENTIFIED BY "${testschema_password}" ACCOUNT UNLOCK';
    END IF;
END;
/
GRANT CREATE SESSION TO testschema;
GRANT CREATE TABLE TO testschema;
GRANT CREATE VIEW TO testschema;
GRANT CREATE PROCEDURE TO testschema;
GRANT CREATE TRIGGER TO testschema;
GRANT CREATE SEQUENCE TO testschema;
GRANT CREATE JOB TO testschema;
GRANT UNLIMITED TABLESPACE TO testschema;
EXIT
SQL

workspace_sql="$tmp_root/test-workspace.sql"
cat > "$workspace_sql" <<'SQL'
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE
    l_exists NUMBER := 0;
    l_has_schema NUMBER := 0;
BEGIN
    SELECT COUNT(*) INTO l_exists
      FROM apex_workspace_schemas
     WHERE workspace_name = 'TEST';

    IF l_exists = 0 THEN
        apex_instance_admin.add_workspace(
            p_workspace      => 'TEST',
            p_primary_schema => 'TESTSCHEMA');
    END IF;

    SELECT COUNT(*) INTO l_has_schema
      FROM apex_workspace_schemas
     WHERE workspace_name = 'TEST'
       AND schema = 'TESTSCHEMA';

    IF l_has_schema = 0 THEN
        raise_application_error(-20001, 'Workspace TEST is not mapped to TESTSCHEMA.');
    END IF;
END;
/
EXIT
SQL

verify_workspace_sql="$tmp_root/verify-workspace.sql"
cat > "$verify_workspace_sql" <<'SQL'
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SET VERIFY OFF
SET TRIMSPOOL ON
SELECT workspace_name || '|' || schema
FROM apex_workspace_schemas
WHERE workspace_name = 'TEST'
  AND schema = 'TESTSCHEMA';
EXIT
SQL

verify_app_sql="$tmp_root/verify-app.sql"
cat > "$verify_app_sql" <<'SQL'
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SET VERIFY OFF
SET TRIMSPOOL ON
SELECT application_name || '|' || page_name || '|' || region_name || '|' || title
FROM apex_applications a
JOIN apex_application_pages p
  ON p.application_id = a.application_id
 AND p.workspace = a.workspace
JOIN apex_application_page_regions r
  ON r.application_id = p.application_id
 AND r.page_id = p.page_id
 AND r.workspace = p.workspace
WHERE a.workspace = 'TEST'
  AND a.application_id = 101
  AND p.page_id = 1;
EXIT
SQL

"$sqlcl_script" -version >/dev/null 2>&1 || fail 'SQLcl missing or broken. Run scripts/sqlcl.sh -version for details.'
printf '[oracle-apexlang] SQLcl verified.\n'

apex_registry=$(oracle_sql_run_file 'Creating Sample Application' sqlcl "$sys_connection" script 'apex-registry.sql' "$apex_registry_sql" | tr -d '\r' | sed '/^$/d' | tail -n 1 | xargs || true)
[ -n "$apex_registry" ] || fail 'APEX not installed or dba_registry query returned no APEX row.'
case "$apex_registry" in
  APEX\|Oracle\ APEX\|26.1.*\|VALID) ;;
  *) fail "APEX not installed or wrong version: $apex_registry" ;;
esac
printf '[oracle-apexlang] APEX registry verified: %s\n' "$apex_registry"

oracle_sql_run_file 'Creating Sample Application' sqlcl "$sys_connection" plsql-block 'testschema-ready.sql' "$testschema_sql" >/dev/null || fail 'TESTSCHEMA missing and could not be created or unlocked.'

oracle_sql_run_file 'Creating Sample Application' sqlcl "$sys_connection" plsql-block 'test-workspace.sql' "$workspace_sql" >/dev/null || fail 'TEST workspace missing and could not be created or mapped to TESTSCHEMA.'
workspace_mapping=$(oracle_sql_run_file 'Creating Sample Application' sqlcl "$sys_connection" script 'verify-workspace.sql' "$verify_workspace_sql" | tr -d '\r' | sed '/^$/d' | tail -n 1 | xargs || true)
[ "$workspace_mapping" = 'TEST|TESTSCHEMA' ] || fail 'TEST workspace missing after setup verification.'

mkdir -p "$workspace_root/exports/apexlang"
oracle_sql_run_file 'Creating Sample Application' sqlcl /nolog sqlcl-command-script 'sql/hello-apexlang/generate-hello-apexlang.sql' "$generate_sql" >/dev/null || fail 'Hello APEXlang package generation failed.'
printf '[oracle-apexlang] Hello APEXlang package generated.\n'

[ -f "$home_page_apx" ] || fail 'Generated Home page APEXlang file is missing.'
python3 - <<'PY'
from pathlib import Path
path = Path('/workspace/exports/apexlang/hello-apexlang/pages/p00001-home.apx')
text = path.read_text(encoding='utf-8')
text = text.replace('title: &APP_TITLE.', 'title: Hello from APEXlang', 1)
path.write_text(text, encoding='utf-8', newline='\n')
PY

oracle_sql_run_file 'Creating Sample Application' sqlcl "$testschema_connection" sqlcl-command-script 'sql/hello-apexlang/validate-hello-apexlang.sql' "$validate_sql" >/dev/null || fail 'Hello APEXlang package validation failed.'
printf '[oracle-apexlang] Hello APEXlang package validated.\n'

oracle_sql_run_file 'Creating Sample Application' sqlcl "$testschema_connection" sqlcl-command-script 'sql/hello-apexlang/import-hello-apexlang.sql' "$import_sql" >/dev/null || fail 'Hello APEXlang import failed.'
printf '[oracle-apexlang] Hello APEXlang application imported.\n'

oracle_sql_run_file 'Creating Sample Application' sqlcl "$testschema_connection" sqlcl-command-script 'sql/hello-apexlang/export-hello-apexlang.sql' "$export_sql" >/dev/null || fail 'Hello APEXlang export failed.'
printf '[oracle-apexlang] Hello APEXlang package exported.\n'

final_app=$(oracle_sql_run_file 'Creating Sample Application' sqlcl "$sys_connection" script 'verify-app.sql' "$verify_app_sql" | tr -d '\r' | sed '/^$/d' | tail -n 1 | xargs || true)
[ "$final_app" = 'Hello APEXlang|Home|Home|Hello from APEXlang' ] || fail 'Hello APEXlang verification query returned an unexpected result.'
printf '[oracle-apexlang] Hello APEXlang application verified: %s\n' "$final_app"
""";

    private static string ApexLangHelloWorldPowerShellScript() => """
$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$workspaceName = Split-Path -Leaf $workspaceRoot
$containerName = ($workspaceName.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-') + '-workspace'

docker ps --format '{{.Names}}' | Select-String -SimpleMatch $containerName | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "The workspace container '$containerName' is not running."
}

docker exec -w /workspace $containerName bash -lc "/workspace/scripts/apexlang-hello-world.sh"
exit $LASTEXITCODE
""";

    private static string OpenOrdsScript(string ordsBaseUrl) => $"""
$ErrorActionPreference = 'Stop'
Start-Process '{ordsBaseUrl}'
""";

    private static string OpenApexScript(string apexLoginUrl) => $"""
$ErrorActionPreference = 'Stop'
Start-Process '{apexLoginUrl}'
""";

    private static string OpenSqlWorksheetScript() => """
$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $workspaceRoot 'open-sqlcl.ps1'

& $scriptPath
exit $LASTEXITCODE
""";
}
