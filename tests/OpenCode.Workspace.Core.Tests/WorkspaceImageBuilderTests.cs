using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Core.Catalog;

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
        var resolver = CreateResolver();
        var baseResolved = resolver.Resolve(CreateDefinition("demo"));
        var sameResolved = resolver.Resolve(CreateDefinition("demo-copy"));
        var changedResolved = resolver.Resolve(CreateDefinition("demo-docs", features: ["core", "document-processing"]));

        var basePlan = WorkspaceImageBuildPlanner.Create(baseResolved, metadata);
        var samePlan = WorkspaceImageBuildPlanner.Create(sameResolved, metadata);
        var changedPlan = WorkspaceImageBuildPlanner.Create(changedResolved, metadata);

        Assert.Equal(basePlan.InputHash, samePlan.InputHash);
        Assert.NotEqual(basePlan.InputHash, changedPlan.InputHash);
    }

    [Fact]
    public void EquivalentWorkspaces_GenerateIdenticalImageToolingAssets()
    {
        var resolver = CreateResolver();
        var generator = new WorkspaceImageToolingScriptGenerator();
        var left = resolver.Resolve(CreateDefinition("docs-a", features: ["core", "document-processing", "analytics-reporting"]));
        var right = resolver.Resolve(CreateDefinition("docs-b", features: ["analytics-reporting", "document-processing", "core"]));

        var leftLayout = generator.GenerateLayout(left);
        var rightLayout = generator.GenerateLayout(right);

        Assert.Equal(leftLayout.CombinedScript, rightLayout.CombinedScript);
        Assert.Equal(leftLayout.LayerScripts.Select(static layer => (layer.CategoryId, layer.Content)), rightLayout.LayerScripts.Select(static layer => (layer.CategoryId, layer.Content)));
    }

    [Fact]
    public void RegeneratingWithoutImageInputChanges_PreservesImageInputHash()
    {
        var metadata = GeneratedArtifactRuntimeMetadataBuilder.Create((WorkspaceRuntimeStateRecord?)null);
        var resolved = CreateResolver().Resolve(CreateDefinition("demo", features: ["core", "document-processing"]));

        var firstPlan = WorkspaceImageBuildPlanner.Create(resolved, metadata);
        var secondPlan = WorkspaceImageBuildPlanner.Create(resolved, metadata);

        Assert.Equal(firstPlan.InputHash, secondPlan.InputHash);
        Assert.Equal(OrderedCategoryHashes(firstPlan.InputCategoryHashes), OrderedCategoryHashes(secondPlan.InputCategoryHashes));
    }

    [Fact]
    public void RuntimeMetadataAndGeneratedTimestamp_DoNotChangeImageInputHash()
    {
        var resolved = CreateResolver().Resolve(CreateDefinition("demo", features: ["core", "analytics-reporting"]));
        var firstMetadata = new GeneratedArtifactRuntimeMetadata
        {
            HostOperatingSystem = "Windows",
            HostArchitecture = "x64",
            Runtime = "docker-desktop",
            TargetPlatform = "linux/amd64",
            Compatibility = "native",
            GeneratedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var secondMetadata = new GeneratedArtifactRuntimeMetadata
        {
            HostOperatingSystem = "Linux",
            HostArchitecture = "arm64",
            Runtime = "docker",
            TargetPlatform = "linux/arm64",
            Compatibility = "fallback",
            GeneratedUtc = new DateTimeOffset(2026, 7, 12, 12, 34, 56, TimeSpan.Zero),
        };

        var firstPlan = WorkspaceImageBuildPlanner.Create(resolved, firstMetadata);
        var secondPlan = WorkspaceImageBuildPlanner.Create(resolved, secondMetadata);

        Assert.Equal(firstPlan.InputHash, secondPlan.InputHash);
        Assert.Equal(OrderedCategoryHashes(firstPlan.InputCategoryHashes), OrderedCategoryHashes(secondPlan.InputCategoryHashes));
    }

    [Fact]
    public void ChangingNodeVersion_ChangesRelevantImageInputCategoryAndHash()
    {
        var metadata = GeneratedArtifactRuntimeMetadataBuilder.Create((WorkspaceRuntimeStateRecord?)null);
        var baseResolved = CreateResolver().Resolve(CreateDefinition("demo", runtimeNode: 22));
        var changedResolved = CreateResolver().Resolve(CreateDefinition("demo", runtimeNode: 24));

        var basePlan = WorkspaceImageBuildPlanner.Create(baseResolved, metadata);
        var changedPlan = WorkspaceImageBuildPlanner.Create(changedResolved, metadata);

        Assert.NotEqual(basePlan.InputHash, changedPlan.InputHash);
        Assert.Equal(basePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.BaseOsCategory], changedPlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.BaseOsCategory]);
        Assert.NotEqual(basePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.CommonToolingCategory], changedPlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.CommonToolingCategory]);
    }

    [Fact]
    public void Dockerfile_EmitsHashLabelAfterExpensiveToolingInstructions()
    {
        var metadata = GeneratedArtifactRuntimeMetadataBuilder.Create((WorkspaceRuntimeStateRecord?)null);
        var resolved = CreateResolver().Resolve(CreateDefinition("oracle-demo", features: ["core", "oracle-demo"], services: ["oracle-demo"]));
        var plan = WorkspaceImageBuildPlanner.Create(resolved, metadata);
        var dockerfile = new WorkspaceImageDockerfileGenerator().Generate(resolved, plan, metadata);
        var lines = dockerfile.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var labelIndex = Array.FindIndex(lines, line => line.StartsWith("LABEL opencode.workspace.image-input-hash=", StringComparison.Ordinal));
        var lastRunIndex = Array.FindLastIndex(lines, line => line.StartsWith("RUN bash /tmp/workspace-image-tooling.", StringComparison.Ordinal));

        Assert.True(lastRunIndex >= 0);
        Assert.True(labelIndex > lastRunIndex);
    }

    [Fact]
    public void OracleOnlyChanges_DoNotInvalidateUnrelatedBaseToolingInputs()
    {
        var metadata = GeneratedArtifactRuntimeMetadataBuilder.Create((WorkspaceRuntimeStateRecord?)null);
        var baseResolved = CreateResolver().Resolve(CreateDefinition("docs", features: ["core", "document-processing"]));
        var oracleResolved = CreateResolver().Resolve(CreateDefinition("docs-oracle", features: ["core", "document-processing", "oracle-demo"], services: ["oracle-demo"]));

        var basePlan = WorkspaceImageBuildPlanner.Create(baseResolved, metadata);
        var oraclePlan = WorkspaceImageBuildPlanner.Create(oracleResolved, metadata);

        Assert.Equal(basePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.BaseOsCategory], oraclePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.BaseOsCategory]);
        Assert.Equal(basePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.CommonToolingCategory], oraclePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.CommonToolingCategory]);
        Assert.Equal(basePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.OptionalToolingCategory], oraclePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.OptionalToolingCategory]);
        Assert.NotEqual(basePlan.InputCategoryHashes.GetValueOrDefault(WorkspaceImageToolingLayoutBuilder.OracleToolingCategory, string.Empty), oraclePlan.InputCategoryHashes[WorkspaceImageToolingLayoutBuilder.OracleToolingCategory]);
    }

    private static WorkspaceDefinition CreateDefinition(string name, IReadOnlyList<string>? features = null, IReadOnlyList<string>? services = null, int? runtimeNode = null)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
            Runtime = new WorkspaceRuntimeDefinition { Node = runtimeNode ?? WorkspaceRuntimeDefinition.DefaultNodeMajorVersion },
            Features = features?.ToList() ?? ["core"],
            Services = services?.ToList() ?? [],
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
            WorkspaceImageInputCategoryHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            WorkspaceDefinitionHash = string.Empty,
            DesiredStateHash = string.Empty,
            AdditionalFiles = new Dictionary<string, string>(),
            AdditionalBinaryFiles = new Dictionary<string, byte[]>(),
        };

    private static WorkspaceResolver CreateResolver()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        return new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
    }

    private static IReadOnlyList<KeyValuePair<string, string>> OrderedCategoryHashes(IReadOnlyDictionary<string, string> hashes)
        => hashes.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();

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
