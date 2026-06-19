# Platform Compatibility

Use the portable CLI diagnostics before changing provisioning, runtime resolution, package installation, container images, or template-driven runtime behavior.

## Commands

Run workspace diagnostics:

```bash
opencode doctor
```

Validate a requested Linux target:

```bash
opencode validate-platform --target linux/amd64
opencode validate-platform --target linux/arm64
```

Native execution is preferred. Compatibility validation is automatic when Docker and Buildx can provide it.

Buildx and QEMU validation improve confidence, but they do not replace final validation on real hardware.

## Multi-Architecture Validation

Run:

```bash
docker buildx ls
```

A healthy active builder should include:

```text
linux/amd64
linux/arm64
```

Verify ARM64 execution:

```bash
docker run --rm --platform linux/arm64 ubuntu:24.04 uname -m
```

Expected:

```text
aarch64
```

Before changing provisioning, runtime logic, package installation, container images, or template definitions, validate both:

```bash
docker buildx build --platform linux/amd64 --load .
docker buildx build --platform linux/arm64 --load .
```

Real Windows ARM64, Linux ARM64, and Apple Silicon devices remain the final release validation step.
