# Documentation

## What It Is

This capability collects the authored guides, generated examples, and validation entry points that explain how to produce readable project documentation inside the workspace.

## Why Use It

Use it when you want the shortest path from workspace attach to understanding the available documentation workflows and their example files.

## Available Tools

### Documentation Guides

Purpose: Explain the workspace-specific authoring and validation flow.

Supported workflows: onboarding, example-driven learning, capability navigation.

Common use cases: find the first docs to read, discover available examples and scripts.

### Validation Scripts

Purpose: Verify documentation and rendering tooling quickly.

Supported workflows: tool smoke checks, first-run validation, regression verification.

Common use cases: confirm the workspace is ready for document work, demonstrate the installed toolchain.

## Typical Tasks

- Read the capability catalog and the documentation workspace guide before building reports or manuals.
- Run documentation validation scripts to confirm the toolchain is installed and ready.
- Use generated sample inputs as a starting point for new documents.

## Examples

- Start with `docs/documentation-features.md` and `scripts/validate-documentation-tooling.sh`.
- Use `samples/documentation/report.md` and `report.html` as first-run examples.

## Related Documentation

- [Documentation Workspace Guide](../documentation-features.md)
Overview of the generated documentation workspace assets.
- [Capability Catalog](README.md)
Entry point for all enabled capabilities.

## Related Capabilities

- [Document Processing](document-processing.md)
- [Analytics](analytics.md)
- [Reporting](reporting.md)
- [Testing](testing.md)
