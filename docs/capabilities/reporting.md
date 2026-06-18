# Reporting

## What It Is

This capability describes how the workspace produces repeatable reports and presentation-ready outputs from authored or processed content.

## Why Use It

Use it when you need business reports, technical documentation, presentation material, worksheets, handouts, or other durable outputs that should stay version-controlled and reproducible.

## Available Tools

### Markdown, Typst, and LaTeX

Purpose: Build printable reports from durable source assets.

Supported workflows: report generation, PDF publishing, repeatable document builds, citations, technical publishing.

Common use cases: build analytical reports, generate manuals, prepare academic papers.

### WeasyPrint and ReportLab

Purpose: Alternative PDF and programmatic report generation paths.

Supported workflows: HTML to PDF, Python-driven reporting, custom report layouts.

Common use cases: produce PDF deliverables from HTML, build scripted reports.

## Typical Tasks

- Generate reports from Markdown, Typst, LaTeX, HTML, or Office-source content.
- Produce durable PDF outputs for sharing or review.
- Include diagrams and tabular content in repeatable report workflows.
- Validate PDFs with `qpdf` and inspect text with `pdftotext`.

## Examples

- Generate a PDF report from Markdown, Typst, or LaTeX sources.
- Produce HTML-to-PDF output and attach inspection metadata.
- Convert SVG diagrams into PDFs and validate the results.

## Related Documentation

- [Documentation Workspace Guide](../documentation-features.md)
Generated guide for report-generation workflows.

- [Publishing Knowledge Pack](../features/publishing-knowledge-pack.md)
Curated references for Markdown, Typst, LaTeX, citations, diagrams, and PDF validation.

## Related Capabilities

- [Analytics](analytics.md)
- [Document Processing](document-processing.md)
- [Documentation](documentation.md)
