# Oracle APEXlang Reference Index

Purpose:
Structured entry point for Oracle APEXlang, Oracle's Open Application Specification Language for Oracle APEX.

Version note:
The primary link targets APEXlang 26.1. Treat it as a versioned reference and confirm that the local SQLcl/APEX toolchain supports the same specification version through `docs/reference/oracle-knowledge-map.yaml`.

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

When to use:
- generating Oracle APEX application definitions
- reviewing APEXlang exports in Git
- validating whether a change belongs in Builder concepts or in the APEXlang document structure

Navigation hints for agents:
- start here before editing `apex/application.apx`
- map Builder concepts like pages, regions, and items to APEXlang section names before proposing edits
- use `docs/reference/oracle-apexlang-navigation.md` for the human-maintained major-section map
- fall back to the general Oracle APEX index for Builder or administration topics outside the application definition format
