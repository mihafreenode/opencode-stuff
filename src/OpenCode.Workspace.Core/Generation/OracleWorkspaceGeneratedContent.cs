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
            [Path.Combine("docs", "oracle-samples.md")] = withGeneratedHeader(OracleSamplesDoc()),
            [Path.Combine("docs", "team-onboarding.md")] = withGeneratedHeader(TeamOnboardingDoc()),
            [Path.Combine("docs", "oracle-lifecycle-workflows.md")] = withGeneratedHeader(OracleLifecycleWorkflowsDoc(kind)),
            [Path.Combine("docs", "sharing-oracle-workspaces.md")] = withGeneratedHeader(SharingOracleWorkspacesDoc(kind)),
            [Path.Combine("docs", "oracle-tools", "README.md")] = withGeneratedHeader(OracleToolsIndexDoc()),
            [Path.Combine("docs", "oracle-tools", "sqlcl.md")] = withGeneratedHeader(SqlclToolDoc()),
            [Path.Combine("docs", "oracle-tools", "data-pump.md")] = withGeneratedHeader(DataPumpToolDoc()),
            [Path.Combine("docs", "oracle-tools", "ords.md")] = withGeneratedHeader(OrdsToolDoc()),
            [Path.Combine("docs", "oracle-tools", "apex-export-import.md")] = withGeneratedHeader(ApexExportImportToolDoc()),
            [Path.Combine("docs", "oracle-tools", "apexlang.md")] = withGeneratedHeader(ApexLangToolDoc()),
            [Path.Combine("docs", "oracle-tools", "sql-developer.md")] = withGeneratedHeader(SqlDeveloperToolDoc()),
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
- Oracle APEX runtime through the database service
- Oracle REST Data Services at `http://localhost:8181/ords`
- SQLcl for database and APEX command-line workflows
- Sample `Customer Orders Demo` schema objects and sample data

### Suggested Flow

1. Start the Oracle workspace.
2. Open ORDS with `scripts/open-ords.ps1`.
3. Open APEX with `scripts/open-apex.ps1`.
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

1. Open ORDS and sign in to APEX Builder.
2. Create an orders report on `demo_order_summary_v`.
3. Add a product-maintenance grid on `demo_products`.
4. Add a chart for orders by month.
5. Export the report data as CSV.

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
- `scripts/export-apex.sh`
- `scripts/import-apex.sh`
- `scripts/validate-apex.sh`
- `docs/apexlang-introduction.md`

Use the generated scripts as the official-tooling starting point for repeatable export, review, validation, and import.

## Try It Yourself

1. Add a new field to the `Customer Orders Demo` application.
2. Export the application with `scripts/export-apex.sh`.
3. Validate the exported source with `scripts/validate-apex.sh`.
4. Review the exported changes in Git.
5. Re-import the application definition with `scripts/import-apex.sh`.

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

### Capability Discovery

Start here:

- `docs/capabilities/README.md`
- `docs/capabilities/oracle.md`

No manual recreation of Oracle settings should be required when the repository already contains those files.
""";

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
- uses the same Customer Orders Demo application as the Builder-based APEX workflow

### Example Commands

```bash
scripts/export-apexlang.sh
scripts/validate-apex.sh apex/application.apx
scripts/import-apex.sh apex/application.apx
```

### Relationship To Other Tools

- APEXlang complements APEX Builder
- APEX export/import is still the official movement path
- SQLcl remains the automation entry point

### Licensing Notes

- terminology and workflow guidance tied to Oracle APEX
- no separate licensing claim made here beyond the APEX environment itself

### Onboarding Exercise

1. add a field to the Customer Orders Demo
2. export the application definition
3. validate the export
4. review the diff in Git
5. re-import the application definition locally
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
