# Smoke CLI Contract

The generic smoke CLI is the stable automation surface for smoke execution, cleanup, and runtime inspection.

## Commands

Supported commands:

```text
opencode smoke list
opencode smoke run <template>
opencode smoke run --family <family>
opencode smoke run --all
opencode smoke cleanup

opencode runtime list
opencode runtime doctor
```

Common smoke options:

```text
--format text|json
--quiet
--verbose
--artifacts-root <path>
--timeout <hh:mm:ss>
--keep-workspace
--keep-runtime-on-failure
```

Selection rules:

- use exactly one selector for `smoke run`: positional template id, `--family <family>`, or `--all`
- unknown template ids fail with exit code `6`
- unknown families fail with exit code `6`
- empty selections fail with exit code `6`
- unsupported templates serialize as `unsupportedSmokeTemplate` and map to exit code `6`
- ordering is deterministic by resource class and template id

## JSON

Structured output uses schema version `1`.

Every JSON document includes:

```json
{
  "schemaVersion": "1",
  "kind": "..."
}
```

Current kinds:

- `smokeDefinitionCatalog`
- `smokeRun`
- `smokeMatrix`
- `smokeCleanup`
- `runtimeInventory`

Conventions:

- camelCase property names
- ISO-8601 UTC timestamps
- enum values serialized as stable camelCase strings
- secret-bearing generated files are redacted in artifacts
- cleanup failure and original failure remain separate in `smokeRun`
- JSON stdout contains JSON only when `--format json` is selected

Artifact discovery fields:

- `runId` or `matrixRunId`
- `artifactDirectory`
- `summaryJsonPath`
- `summaryTextPath`
- `selectedTemplates`
- `status`

## Exit Codes

```text
0    success
1    smoke, provisioning, or validation failure
2    invalid command or configuration
3    cleanup verification failure
4    lock or concurrency failure
5    runtime resource exhaustion
6    unsupported template or selection
7    internal or validation-tooling failure
130  cancelled
```

`OracleRuntimeSmoke` maps generic smoke outcomes to the same exit-code table.

## Cancellation

- first Ctrl+C requests cancellation and returns exit code `130`
- active runs still execute bounded cleanup
- cleanup uses a separate timeout and does not reuse the cancelled execution token
- second Ctrl+C is not intercepted and may terminate the process immediately
- cancellation is serialized as `status=cancelled` and `failureClassification=cancelled`

## Artifacts

Single-run artifacts:

- `summary.json`
- `summary.txt`
- `validation/*.json`
- `cleanup/*.json`
- `docker/*.txt`

Matrix artifacts:

- `matrix-summary.json`
- `matrix-summary.txt`
- `host-before/*`
- `host-after/*`
- per-template run directories

## Examples

```text
opencode smoke run empty-workspace
opencode smoke run --family oracle
opencode smoke run --all --format json
opencode smoke cleanup --dry-run --all --format json
```
