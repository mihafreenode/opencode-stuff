# Oracle Database Reference Index

Purpose:
Curated official starting points for Oracle Database concepts, SQL, administration, and security.

Version note:
The links in this index target Oracle Database 23. Start with `docs/reference/oracle-knowledge-map.yaml` and use the documentation matching the target database version for version-sensitive behavior.

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
- Backup, Import, and Export Concepts
- Security Fundamentals

When to use:
- schema design and SQL questions
- user, privilege, and connection setup tasks
- administration and operational troubleshooting that is not specific to APEX or ORDS

Navigation hints for agents:
- use the SQL Language Reference for DDL, DML, and query behavior
- use Concepts when the task is architectural or terminology-heavy
- use the Administrator's Guide for operational setup and maintenance
- use the Security Guide for grants, authentication, and least-privilege guidance
