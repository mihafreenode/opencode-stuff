using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class PlatformValidationServiceTests
{
    [Theory]
    [InlineData("linux/amd64")]
    [InlineData("linux/arm64")]
    public async Task ValidateAsync_AcceptsSupportedTargets(string targetPlatform)
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(root).ValidateAsync(new PlatformValidationRequest
            {
                WorkspacePath = root,
                TargetPlatform = targetPlatform,
            });

            Assert.True(report.IsSuccess);
            Assert.Equal(targetPlatform, report.TargetPlatform);
            Assert.Contains(report.Checks, check => check.Name == "Compose generation" && check.Severity == DiagnosticSeverity.Information);
            Assert.Contains(report.Checks, check => check.Name == "Provisioning generation" && check.Severity == DiagnosticSeverity.Information);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnsupportedTarget()
    {
        var report = await CreateService(CreateWorkspaceRoot()).ValidateAsync(new PlatformValidationRequest
        {
            WorkspacePath = Environment.CurrentDirectory,
            TargetPlatform = "linux/ppc64le",
        });

        Assert.False(report.IsSuccess);
        Assert.Contains(report.Checks, check => check.Name == "Target" && check.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ValidateAsync_ReportsWorkspaceConfigParseFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"platform-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), "workspace: [broken\n");

        try
        {
            var report = await CreateService(root).ValidateAsync(new PlatformValidationRequest
            {
                WorkspacePath = root,
                TargetPlatform = "linux/amd64",
            });

            Assert.False(report.IsSuccess);
            Assert.Contains(report.Checks, check => check.Name == "Workspace config" && check.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_ReportsRuntimeResolutionFailure()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(root, runtimeResolver: new FakeRuntimeResolver(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
                SupportLevel = SupportLevel.Unavailable,
                IsAvailable = false,
                DiagnosticExplanation = "Docker engine is not reachable.",
            })).ValidateAsync(new PlatformValidationRequest
            {
                WorkspacePath = root,
                TargetPlatform = "linux/amd64",
            });

            Assert.False(report.IsSuccess);
            Assert.Contains(report.Checks, check => check.Name == "Runtime resolution" && check.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenBuildxIsMissing_ReturnsWarning()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(root, hostPlatform: new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Windows,
                Architecture = HostArchitecture.X64,
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability { EngineId = "docker", CliAvailable = true, EngineReachable = true, BuildxAvailable = false },
            }).ValidateAsync(new PlatformValidationRequest
            {
                WorkspacePath = root,
                TargetPlatform = "linux/arm64",
            });

            Assert.True(report.IsSuccess);
            Assert.True(report.HasWarnings);
            Assert.Contains(report.Checks, check => check.Name == "Buildx build support" && check.Severity == DiagnosticSeverity.Warning);
            Assert.Contains(report.Checks, check => check.Name == "Container execution" && check.Severity == DiagnosticSeverity.Information);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenRequestedPlatformIsNotAdvertised_ReturnsWarning()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(root, hostPlatform: new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Windows,
                Architecture = HostArchitecture.X64,
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64"],
                },
            }).ValidateAsync(new PlatformValidationRequest
            {
                WorkspacePath = root,
                TargetPlatform = "linux/arm64",
            });

            Assert.True(report.IsSuccess);
            Assert.True(report.HasWarnings);
            Assert.Contains(report.Checks, check => check.Name == "Buildx build support" && check.Severity == DiagnosticSeverity.Warning);
            Assert.Contains(report.Checks, check => check.Name == "Container execution" && check.Severity == DiagnosticSeverity.Information);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_FailsWhenComposeGenerationFails()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                composeGeneration: (_, _) => throw new InvalidOperationException("compose failed")).ValidateAsync(new PlatformValidationRequest
                {
                    WorkspacePath = root,
                    TargetPlatform = "linux/amd64",
                });

            Assert.False(report.IsSuccess);
            Assert.Contains(report.Checks, check => check.Name == "Compose generation" && check.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_FailsWhenProvisioningGenerationFails()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                provisioningGeneration: _ => throw new InvalidOperationException("provision failed")).ValidateAsync(new PlatformValidationRequest
                {
                    WorkspacePath = root,
                    TargetPlatform = "linux/amd64",
                });

            Assert.False(report.IsSuccess);
            Assert.Contains(report.Checks, check => check.Name == "Provisioning generation" && check.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_DoesNotMutateWorkspaceYaml_AndReturnsMachineReadableChecks()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var yamlPath = Path.Combine(root, "workspace.yaml");
            var before = File.ReadAllText(yamlPath);
            var report = await CreateService(root).ValidateAsync(new PlatformValidationRequest
            {
                WorkspacePath = root,
                TargetPlatform = "linux/amd64",
            });

            Assert.True(report.IsSuccess);
            Assert.NotEmpty(report.Checks);
            Assert.All(report.Checks, check => Assert.False(string.IsNullOrWhiteSpace(check.Name)));
            Assert.Equal(before, File.ReadAllText(yamlPath));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenBuildxMissingTargetAndContainerExecutionFails_ReturnsFailure()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                hostPlatform: new HostPlatformInfo
                {
                    OperatingSystem = HostOperatingSystem.Windows,
                    Architecture = HostArchitecture.X64,
                    NativeContainerPlatform = "linux/amd64",
                    Docker = new ContainerRuntimeAvailability
                    {
                        EngineId = "docker",
                        CliAvailable = true,
                        EngineReachable = true,
                        BuildxAvailable = true,
                        SupportedPlatforms = ["linux/amd64"],
                    },
                },
                containerExecutionProbe: (_, _) => Task.FromResult(Failure("docker run", "exec format error"))).ValidateAsync(new PlatformValidationRequest
                {
                    WorkspacePath = root,
                    TargetPlatform = "linux/arm64",
                });

            Assert.False(report.IsSuccess);
            Assert.Contains(report.Checks, check => check.Name == "Buildx build support" && check.Severity == DiagnosticSeverity.Warning);
            Assert.Contains(report.Checks, check => check.Name == "Container execution" && check.Severity == DiagnosticSeverity.Error);
            Assert.Contains(report.Checks, check => check.Name == "Compose generation" && check.Severity == DiagnosticSeverity.Information);
            Assert.Contains(report.Checks, check => check.Name == "Provisioning generation" && check.Severity == DiagnosticSeverity.Information);
            Assert.Equal("linux/arm64 validation failed on this host.", report.Summary);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenArm64ExecutionFailsWithExecFormatError_ReportsHostSpecificGuidance()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                hostPlatform: new HostPlatformInfo
                {
                    OperatingSystem = HostOperatingSystem.Linux,
                    Architecture = HostArchitecture.X64,
                    NativeContainerPlatform = "linux/amd64",
                    Docker = new ContainerRuntimeAvailability
                    {
                        EngineId = "docker",
                        CliAvailable = true,
                        EngineReachable = true,
                        BuildxAvailable = true,
                        SupportedPlatforms = ["linux/amd64"],
                    },
                },
                containerExecutionProbe: (_, _) => Task.FromResult(Failure("docker run", "exec /usr/bin/uname: exec format error"))).ValidateAsync(new PlatformValidationRequest
                {
                    WorkspacePath = root,
                    TargetPlatform = "linux/arm64",
                });

            var executionCheck = Assert.Single(report.Checks, check => check.Name == "Container execution");
            Assert.Equal(DiagnosticSeverity.Error, executionCheck.Severity);
            Assert.Contains("This host cannot currently execute linux/arm64 containers.", executionCheck.Message, StringComparison.Ordinal);
            Assert.Contains("Possible fixes:", executionCheck.Message, StringComparison.Ordinal);
            Assert.Contains("- Install ARM64 emulation support", executionCheck.Message, StringComparison.Ordinal);
            Assert.Contains("- Configure a Buildx builder with linux/arm64 support", executionCheck.Message, StringComparison.Ordinal);
            Assert.Contains("- Validate on real ARM64 hardware", executionCheck.Message, StringComparison.Ordinal);
            Assert.Contains("exec /usr/bin/uname: exec format error", executionCheck.Message, StringComparison.Ordinal);
            Assert.Equal("linux/arm64 validation failed on this host.", report.Summary);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_RequestedArm64_WithResolvedArm64_ReportsDirectValidation()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                hostPlatform: new HostPlatformInfo
                {
                    OperatingSystem = HostOperatingSystem.Linux,
                    Architecture = HostArchitecture.Arm64,
                    NativeContainerPlatform = "linux/arm64",
                    Docker = new ContainerRuntimeAvailability
                    {
                        EngineId = "docker",
                        CliAvailable = true,
                        EngineReachable = true,
                        BuildxAvailable = true,
                        SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                    },
                },
                runtimeResolver: new FakeRuntimeResolver(new ResolvedRuntimePlan
                {
                    Runtime = "docker",
                    TargetPlatform = "linux/arm64",
                    CompatibilityMode = RuntimeCompatibilityMode.Native,
                    SupportLevel = SupportLevel.NativeTested,
                    IsAvailable = true,
                    DiagnosticExplanation = "OK",
                })).ValidateAsync(new PlatformValidationRequest { WorkspacePath = root, TargetPlatform = "linux/arm64" });

            Assert.True(report.IsSuccess);
            Assert.Equal("linux/arm64", report.TargetPlatform);
            Assert.Equal("linux/arm64", report.ResolvedPlatform);
            Assert.Equal("direct", report.CompatibilityDisplay);
            Assert.False(report.ValidatedWithFallback);
            Assert.Contains(report.Checks, check => check.Name == "Container execution" && check.Message.Contains("aarch64", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_RequestedArm64_WithResolvedAmd64_ReportsFallbackValidation()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                runtimeResolver: new FakeRuntimeResolver(new ResolvedRuntimePlan
                {
                    Runtime = "docker",
                    TargetPlatform = "linux/amd64",
                    CompatibilityMode = RuntimeCompatibilityMode.Emulated,
                    SupportLevel = SupportLevel.EmulatedTested,
                    IsAvailable = true,
                    DiagnosticExplanation = "OK",
                })).ValidateAsync(new PlatformValidationRequest { WorkspacePath = root, TargetPlatform = "linux/arm64" });

            Assert.True(report.IsSuccess);
            Assert.Equal("linux/arm64", report.TargetPlatform);
            Assert.Equal("linux/amd64", report.ResolvedPlatform);
            Assert.Equal("emulated fallback", report.CompatibilityDisplay);
            Assert.True(report.ValidatedWithFallback);
            Assert.Contains(report.Checks, check => check.Name == "Container execution" && check.Message.Contains("aarch64", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ValidateAsync_RequestedAmd64_WithResolvedAmd64_ReportsDirectValidation()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var report = await CreateService(
                root,
                runtimeResolver: new FakeRuntimeResolver(new ResolvedRuntimePlan
                {
                    Runtime = "docker",
                    TargetPlatform = "linux/amd64",
                    CompatibilityMode = RuntimeCompatibilityMode.Native,
                    SupportLevel = SupportLevel.NativeTested,
                    IsAvailable = true,
                    DiagnosticExplanation = "OK",
                })).ValidateAsync(new PlatformValidationRequest { WorkspacePath = root, TargetPlatform = "linux/amd64" });

            Assert.True(report.IsSuccess);
            Assert.Equal("linux/amd64", report.TargetPlatform);
            Assert.Equal("linux/amd64", report.ResolvedPlatform);
            Assert.Equal("direct", report.CompatibilityDisplay);
            Assert.False(report.ValidatedWithFallback);
            Assert.Contains(report.Checks, check => check.Name == "Container execution" && check.Message.Contains("x86_64", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static PlatformValidationService CreateService(
        string root,
        HostPlatformInfo? hostPlatform = null,
        IRuntimeResolver? runtimeResolver = null,
        Func<ResolvedWorkspace, WorkspacePaths, string>? composeGeneration = null,
        Func<ResolvedWorkspace, string>? provisioningGeneration = null,
        Func<string, CancellationToken, Task<ProcessResult>>? containerExecutionProbe = null)
    {
        var effectiveHost = hostPlatform ?? new HostPlatformInfo
        {
            OperatingSystem = HostOperatingSystem.Windows,
            Architecture = HostArchitecture.X64,
            NativeContainerPlatform = "linux/amd64",
            Docker = new ContainerRuntimeAvailability
            {
                EngineId = "docker",
                CliAvailable = true,
                EngineReachable = true,
                BuildxAvailable = true,
                SupportedPlatforms = ["linux/amd64", "linux/arm64"],
            },
        };
        var resolver = new WorkspaceResolver(
            [new FeatureManifest { Id = "core", DisplayName = "Core", Description = "Core" }],
            Array.Empty<ServiceManifest>(),
            Array.Empty<CapabilityManifest>(),
            Array.Empty<KnowledgePackManifest>());
        return new PlatformValidationService(
            new WorkspaceDiscoveryService(),
            new WorkspaceYamlService(),
            new FakePlatformDetector(effectiveHost),
            runtimeResolver ?? new FakeRuntimeResolver(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = effectiveHost.NativeContainerPlatform,
                CompatibilityMode = RuntimeCompatibilityMode.Native,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                DiagnosticExplanation = "OK",
                HostPlatform = effectiveHost,
            }),
            resolver,
            composeGeneration ?? new ComposeGenerator().Generate,
            provisioningGeneration ?? new ProvisioningScriptGenerator().Generate,
            containerExecutionProbe ?? DefaultContainerExecutionProbe);
    }

    private static Task<ProcessResult> DefaultContainerExecutionProbe(string targetPlatform, CancellationToken cancellationToken)
        => Task.FromResult(Success("docker run", targetPlatform.Equals("linux/arm64", StringComparison.OrdinalIgnoreCase) ? "aarch64" : "x86_64"));

    [Fact]
    public void IsExpectedExecutionArchitecture_AcceptsArm64Aarch64()
        => Assert.True(PlatformValidationService.IsExpectedExecutionArchitecture("linux/arm64", "aarch64"));

    [Fact]
    public void IsExpectedExecutionArchitecture_AcceptsAmd64X8664()
        => Assert.True(PlatformValidationService.IsExpectedExecutionArchitecture("linux/amd64", "x86_64"));

    private static ProcessResult Success(string command, string stdout) => new()
    {
        Command = command,
        ExitCode = 0,
        StandardOutput = stdout,
        StandardError = string.Empty,
        StandardOutputLines = [stdout],
        StandardErrorLines = Array.Empty<string>(),
        Duration = TimeSpan.FromMilliseconds(10),
    };

    private static ProcessResult Failure(string command, string stderr) => new()
    {
        Command = command,
        ExitCode = 1,
        StandardOutput = string.Empty,
        StandardError = stderr,
        StandardOutputLines = Array.Empty<string>(),
        StandardErrorLines = [stderr],
        Duration = TimeSpan.FromMilliseconds(10),
    };

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"platform-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), new WorkspaceYamlService().Write(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "validation-demo", Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core"],
        }));
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class FakePlatformDetector(HostPlatformInfo hostPlatform) : IPlatformDetector
    {
        public Task<HostPlatformInfo> DetectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(hostPlatform);
    }

    private sealed class FakeRuntimeResolver(ResolvedRuntimePlan plan) : IRuntimeResolver
    {
        public Task<ResolvedRuntimePlan> ResolveAsync(WorkspaceDefinition definition, HostPlatformInfo hostPlatform, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedRuntimePlan
            {
                Runtime = plan.Runtime,
                TargetPlatform = plan.TargetPlatform,
                CompatibilityMode = plan.CompatibilityMode,
                SupportLevel = plan.SupportLevel,
                IsAvailable = plan.IsAvailable,
                DiagnosticExplanation = plan.DiagnosticExplanation,
                HostPlatform = hostPlatform,
            });
    }
}
