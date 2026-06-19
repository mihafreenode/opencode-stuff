using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceDoctorServiceTests
{
    [Fact]
    public async Task DiagnoseAsync_ReportsHostOsArchitectureAndBuildxPlatforms()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var service = CreateService(new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Linux,
                Architecture = HostArchitecture.Arm64,
                HostDescription = "Linux Arm64",
                NativeContainerPlatform = "linux/arm64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                },
            });

            var result = await service.DiagnoseAsync(root);

            Assert.Equal(HostOperatingSystem.Linux, result.HostPlatform?.OperatingSystem);
            Assert.Equal(HostArchitecture.Arm64, result.HostPlatform?.Architecture);
            Assert.Equal(["linux/amd64", "linux/arm64"], result.HostPlatform?.Docker.SupportedPlatforms);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_WhenDockerCliIsUnavailable_ReturnsActionableRecommendation()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var service = CreateService(new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Windows,
                Architecture = HostArchitecture.X64,
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability { EngineId = "docker", CliAvailable = false },
            }, new FakeRuntimeResolver(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
                SupportLevel = SupportLevel.Unavailable,
                IsAvailable = false,
                DiagnosticExplanation = "Docker CLI unavailable.",
            }));

            var result = await service.DiagnoseAsync(root);

            Assert.False(result.CanRun);
            Assert.Contains("Install Docker Desktop", result.Recommendation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_WhenDockerEngineIsUnavailable_ReturnsActionableRecommendation()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var service = CreateService(new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Windows,
                Architecture = HostArchitecture.X64,
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability { EngineId = "docker", CliAvailable = true, EngineReachable = false },
            }, new FakeRuntimeResolver(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
                SupportLevel = SupportLevel.Unavailable,
                IsAvailable = false,
                DiagnosticExplanation = "Docker engine is not reachable.",
            }));

            var result = await service.DiagnoseAsync(root);

            Assert.False(result.CanRun);
            Assert.Contains("Start Docker Desktop", result.Recommendation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_ReportsBuildxUnavailableAndMissingRuntimeState()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var service = CreateService(new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.MacOS,
                Architecture = HostArchitecture.Arm64,
                NativeContainerPlatform = "linux/arm64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = false,
                },
            });

            var result = await service.DiagnoseAsync(root);

            Assert.False(result.HostPlatform?.Docker.BuildxAvailable);
            Assert.Equal(WorkspaceRuntimeStateReadStatus.Missing, result.RuntimeStateStatus);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_ReportsWorkspaceAndRuntimeStateFound()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            var runtimeStateService = new WorkspaceRuntimeStateService();
            runtimeStateService.Write(paths.RuntimeStatePath, new WorkspaceRuntimeStateRecord
            {
                ResolvedEngine = "docker",
                ResolvedPlatform = "linux/amd64",
                CompatibilityMode = "Native",
                LastSuccessfulProvision = DateTimeOffset.UtcNow,
            });
            var service = CreateService(CreateReadyHostPlatform());

            var result = await service.DiagnoseAsync(root);

            Assert.Equal(WorkspaceConfigurationStatus.Found, result.WorkspaceConfigurationStatus);
            Assert.Equal(WorkspaceRuntimeStateReadStatus.Loaded, result.RuntimeStateStatus);
            Assert.NotNull(result.RuntimeState);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_WhenWorkspaceYamlIsMissing_ReportsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"doctor-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = CreateService(CreateReadyHostPlatform());

            var result = await service.DiagnoseAsync(root);

            Assert.Equal(WorkspaceConfigurationStatus.NotFound, result.WorkspaceConfigurationStatus);
            Assert.Contains("workspace.yaml was not found", result.Recommendation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_WhenRuntimeStateIsCorrupted_DoesNotFail()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.RuntimeStatePath)!);
            File.WriteAllText(paths.RuntimeStatePath, "resolvedEngine: [broken\n");
            var service = CreateService(CreateReadyHostPlatform());

            var result = await service.DiagnoseAsync(root);

            Assert.Equal(WorkspaceRuntimeStateReadStatus.Corrupted, result.RuntimeStateStatus);
            Assert.Equal(WorkspaceConfigurationStatus.Found, result.WorkspaceConfigurationStatus);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_DoesNotMutateWorkspaceYaml()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var yamlPath = Path.Combine(root, "workspace.yaml");
            var before = File.ReadAllText(yamlPath);
            var service = CreateService(CreateReadyHostPlatform());

            _ = await service.DiagnoseAsync(root);

            Assert.Equal(before, File.ReadAllText(yamlPath));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static WorkspaceDoctorService CreateService(HostPlatformInfo hostPlatform, IRuntimeResolver? runtimeResolver = null)
        => new(
            new FakePlatformDetector(hostPlatform),
            runtimeResolver ?? new FakeRuntimeResolver(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = hostPlatform.NativeContainerPlatform,
                CompatibilityMode = RuntimeCompatibilityMode.Native,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                DiagnosticExplanation = "Workspace can run on this machine.",
                HostPlatform = hostPlatform,
            }),
            new WorkspaceDiscoveryService(),
            new WorkspaceYamlService(),
            new WorkspaceRuntimeStateService());

    private static HostPlatformInfo CreateReadyHostPlatform()
        => new()
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

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"doctor-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), new WorkspaceYamlService().Write(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "doctor-demo", Image = "ubuntu:24.04" },
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
