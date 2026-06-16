using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

internal static class OracleWorkspaceGeneratedContent
{
    public static IReadOnlyDictionary<string, string> Generate(
        WorkspaceDefinition definition,
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
            [Path.Combine("docs", "team-onboarding.md")] = withGeneratedHeader(TeamOnboardingDoc()),
            [Path.Combine("docs", "oracle-lifecycle-workflows.md")] = withGeneratedHeader(OracleLifecycleWorkflowsDoc(kind)),
            [Path.Combine("docs", "sharing-oracle-workspaces.md")] = withGeneratedHeader(SharingOracleWorkspacesDoc(kind)),
        };

        if (kind is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang)
        {
            files[Path.Combine("docs", "oracle-apex-demo.md")] = withGeneratedHeader(OracleApexDemoDoc());
            files[Path.Combine("scripts", "health-check-database.sh")] = withGeneratedScriptHeader(HealthCheckDatabaseScript());
            files[Path.Combine("scripts", "health-check-ords.sh")] = withGeneratedScriptHeader(HealthCheckOrdsScript());
            files[Path.Combine("scripts", "health-check-apex.sh")] = withGeneratedScriptHeader(HealthCheckApexScript());
            files[Path.Combine("scripts", "health-check-sqlcl.sh")] = withGeneratedScriptHeader(HealthCheckSqlclScript());
            files[Path.Combine("scripts", "open-ords.ps1")] = OpenOrdsScript();
            files[Path.Combine("scripts", "open-apex.ps1")] = OpenApexScript();
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
            files[Path.Combine("apex", "application.apx")] = ApexApplicationStub();
            files[Path.Combine("sql", "customers-reference.sql")] = withGeneratedSqlHeader(CustomersReferenceSql());
            files[Path.Combine("scripts", "export-apex.sh")] = withGeneratedScriptHeader(ExportApexScript());
            files[Path.Combine("scripts", "import-apex.sh")] = withGeneratedScriptHeader(ImportApexScript());
            files[Path.Combine("scripts", "validate-apex.sh")] = withGeneratedScriptHeader(ValidateApexScript());
            files[Path.Combine("scripts", "export-schema.sh")] = withGeneratedScriptHeader(ExportSchemaScript());
            files[Path.Combine("scripts", "import-schema.sh")] = withGeneratedScriptHeader(ImportSchemaScript());
            files[Path.Combine("scripts", "export-datapump.sh")] = withGeneratedScriptHeader(ExportDataPumpScript());
            files[Path.Combine("scripts", "import-datapump.sh")] = withGeneratedScriptHeader(ImportDataPumpScript());
            files[Path.Combine("scripts", "export-apexlang.sh")] = withGeneratedScriptHeader(ExportApexLangScript());
        }

        return files;
    }

    private static string OraclePlSqlDemoDoc() => """
## Oracle PL/SQL Demo

Use this workspace when the goal is learning PL/SQL in a safe local Oracle environment with reproducible setup.

### Included

- Oracle Database Free
- SQLcl and SQL*Plus validation
- Sample schema and tutorial queries
- Oracle-focused OpenCode skills

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
""";

    private static string OracleApexDemoDoc() => """
## Oracle APEX Demo

This workspace extends the Oracle PL/SQL demo with Oracle APEX and ORDS so onboarding can continue from database objects into local low-code application development.

### Included

- Oracle Database Free
- Oracle APEX runtime through the database service
- Oracle REST Data Services at `http://localhost:8181/ords`
- SQLcl for database and APEX command-line workflows
- Sample `Customers` schema objects and sample data

### Suggested Flow

1. Start the Oracle workspace.
2. Open ORDS with `scripts/open-ords.ps1`.
3. Open APEX with `scripts/open-apex.ps1`.
4. Run `scripts/health-check-database.sh`, `scripts/health-check-ords.sh`, and `scripts/health-check-apex.sh` inside the workspace runtime.

This workspace focuses on the traditional Oracle APEX Builder workflow in a reproducible local onboarding environment.

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
- `scripts/export-apex.sh`
- `scripts/import-apex.sh`
- `scripts/validate-apex.sh`
- `docs/apexlang-introduction.md`

Use the generated scripts as the official-tooling starting point for repeatable export, review, validation, and import.
""";

    private static string TeamOnboardingDoc() => """
## Team Onboarding

The repository is the source of truth for Oracle workspace onboarding.

### Expected Flow

```text
Clone Repository
    ↓
Open Existing Repository
    ↓
Workspace Discovered
    ↓
Review Configuration
    ↓
Provision Environment
    ↓
Read Docs
    ↓
Run Tutorial
    ↓
Start Learning
```

### Durable Inputs

- `workspace.yaml`
- `compose.yaml`
- `.env.example`
- `sql/`
- `apex/`
- `scripts/`
- `docs/`
- `AGENTS.md`

No manual recreation of Oracle settings should be required when the repository already contains those files.
""";

    private static string OracleLifecycleWorkflowsDoc(OracleWorkspaceKind kind) => $"""
## Oracle Lifecycle Workflows

This workspace favors official Oracle tooling and readable repository artifacts over custom deployment formats.

### Database Lifecycle

- Export Schema: `scripts/export-schema.sh`
- Import Schema: `scripts/import-schema.sh`
- Export Data Pump: `scripts/export-datapump.sh`
- Import Data Pump: `scripts/import-datapump.sh`

### APEX Lifecycle

- Export Application: `scripts/export-apex.sh`
- Import Application: `scripts/import-apex.sh`
- Validate APEXlang: `scripts/validate-apex.sh`
- Export APEXlang: `scripts/export-apexlang.sh`

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

    private static string CustomersSchemaSql() => """
CREATE TABLE demo_customers_apex (
    customer_id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    customer_name VARCHAR2(200) NOT NULL,
    email VARCHAR2(320) NOT NULL UNIQUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL
);
""";

    private static string CustomersSampleDataSql() => """
INSERT INTO demo_customers_apex (customer_name, email) VALUES ('Ava Novak', 'ava.novak@example.test');
INSERT INTO demo_customers_apex (customer_name, email) VALUES ('Luka Horvat', 'luka.horvat@example.test');
INSERT INTO demo_customers_apex (customer_name, email) VALUES ('Mia Kovac', 'mia.kovac@example.test');
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
SELECT customer_id, customer_name, email
FROM demo_customers_apex
ORDER BY customer_id;
""";

    private static string ApexApplicationStub() => """
-- Oracle APEX application source placeholder
-- This generated starter file marks the source-controlled location for APEX exports.
-- Replace it with a real SQLcl or APEX export after the first builder-based export.
--
-- Suggested sample application:
--   Name: Customers
--   Pages: List, Create, Edit, Delete
--   Fields: Customer ID, Customer Name, Email
""";

    private static string HealthCheckDatabaseScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
connection=${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}

sql -S "$connection" @"$workspace_root/tutorial/oracle/scripts/health-check-database.sql"
sql -S "$connection" @"$workspace_root/tutorial/oracle/scripts/health-check-pdb.sql"
""";

    private static string HealthCheckOrdsScript() => """
ords_url=${ORACLE_ORDS_BASE_URL:-http://oracle-ords:8181/ords}
curl -fsSL "$ords_url" >/dev/null
printf 'ORDS reachable at %s\n' "$ords_url"
""";

    private static string HealthCheckApexScript() => """
apex_url=${ORACLE_APEX_LOGIN_URL:-http://oracle-ords:8181/ords/apex}
curl -fsSL "$apex_url" >/dev/null
printf 'APEX login reachable at %s\n' "$apex_url"
""";

    private static string HealthCheckSqlclScript() => """
command -v sql >/dev/null 2>&1
sql -v
""";

    private static string ExportApexScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output_file=${1:-"$workspace_root/apex/application.apx"}
connection=${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}

mkdir -p "$(dirname "$output_file")"
sql -S "$connection" <<SQL
apex export -applicationid 100 -split -expcomponents -dir $(dirname "$output_file")
exit
SQL
""";

    private static string ImportApexScript() => """
workspace_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
input_file=${1:-"$workspace_root/apex/application.apx"}
connection=${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}

test -f "$input_file"
sql -S "$connection" @"$input_file"
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
ddl demo_customers_apex
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
"$workspace_root/scripts/export-apex.sh" "$workspace_root/apex/application.apx"
"$workspace_root/scripts/validate-apex.sh" "$workspace_root/apex/application.apx"
""";

    private static string OpenOrdsScript() => """
$ErrorActionPreference = 'Stop'
Start-Process 'http://localhost:8181/ords'
""";

    private static string OpenApexScript() => """
$ErrorActionPreference = 'Stop'
Start-Process 'http://localhost:8181/ords/apex'
""";

    private static string OpenSqlWorksheetScript() => """
$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $workspaceRoot 'open-sqlcl.ps1'

& $scriptPath
exit $LASTEXITCODE
""";
}
