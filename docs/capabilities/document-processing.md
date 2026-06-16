# Document Processing

## What It Is

This capability exposes the installed tools for Markdown, HTML, PDF, Office documents, diagrams, and document inspection so an attached agent can immediately understand the available conversion paths.

## Why Use It

Use it when you need to convert Office files, generate PDFs, inspect document output, or automate repeatable documentation workflows.

## Available Tools

### Pandoc

Purpose: Convert Markdown and other text formats into PDF and other deliverables.

Supported workflows: Markdown to PDF, format conversion, document automation.

Common use cases: build manuals, export reports.

### LibreOffice

Purpose: Process Office documents and convert spreadsheet or word-processing files.

Supported workflows: Office document conversion, spreadsheet export, headless batch processing.

Common use cases: convert DOCX or XLSX files, produce PDF from Office formats.

### PDF Tooling

Purpose: Inspect, validate, and transform PDFs.

Supported workflows: PDF inspection, metadata checks, conversion support.

Common use cases: verify generated PDFs, extract or compare PDF metadata.

## Typical Tasks

- Convert Markdown, HTML, or Office documents into PDF deliverables.
- Inspect PDF output and metadata after generation.
- Use repeatable conversion scripts instead of manual desktop workflows.

## Examples

- Convert `samples/documentation/report.md` into PDF.
- Convert Office documents or spreadsheets through LibreOffice in headless mode.
- Review PDF metadata and validation output under `artifacts/documentation-demo/`.

## Related Documentation

- [Documentation Workspace Guide](../documentation-features.md)
Generated guide for document-processing workflows.
- [Capability Catalog](README.md)
Entry point for related document workflows.

## Related Capabilities

- [Documentation](documentation.md)
- [Analytics](analytics.md)
- [Reporting](reporting.md)
- [OCR](ocr.md)
