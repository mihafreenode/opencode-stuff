# Oracle PL/SQL Demo Workspace

OpenCode provides a reproducible Oracle development workspace containing the database, tools, tutorials, knowledge, and AI assistance required to become productive quickly and safely.

For the authoritative capability and maturity map, see [Oracle and Oracle APEX Integration](integrations/oracle-apex.md).

The Oracle demo database is local to the workspace. Staging setup is optional and is not part of the first tutorial.

Provision a local Oracle environment, explore a realistic schema, and understand existing PL/SQL before connecting to customer systems.

## Why This Matters

Many Oracle developers learn, troubleshoot, and test changes against a shared staging environment.

That approach has drawbacks:

- limited access
- shared state
- risk of accidental changes
- difficulty experimenting freely
- slower onboarding for new developers

The Oracle PL/SQL Demo workspace provides a local Oracle environment with sample schema, data, procedures, and triggers that can be reset at any time.

Developers can:

- experiment safely
- practice PL/SQL development
- learn unfamiliar Oracle tooling
- explore schema changes
- test ideas before proposing changes to shared environments

The goal is not to replace staging. The goal is to provide a safe place to learn, prototype, and understand Oracle systems before working against customer environments.

## Architecture Overview

The built-in `Oracle PL/SQL Demo` template packages:

- Oracle Database Free in Docker Compose
- SQLcl installed inside the workspace runtime
- SQL*Plus installed inside the workspace runtime as a reliable terminal fallback
- guided tutorial content and quick demo scripts
- a local sample schema with procedures and triggers
- Oracle-focused AI prompt files for explanation, debugging, refactoring, and tests
- optional later-stage Oracle network configuration for staging access

The onboarding path does not require customer infrastructure. The demo is entirely local.

For onboarding and tutorial exercises, use the provided demo connection details. Manual Oracle network configuration is only required for customer environments.

## Getting Started

1. Create a new workspace with `New Workspace -> Oracle PL/SQL Demo -> Create`.
2. In the workspace dashboard, start the Oracle demo database.
3. Open the workspace tutorial.
4. Connect with SQLcl, SQL*Plus, or SQL Developer.
5. Run the sample query and inspect the sample PL/SQL.
6. Ask AI to explain the demo procedure and trigger.

## Recommended Learning Path

1. [Practical Git for Oracle Developers](oracle/practical-git-for-oracle-developers.md)
2. [From Oracle Demo to Oracle Onboarding](articles/oracle-onboarding.md)
3. [Repository Workflows](capabilities/repository.md)
4. [Testing](capabilities/testing.md)
5. [AGENTS.md Guide](agents-guide.md)
6. [APEXlang](oracle-tools/apexlang.md)

## Tutorial Walkthrough

The workspace tutorial covers:

1. Environment verification
2. Hello world PL/SQL
3. Demo schema creation
4. Sample data
5. Procedure creation and explanation
6. Trigger explanation
7. Trigger troubleshooting
8. Refactoring example
9. Mini real-world demo scenario

The tutorial is generated into the workspace and visible from the dashboard.

## Demo Flow

A typical demonstration takes less than five minutes:

1. Create an Oracle PL/SQL Demo workspace.
2. Start Oracle Database Free.
3. Verify connectivity from OpenCode.
4. Connect using SQL Developer.
5. Execute a sample query.
6. Ask OpenCode to explain existing PL/SQL.

The screenshots below demonstrate:

1. AI-assisted PL/SQL explanation
2. SQL Developer connection
3. Querying the demo schema

## Oracle Demo Database

- Docker Compose profile: `oracle-demo`
- Demo username: `demo_user`
- Demo password: `demo_password`
- Admin password: generated into workspace `.env` as `ORACLE_PASSWORD`

### Windows / SQL Developer

- Host: `localhost`
- Port: `1521`
- Service: `FREEPDB1`

### Inside Workspace Runtime

- Host: `oracle-demo`
- Port: `1521`
- Service: `FREEPDB1`

The workspace creates a persistent Oracle data volume and exposes start, stop, reset, logs, and copy-connection actions from the dashboard.

## SQLcl Usage

SQLcl is installed during workspace provisioning.

On the first provisioning run, SQLcl is downloaded from Oracle and requires internet access. After provisioning completes, the demo database and tutorial work locally.

The workspace installs a supported Java runtime automatically during provisioning because SQLcl requires Java 11 or newer.

Typical examples:

```text
sql demo_user/demo_password@//oracle-demo:1521/FREEPDB1
SET SERVEROUTPUT ON
EXEC demo_show_customer(1)
@tutorial/oracle/scripts/03-sample-queries.sql
```

Workspace helper scripts:

- `open-sqlcl.ps1`
- `test-oracle-connection.ps1`
- `run-tutorial-query.ps1`

SQLcl is the preferred interactive client for the demo when it is healthy.

## SQL*Plus Fallback

SQL*Plus is installed inside the workspace container for reliable terminal verification.

Use it from the workspace terminal when SQLcl is unavailable or unstable:

```text
sqlplus -S demo_user/demo_password@//oracle-demo:1521/FREEPDB1 <<'EOF'
SELECT 'Connection OK' AS status FROM dual;
SELECT customer_id, customer_name FROM demo_customers ORDER BY customer_id;
SET SERVEROUTPUT ON
EXEC demo_show_customer(1);
EXIT;
EOF
```

For onboarding and tutorial exercises, use the provided demo connection details. Manual Oracle network configuration is only required for customer environments.

The helper actions `Test Connection` and `Run Tutorial Query` prefer SQL*Plus when it is available, then fall back to SQLcl.

## Explain Existing PL/SQL Code

In this example OpenCode explains the `demo_show_customer` procedure, identifies the tables involved, describes the generated output, and explains the relationship between customers and orders.

OpenCode can inspect existing Oracle PL/SQL and explain:

- procedure purpose
- table relationships
- SQL statements being executed
- expected output
- edge cases
- opportunities for improvement

This helps developers understand unfamiliar database code without manually tracing every dependency.

## SQL Developer Usage

If SQL Developer is detected on Windows, the dashboard shows an `Open SQL Developer` action.

If SQL Developer is not installed, the tutorial still works with SQLcl or SQL*Plus and the app shows guidance only.

Use the connection details listed in the Oracle Demo Database section.

### Connect Using SQL Developer

SQL Developer connected successfully to the local Oracle Free database running inside the workspace.

Use the connection details listed in the Oracle Demo Database section.

This verifies that standard Oracle tooling can connect to the same database used by the workspace.

### Querying the Demo Schema

The demo workspace provisions a sample Oracle schema containing customers, orders, and products.

This query joins:

- DEMO_CUSTOMERS
- DEMO_ORDERS
- DEMO_PRODUCTS

and returns business-oriented results using standard Oracle tooling.

## Oracle Skills

The template includes Oracle-focused prompt files and catalog entries for:

- Explain Procedure
- Explain Trigger
- Debug Procedure
- Refactor Procedure
- Generate Test Cases

The included examples focus on understanding and exploring existing PL/SQL before creating new database code.

## Staging Guidance

Staging is optional and not required for onboarding.

Supported configuration:

- Connection name
- Host
- Port
- Service name
- Username
- TNS alias
- Wallet
- custom `sqlnet.ora`
- custom `tnsnames.ora`

Display staging clearly as `STAGING (READ ONLY)`.

## Read-Only Guidance

Strongly prefer a dedicated read-only Oracle user. When supported, set:

```sql
ALTER SESSION SET read_only = TRUE;
```

Restrict staging interactions to:

- `SELECT`
- `WITH`
- `EXPLAIN PLAN`

Reject:

- `INSERT`
- `UPDATE`
- `DELETE`
- `MERGE`
- `TRUNCATE`
- `CREATE`
- `ALTER`
- `DROP`
- `GRANT`
- `REVOKE`
- PL/SQL execution

Never run migrations, setup scripts, or schema changes against staging.

## Oracle Network Configuration

Place customer-provided Oracle network files under:

```text
.local/oracle/network/admin
```

When present, the workspace shell exports `TNS_ADMIN` to that directory automatically.

## MCP Integration Notes

The OpenCode Workspace MCP server is the implemented workspace lifecycle and Oracle Assistant integration. Oracle SQLcl MCP through `sql -mcp` is a separate catalog entry and remains experimental/environment-dependent; its availability does not imply that it is installed, configured, or the MCP server used by the workspace manager.

## Presenter Demo Script

Use this presenter flow:

1. Create `Oracle PL/SQL Demo` workspace.
2. Start Oracle.
3. Verify with `sqlplus demo_user/demo_password@oracle-demo:1521/FREEPDB1`.
4. Run the demo queries.
5. Open SQL Developer only if you specifically want the GUI path.
6. Open tutorial.
7. Explain procedure.
8. Explain trigger.

## Troubleshooting

- If Oracle startup is slow, inspect `View Logs` and wait for the health check to settle.
- If SQLcl is missing, run `Prepare Workspace` so the generated install plan runs again.
- If SQLcl is unstable, use SQL*Plus from the workspace terminal for demo verification.
- If SQLcl reports a Java error, run `Prepare Workspace` so the generated install plan can install a supported Java runtime automatically.
- If the demo schema is missing, use the reset action to recreate the Oracle data volume and rerun initialization scripts.
- If SQL Developer cannot connect, verify the service name is `FREEPDB1` and the host port `1521` is free on Windows.
