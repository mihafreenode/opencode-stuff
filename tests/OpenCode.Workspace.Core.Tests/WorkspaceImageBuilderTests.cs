using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceImageBuilderTests
{
    [Fact]
    public async Task WorkspaceImage_IsReused_WhenMatchingHashAlreadyExists()
    {
        var runtime = new RecordingContainerRuntime();
        var builder = new DockerWorkspaceImageBuilder(runtime);
        var artifacts = CreateArtifacts("opencode-workspace-demo:abc123", "ABC123");
        runtime.SetExistingImage(artifacts.WorkspaceImageTag, artifacts.WorkspaceImageInputHash);

        await builder.EnsureImageAsync(CreateDefinition("demo"), WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "image-reuse")), artifacts);

        Assert.Equal(0, runtime.BuildCallCount);
    }

    [Fact]
    public async Task GenericRebuild_SkipsImmutableTooling_WhenImageInputsDidNotChange()
    {
        var runtime = new RecordingContainerRuntime();
        var builder = new DockerWorkspaceImageBuilder(runtime);
        var artifacts = CreateArtifacts("opencode-workspace-generic:abc123", "ABC123");
        var definition = CreateDefinition("generic");
        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "generic-rebuild"));

        await builder.EnsureImageAsync(definition, paths, artifacts);
        await builder.EnsureImageAsync(definition, paths, artifacts);

        Assert.Equal(1, runtime.BuildCallCount);
    }

    [Fact]
    public async Task OracleReprovision_DoesNotRebuildBaseImage_WhenImageInputsDidNotChange()
    {
        var runtime = new RecordingContainerRuntime();
        var builder = new DockerWorkspaceImageBuilder(runtime);
        var artifacts = CreateArtifacts("opencode-workspace-oracle:abc123", "ABC123");
        var definition = CreateDefinition("oracle-demo", features: ["core", "oracle-demo", "oracle-apex-demo"], services: ["oracle-demo", "oracle-ords"]);
        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "oracle-reprovision"));

        await builder.EnsureImageAsync(definition, paths, artifacts);
        await builder.EnsureImageAsync(definition, paths, artifacts);

        Assert.Equal(1, runtime.BuildCallCount);
    }

    [Fact]
    public void WorkspaceImage_RebuildsOnlyWhenImageInputsChange()
    {
        var metadata = GeneratedArtifactRuntimeMetadataBuilder.Create((WorkspaceRuntimeStateRecord?)null);
        var baseResolved = CreateResolvedWorkspace("demo", aptPackages: ["git"], npmPackages: ["playwright"]);
        var sameResolved = CreateResolvedWorkspace("demo", aptPackages: ["git"], npmPackages: ["playwright"]);
        var changedResolved = CreateResolvedWorkspace("demo", aptPackages: ["git", "pandoc"], npmPackages: ["playwright"]);

        var basePlan = WorkspaceImageBuildPlanner.Create(baseResolved, metadata);
        var samePlan = WorkspaceImageBuildPlanner.Create(sameResolved, metadata);
        var changedPlan = WorkspaceImageBuildPlanner.Create(changedResolved, metadata);

        Assert.Equal(basePlan.InputHash, samePlan.InputHash);
        Assert.NotEqual(basePlan.InputHash, changedPlan.InputHash);
    }

    private static WorkspaceDefinition CreateDefinition(string name, IReadOnlyList<string>? features = null, IReadOnlyList<string>? services = null)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
            Features = features?.ToList() ?? ["core"],
            Services = services?.ToList() ?? [],
        };

    private static ResolvedWorkspace CreateResolvedWorkspace(string name, IReadOnlyList<string>? aptPackages = null, IReadOnlyList<string>? npmPackages = null)
        => new()
        {
            Definition = CreateDefinition(name),
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            KnowledgePacks = Array.Empty<KnowledgePackManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = aptPackages ?? [],
            NpmPackages = npmPackages ?? [],
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        };

    private static GeneratedWorkspaceArtifacts CreateArtifacts(string imageTag, string imageHash)
        => new()
        {
            WorkspaceYaml = string.Empty,
            ComposeYaml = string.Empty,
            EnvironmentFile = string.Empty,
            ProvisionScript = string.Empty,
            StarshipConfig = string.Empty,
            ShellInitScript = string.Empty,
            OpencodeWorkspaceShellScript = string.Empty,
            ScreenConfig = string.Empty,
            AttachWrapperScript = string.Empty,
            TerminalDiagnosticsScript = string.Empty,
            WorkspaceImageTag = imageTag,
            WorkspaceImageInputHash = imageHash,
            WorkspaceDefinitionHash = string.Empty,
            DesiredStateHash = string.Empty,
            AdditionalFiles = new Dictionary<string, string>(),
            AdditionalBinaryFiles = new Dictionary<string, byte[]>(),
        };

    private sealed class RecordingContainerRuntime : IContainerRuntime
    {
        private readonly Dictionary<string, string> _images = new(StringComparer.OrdinalIgnoreCase);
        private string _lastRequestedImageTag = string.Empty;

        public int BuildCallCount { get; private set; }
        public string DefaultBuiltImageHash { get; set; } = "ABC123";

        public string RuntimeId => "docker";

        public void SetExistingImage(string imageTag, string imageHash)
            => _images[imageTag] = imageHash;

        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => DockerService.GetWorkspaceContainerName(definition);

        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => [];

        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RestartServiceAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RepairOracleOrdsGatewayAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> ProbeHttpGetFromWorkspaceAsync(WorkspaceDefinition definition, string url, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            var list = arguments.ToList();
            if (list.Count >= 4 && list[0] == "image" && list[1] == "inspect")
            {
                var imageTag = list[2];
                _lastRequestedImageTag = imageTag;
                return Task.FromResult(_images.TryGetValue(imageTag, out var hash)
                    ? Success("docker image inspect", hash)
                    : Failure("docker image inspect", "No such image"));
            }

            if (list.Count >= 2 && list[^2] == "build" && list[^1] == "workspace")
            {
                BuildCallCount++;
                if (!string.IsNullOrWhiteSpace(_lastRequestedImageTag))
                {
                    _images[_lastRequestedImageTag] = DefaultBuiltImageHash;
                }

                return Task.FromResult(Success("docker compose build"));
            }

            return Task.FromResult(Success("docker command"));
        }

        private static ProcessResult Success(string command, string standardOutput = "")
            => new() { Command = command, ExitCode = 0, StandardOutput = standardOutput, StandardError = string.Empty, StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? [] : standardOutput.Split('\n'), StandardErrorLines = [], Duration = TimeSpan.Zero };

        private static ProcessResult Failure(string command, string standardError)
            => new() { Command = command, ExitCode = 1, StandardOutput = string.Empty, StandardError = standardError, StandardOutputLines = [], StandardErrorLines = [standardError], Duration = TimeSpan.Zero };
    }
}
