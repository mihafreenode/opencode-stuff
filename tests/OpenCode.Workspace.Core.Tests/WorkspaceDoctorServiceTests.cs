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
    public async Task DiagnoseAsync_Arm64SupportAvailable_WhenExecutionProbeSucceeds()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var result = await CreateService(
                CreateReadyHostPlatform(),
                arm64ExecutionProbe: _ => Task.FromResult(Success("docker run", "aarch64")))
                .DiagnoseAsync(root);

            Assert.Equal(Arm64ExecutionSupportStatus.Available, result.Arm64ExecutionSupportStatus);
            Assert.Contains("aarch64", result.Arm64ExecutionSupportDetails, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_Arm64SupportInferredFromExecutionProbe_WhenBuildxDoesNotAdvertiseArm64()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var hostPlatform = new HostPlatformInfo
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
            };
            var result = await CreateService(
                hostPlatform,
                arm64ExecutionProbe: _ => Task.FromResult(Success("docker run", "aarch64")))
                .DiagnoseAsync(root);

            Assert.Equal(Arm64ExecutionSupportStatus.Available, result.Arm64ExecutionSupportStatus);
            Assert.Contains("Execution probe OK", result.Arm64ExecutionSupportDetails, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_Arm64SupportUnavailable_WhenExecutionProbeFails()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var result = await CreateService(
                CreateReadyHostPlatform(),
                arm64ExecutionProbe: _ => Task.FromResult(Failure("docker run", "exec format error")))
                .DiagnoseAsync(root);

            Assert.Equal(Arm64ExecutionSupportStatus.Unavailable, result.Arm64ExecutionSupportStatus);
            Assert.Contains("exec format error", result.Arm64ExecutionSupportDetails, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_Arm64SupportInferredFromBuildx_WhenProbeUnavailableAndAdvertised()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var hostPlatform = new HostPlatformInfo
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
                    SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                },
            };
            var result = await CreateService(
                hostPlatform,
                arm64ExecutionProbe: _ => throw new TimeoutException("probe skipped"))
                .DiagnoseAsync(root);

            Assert.Equal(Arm64ExecutionSupportStatus.Available, result.Arm64ExecutionSupportStatus);
            Assert.Contains("Buildx advertises linux/arm64", result.Arm64ExecutionSupportDetails, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_Arm64SupportInferredFromBuildx_WhenProbeUnavailableAndNotAdvertised()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var hostPlatform = new HostPlatformInfo
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
            };
            var result = await CreateService(
                hostPlatform,
                arm64ExecutionProbe: _ => throw new TimeoutException("probe skipped"))
                .DiagnoseAsync(root);

            Assert.Equal(Arm64ExecutionSupportStatus.Unavailable, result.Arm64ExecutionSupportStatus);
            Assert.Contains("Buildx does not advertise linux/arm64", result.Arm64ExecutionSupportDetails, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_OnNativeLinuxAmd64Workspace_ReturnsRunnableNativePlan()
    {
        var root = CreateWorkspaceRoot();

        try
        {
            var hostPlatform = new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Linux,
                Architecture = HostArchitecture.X64,
                HostDescription = "Linux X64",
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                    DiagnosticSummary = "Docker CLI available. Docker engine reachable. Docker Buildx available for linux/amd64, linux/arm64.",
                },
            };
            var service = CreateService(hostPlatform, new RuntimeResolver());

            var result = await service.DiagnoseAsync(root);

            Assert.True(result.CanRun);
            Assert.Equal("docker", result.ResolvedRuntimePlan?.Runtime);
            Assert.Equal("linux/amd64", result.ResolvedRuntimePlan?.TargetPlatform);
            Assert.Equal("linux/amd64", result.ResolvedRuntimePlan?.HostPlatform.NativeContainerPlatform, ignoreCase: true);
            Assert.Equal("Workspace can run on this machine.", result.Recommendation);
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
            Assert.NotNull(result.RuntimeInventory);
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

    private static WorkspaceDoctorService CreateService(HostPlatformInfo hostPlatform, IRuntimeResolver? runtimeResolver = null, Func<CancellationToken, Task<ProcessResult>>? arm64ExecutionProbe = null)
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
            new WorkspaceRuntimeStateService(),
            arm64ExecutionProbe ?? (_ => Task.FromResult(Success("docker run", "aarch64"))));

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
}
