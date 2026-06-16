# Testing

## What It Is

This capability captures the testing and validation workflows supported by the workspace, including regression checks, smoke tests, and Playwright-based browser automation when installed.

## Why Use It

Use it when you want to verify the workspace toolchain quickly or automate browser and document validation tasks.

## Available Tools

### Playwright

Purpose: Browser automation for smoke tests, regression checks, and capture workflows.

Supported workflows: browser regression testing, UI smoke testing, automated validation.

Common use cases: verify rendered output, reproduce browser flows.

### Workspace Validation Scripts

Purpose: Quick capability and environment smoke tests.

Supported workflows: regression testing, tool availability checks, documentation smoke testing.

Common use cases: validate installed tools, confirm workspace readiness.

## Typical Tasks

- Run regression testing or smoke testing after reprovision or update.
- Use Playwright when browser automation is required.
- Validate documentation and rendering toolchains before relying on generated outputs.

## Examples

- Run documentation smoke tests before generating deliverables.
- Use Playwright or equivalent tooling to validate browser-based workflows.

## Related Documentation

- [Documentation Workspace Guide](../documentation-features.md)
Includes validation and demo scripts for documentation-heavy workspaces.

## Related Capabilities

- [Documentation](documentation.md)
- [Reporting](reporting.md)
- [Oracle](oracle.md)
