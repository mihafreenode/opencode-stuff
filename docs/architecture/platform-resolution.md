# Platform Resolution

OpenCode keeps the repository durable and portable while resolving runtime details locally on each machine.

## Core rule

- `workspace.yaml` stays architecture-neutral
- runtime resolution happens at execution time
- `.opencode/local/` stores machine-local cache data only

## `opencode doctor`

`opencode doctor` explains the current host and runtime situation without depending on the Windows UI.

It reports:

- detected host OS and architecture
- Docker CLI and engine availability
- Buildx availability and advertised platforms
- resolved runtime plan when `workspace.yaml` is present
- whether `.opencode/local/runtime-state.yaml` exists

## `opencode validate-platform`

`opencode validate-platform --target <platform>` validates whether a workspace can be generated and plausibly executed for a requested Linux target.

The first phase checks:

- target support
- workspace configuration loading
- runtime resolution
- Buildx platform advertisement when available
- container execution probe for the requested target when Docker runtime probing is possible
- compose generation
- provisioning script generation

Buildx build support and runtime execution support are related but not identical. A machine may execute `linux/arm64` containers successfully even when the active Buildx builder only advertises `linux/amd64` variants.

The inverse can also happen: generation may succeed while the current host cannot execute `linux/arm64` containers locally. In that case `validate-platform` reports a host validation failure and recommends emulation, a runtime with `linux/arm64` support, or validation on real ARM64 hardware.

This is generation and compatibility validation with a lightweight execution probe, not a full runtime smoke test.

## Resolution policy

The portable core prefers:

1. native runtime
2. native architecture
3. compatible multi-architecture support when native-only confirmation is unavailable

## Validation limits

Buildx and QEMU improve confidence before real device testing, but they are not the final release signal.

Final validation still belongs on:

- Windows ARM64
- Linux ARM64
- Apple Silicon
