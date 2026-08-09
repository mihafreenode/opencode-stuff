# Oracle APEX API Packages

Purpose:
Quick package discovery catalog for Oracle APEX runtime development without copying Oracle documentation text.

Use this catalog to identify likely package families, then open the official Oracle package page.

All package deep links in this catalog target the APEX 24.2 API Reference. Treat them as a versioned discovery index, not as proof that a package or member exists in another runtime. Start with `docs/reference/oracle-knowledge-map.yaml` and confirm behavior in documentation matching the deployed APEX version.

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
