# Capability Catalog

Use this catalog before searching the repository or probing installed binaries.

## Getting Started

If using a shell:

```bash
su opencode
cd /workspace
opencode -s resume
```

Docker Desktop Exec is a valid way to access a workspace, but the best onboarding experience starts from an OpenCode session rather than a root shell.

Then review:

- capability catalog
- onboarding materials
- workspace documentation

Read more:

- [Team Onboarding](../team-onboarding.md)
- [Workspace Sessions Troubleshooting](../troubleshooting/workspace-sessions.md)

The capability catalog is intended to answer questions such as `Can I process Excel files?`, `What PDF tools are available?`, `What OCR tools are available?`, `What Oracle tooling exists?`, and `What onboarding materials are available?` without repository-wide searching.

Tool guidance:

- capability docs describe supported workflows
- capability docs may mention optional tools
- agents should verify installed tools before claiming they are available
- if a documented tool is missing, report that clearly instead of assuming it exists

## Enabled Capabilities

- [x] Repository Workflows
- [x] Documentation
- [x] Document Processing
- [x] OCR
- [x] Spell Checking
- [x] Analytics
- [x] Reporting
- [x] Testing
- [x] Localization
- [x] Oracle

## Repository Workflows

Git-backed workspace conventions, `workspace.yaml`, generated artifacts, and `AGENTS.md` guidance.

Onboarding relevance: start here for workspace structure, safe working-copy habits, and self-describing repository guidance.

Available tools: Git, `workspace.yaml`, `AGENTS.md`

Read more: [Repository Workflows](repository.md)

## Documentation

Workspace-local guides, generated examples, and validation entry points for authored documentation.

Onboarding relevance: useful early when the workspace is documentation-heavy or example-driven.

Available tools: documentation guides, validation scripts

Read more: [Documentation](documentation.md)

## Document Processing

Conversion, PDF generation, Office processing, diagram rendering, and document validation workflows.

Onboarding relevance: useful when you need reliable PDF, Office, or conversion workflows without tool discovery work.

Available tools: Pandoc, LibreOffice, PDF tooling

Read more: [Document Processing](document-processing.md)

## OCR

Optical character recognition for scanned PDFs and image-based documents.

Onboarding relevance: useful when source documents are scanned or image-based.

Available tools: Tesseract, PDF imaging helpers

Read more: [OCR](ocr.md)

## Spell Checking

Proofreading workflows based on installed dictionaries and workspace-local text processing.

Onboarding relevance: useful when document quality and language-aware review matter early.

Available tools: Hunspell, workspace text pipelines

Read more: [Spell Checking](spell-checking.md)

## Analytics

Spreadsheet, tabular, and report-oriented analysis workflows supported by the workspace toolchain.

Onboarding relevance: useful when the workspace is expected to inspect data or automate analytical reporting.

Available tools: Python, LibreOffice, reporting tooling

Read more: [Analytics](analytics.md)

## Reporting

Repeatable report generation across Markdown, HTML, PDF, diagrams, and Office-style outputs.

Onboarding relevance: useful when the workspace exists to produce durable deliverables.

Available tools: Pandoc, Typst, WeasyPrint, ReportLab

Read more: [Reporting](reporting.md)

## Testing

Validation, regression, smoke, and browser-automation workflows available inside the workspace.

Onboarding relevance: useful when the fastest path to trust is to run supported validation flows.

Available tools: Playwright, validation scripts

Read more: [Testing](testing.md)

## Localization

Language-aware workflows for multilingual content, dictionary validation, and text review.

Onboarding relevance: useful when the workspace handles multiple languages or post-OCR proofreading.

Available tools: Hunspell dictionaries, OCR and text pipelines

Read more: [Localization](localization.md)

## Oracle

Oracle Database Free, SQLcl, ORDS, APEX, APEXlang, and Oracle onboarding workflows.

Onboarding relevance: start here for Oracle-enabled workspaces so you can follow the intended PL/SQL to APEX to APEXlang progression.

Available tools: SQLcl, Data Pump, ORDS, APEX, APEXlang, SQL Developer

Read more: [Oracle](oracle.md)
