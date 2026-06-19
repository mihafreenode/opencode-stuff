# Analytics & Reporting Workspace

The Analytics & Reporting workspace is a complete environment for exploring data, building dashboards, generating reports, and learning modern Python-based analytical workflows.

It is intended to make analytical work durable, reproducible, and friendly to both Git and AI-assisted collaboration.

## Goals

- help users move from raw spreadsheet or tabular inputs into repeatable analysis
- keep notebooks, scripts, dashboards, and reports as normal repository assets
- support AI-assisted exploration without hiding the underlying work
- make it practical to revisit, review, and improve analytical workflows over time

## Onboarding Flow

Use the same repository-first discovery model described elsewhere in the product:

```text
Repository
    ↓
Workspace Discovery
    ↓
Provision Environment
    ↓
Read Documentation
    ↓
Start Working
```

Recommended first experience:

```text
Create Analytics Workspace
    ↓
Provision Runtime
    ↓
Open Marimo
    ↓
Explore Sample Data
    ↓
Generate KPI Dashboard
    ↓
Export Report
```

## What You Can Work With

- Marimo
- Pandas
- Excel
- CSV
- JSON
- Plotly
- Matplotlib
- statistics
- dashboards
- reports

Marimo notebooks are stored as normal Python files, which makes them easier to review, compare, and keep in Git than opaque notebook formats.

Analytical assets should live in the repository:

- source data when appropriate
- cleaned datasets
- Python notebooks and scripts
- charts and dashboards
- Markdown notes
- generated reports

## Why This Workspace Exists

Many teams can already open a spreadsheet or run a notebook, but that work is often difficult to reproduce, explain, or hand off.

This workspace exists to make analytical work more durable:

- workflows are Git-friendly
- reports are reproducible
- validation scripts can confirm the environment quickly
- AI agents can participate naturally in normal Python and Markdown assets

## Example Projects

- build a KPI dashboard from CSV exports
- turn Excel and JSON inputs into a repeatable weekly report
- explore sample business data and explain patterns with charts
- create a survey analysis notebook with reproducible summary tables
- generate a client-ready PDF report from analytical outputs

## Skills And Knowledge Packs

Common supporting assets include:

- sample datasets
- guided skills
- validation scripts
- knowledge packs

Start with:

- [Analytics Capability](capabilities/analytics.md)
- [Reporting Capability](capabilities/reporting.md)
- [Analytics Agent Onboarding](reference/agent-onboarding/analytics.md)
- [Education Knowledge Pack](features/education-knowledge-pack.md) when the workspace is used for teaching or learning

## Recommended Learning Path

1. Start with a small CSV or Excel file.
2. Open the workspace documentation before writing code.
3. Ask OpenCode to explain the available tools and suggest a first workflow.
4. Explore the dataset in Marimo.
5. Build one chart and one summary table.
6. Turn the result into a dashboard or report.
7. Review the generated code and explanation until the workflow is understood.

Users do not need prior Python experience to begin.

Understanding the generated work remains important.

## AI-Assisted Learning Guidance

OpenCode can help:

- generate analytical code
- explain analytical code
- create charts
- build reports
- troubleshoot workflows

Useful first questions:

- What data files are available here?
- Which workflow should I use for Excel versus CSV?
- Can you explain this Pandas code before I run it?
- Can you help me turn this analysis into a KPI dashboard?
- How can I export this result as a durable report?

AI is most useful here as a collaborator that accelerates exploration and explanation. It should not replace checking assumptions, reviewing results, or understanding how the workflow works.
