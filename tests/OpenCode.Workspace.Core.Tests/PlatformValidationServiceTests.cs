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
            Assert.Contains(report.Checks, check => check.Name == "Buildx support" && check.Severity == DiagnosticSeverity.Warning);
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
            Assert.Contains(report.Checks, check => check.Name == "Buildx support" && check.Severity == DiagnosticSeverity.Warning);
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

    private static PlatformValidationService CreateService(
        string root,
        HostPlatformInfo? hostPlatform = null,
        IRuntimeResolver? runtimeResolver = null,
        Func<ResolvedWorkspace, WorkspacePaths, string>? composeGeneration = null,
        Func<ResolvedWorkspace, string>? provisioningGeneration = null)
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
            provisioningGeneration ?? new ProvisioningScriptGenerator().Generate);
    }

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
