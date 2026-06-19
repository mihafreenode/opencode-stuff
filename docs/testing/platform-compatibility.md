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

`opencode validate-platform` distinguishes between Buildx build support and container execution support. A builder may not advertise `linux/arm64` while `docker run --platform linux/arm64 ...` still works correctly on the current machine.

If `linux/arm64` container execution fails locally with an `exec format error`, treat that as a host-specific validation failure. It means the current machine cannot execute `linux/arm64` containers in its present configuration, not that the workspace is invalid on real ARM64 hardware.

Typical remedies are:

- enable container emulation
- use a builder or runtime with `linux/arm64` support
- validate on real ARM64 hardware

### ARM64 Validation Fails With exec format error

Symptoms:

```text
Container execution: Failed
exec /usr/bin/uname: exec format error
```

Explanation:

The local Docker environment cannot currently execute ARM64 containers.

Possible causes:

- ARM64 emulation not installed
- Buildx builder missing ARM64 support
- QEMU/binfmt registration missing

Remediation:

```bash
docker run --privileged --rm tonistiigi/binfmt --install arm64
docker buildx create --use --name multiarch
docker buildx inspect --bootstrap
docker buildx ls
```

Expected result:

```text
linux/arm64
```

appears in Buildx platform support.

Validation check:

```bash
docker run --rm --platform linux/arm64 ubuntu:24.04 uname -m
```

Expected:

```text
aarch64
```

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

This runtime execution probe is related to Buildx support, but it is not the same signal. Buildx helps validate multi-architecture image build paths, while `docker run --platform ...` confirms whether the local runtime can actually execute a container for the requested target.

Before changing provisioning, runtime logic, package installation, container images, or template definitions, validate both:

```bash
docker buildx build --platform linux/amd64 --load .
docker buildx build --platform linux/arm64 --load .
```

Real Windows ARM64, Linux ARM64, and Apple Silicon devices remain the final release validation step.
