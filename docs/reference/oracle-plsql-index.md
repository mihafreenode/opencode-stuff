# Oracle PL/SQL Reference Index

Purpose:
Curated official references for PL/SQL language, packages, stored procedures, triggers, and error handling.

Version note:
The links in this index target Oracle Database 23. Start with `docs/reference/oracle-knowledge-map.yaml` and confirm language, package, and error behavior against the deployed database version.

Intended use:
- implementing and reviewing PL/SQL code
- answering language and package behavior questions
- helping agents stay on official Oracle semantics for procedural database work

Primary documentation:
- PL/SQL Language Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/lnpls/
- PL/SQL Packages and Types Reference: https://docs.oracle.com/en/database/oracle/oracle-database/23/arpls/
- Database Error Messages: https://docs.oracle.com/en/database/oracle/oracle-database/23/errmg/

Recommended sections:
- Blocks and Control Structures
- Procedures and Functions
- Packages
- Triggers
- Collections and Records
- Dynamic SQL
- Exception Handling

When to use:
- explaining or refactoring existing PL/SQL procedures
- validating syntax and package usage
- debugging runtime exceptions and Oracle error codes

Navigation hints for agents:
- start with the language reference for syntax and semantics
- use the packages reference for built-in package behavior such as `DBMS_OUTPUT`, `UTL_*`, and `DBMS_SQL`
- use the error messages reference when a task includes `ORA-` diagnostics
- prefer this index over generic SQL references when the code contains `BEGIN`, `EXCEPTION`, packages, or triggers
