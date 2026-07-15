using System.Text;
using System.Text.Json;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Smoke;

public interface IWorkspaceSmokeWorkspaceService
{
    IReadOnlyList<WorkspaceRecord> LoadWorkspaceRecords();
    WorkspaceSnapshot CreateWorkspace(string rootPath, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null);
    Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default);
    void DeleteWorkspaceRegistration(string rootPath);
}

public interface IWorkspaceSmokeWorkspaceServiceFactory
{
    IWorkspaceSmokeWorkspaceService Create();
}

public sealed class DefaultWorkspaceSmokeWorkspaceServiceFactory : IWorkspaceSmokeWorkspaceServiceFactory
{
    private readonly string _catalogRootPath;
    private readonly string _stateRootPath;
    private readonly IContainerRuntime _containerRuntime;

    public DefaultWorkspaceSmokeWorkspaceServiceFactory(string catalogRootPath, string stateRootPath, IContainerRuntime containerRuntime)
    {
        _catalogRootPath = catalogRootPath;
        _stateRootPath = stateRootPath;
        _containerRuntime = containerRuntime;
    }

    public IWorkspaceSmokeWorkspaceService Create()
    {
        Directory.CreateDirectory(_stateRootPath);
        var provider = new BuiltInCatalogProvider(_catalogRootPath);
        var ignorePolicy = new WorkspaceIgnorePolicyService();
        var orchestrator = new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            new WorkspaceRepository(_stateRootPath),
            new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks()),
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            new WorkspaceTimelineService(),
            new WorkspaceSafetyService(),
            ignorePolicy,
            new WorkspaceRuntimeStateService(),
            new GitWorkspaceProvider(new ProcessRunner(), ignorePolicy),
            _containerRuntime,
            new PlatformDetector(new ProcessRunner()),
            new RuntimeResolver(),
            new WorkspaceSmokeNoOpTerminalLauncher());
        return new WorkspaceOrchestratorSmokeWorkspaceService(orchestrator);
    }

    private sealed class WorkspaceSmokeNoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public sealed class WorkspaceOrchestratorSmokeWorkspaceService : IWorkspaceSmokeWorkspaceService
{
    private readonly WorkspaceOrchestrator _orchestrator;

    public WorkspaceOrchestratorSmokeWorkspaceService(WorkspaceOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public IReadOnlyList<WorkspaceRecord> LoadWorkspaceRecords() => _orchestrator.LoadWorkspaceRecords();

    public WorkspaceSnapshot CreateWorkspace(string rootPath, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null)
        => _orchestrator.CreateWorkspace(rootPath, definition, log);

    public Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _orchestrator.ProvisionAsync(snapshot, log, cancellationToken);

    public void DeleteWorkspaceRegistration(string rootPath) => _orchestrator.DeleteWorkspaceRegistration(rootPath);
}

public sealed class WorkspaceSmokeContext
{
    public required string MatrixRunId { get; init; }
    public required string RunId { get; init; }
    public required WorkspaceSmokeDefinition SmokeDefinition { get; init; }
    public required WorkspaceDefinition WorkspaceDefinition { get; init; }
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required IWorkspaceSmokeWorkspaceService WorkspaceService { get; init; }
    public required IContainerRuntime ContainerRuntime { get; init; }
    public required RuntimeOwnershipService RuntimeOwnershipService { get; init; }
    public required string ArtifactDirectory { get; init; }
    public required string ValidationArtifactDirectory { get; init; }

    public Task<ProcessResult> RunWorkspaceCommandAsync(string command, CancellationToken cancellationToken = default)
        => ContainerRuntime.RunSimpleDockerCommandAsync(["exec", ContainerRuntime.GetWorkspaceContainerName(WorkspaceDefinition), "bash", "-lc", command], cancellationToken: cancellationToken);

    public Task<ProcessResult> RunServiceCommandAsync(string serviceName, IReadOnlyList<string> commandArguments, CancellationToken cancellationToken = default)
        => ContainerRuntime.RunCommandInServiceContainerAsync(WorkspaceDefinition, serviceName, commandArguments, cancellationToken: cancellationToken);
}

public static class WorkspaceSmokeFailureClassifier
{
    public static WorkspaceSmokeFailureClassification Classify(Exception exception)
    {
        var message = exception.ToString();
        if (message.Contains("Cannot allocate memory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unable to create native thread", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot fork", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no space left on device", StringComparison.OrdinalIgnoreCase)
            || message.Contains("resource exhaustion", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.RuntimeResourceExhaustion;
        }

        if (message.Contains("Validation tooling failure", StringComparison.OrdinalIgnoreCase)
            || exception is ArgumentException)
        {
            return WorkspaceSmokeFailureClassification.ValidationToolingFailure;
        }

        if (message.Contains("create workspace", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.WorkspaceCreationFailure;
        }

        if (message.Contains("compose", StringComparison.OrdinalIgnoreCase)
            && message.Contains("valid", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.ComposeValidationFailure;
        }

        if (message.Contains("Workspace image build failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed to solve", StringComparison.OrdinalIgnoreCase)
            || message.Contains("did not complete successfully", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.RuntimeStartupFailure;
        }

        if (message.Contains("Oracle APEX prerequisite validation failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Oracle XML Database", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.ApexPrerequisiteFailure;
        }

        if (message.Contains("oracle", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ords", StringComparison.OrdinalIgnoreCase)
            || message.Contains("apex", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sqlcl", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.OracleRuntimeFailure;
        }

        if (message.Contains("docker", StringComparison.OrdinalIgnoreCase)
            || message.Contains("daemon", StringComparison.OrdinalIgnoreCase)
            || message.Contains("network", StringComparison.OrdinalIgnoreCase)
            || message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSmokeFailureClassification.EnvironmentFailure;
        }

        return WorkspaceSmokeFailureClassification.ProductFailure;
    }
}

public static class WorkspaceSmokeArtifacts
{
    public static void CaptureGeneratedArtifacts(WorkspacePaths paths, string artifactsRoot)
    {
        CopyIfExists(paths.ComposePath, Path.Combine(artifactsRoot, "compose.yaml"));
        CopyIfExists(paths.WorkspaceYamlPath, Path.Combine(artifactsRoot, "workspace.yaml"));

        if (!File.Exists(paths.EnvironmentFilePath))
        {
            return;
        }

        var envContent = File.ReadAllLines(paths.EnvironmentFilePath)
            .Select(line => line.StartsWith("ORACLE_PASSWORD=", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ORACLE_DEMO_PASSWORD=", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ORACLE_ORDS_PUBLIC_PASSWORD=", StringComparison.OrdinalIgnoreCase)
                ? RedactValue(line)
                : line);
        File.WriteAllLines(Path.Combine(artifactsRoot, "env.redacted"), envContent);
    }

    public static async Task WriteRuntimeInventoryArtifactsAsync(string artifactsRoot, string suffix, RuntimeResourceInventory inventory, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, $"runtime-inventory-{suffix}.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, $"runtime-inventory-{suffix}.txt"), FormatRuntimeInventorySummary(inventory), cancellationToken);
    }

    public static string FormatRuntimeInventorySummary(RuntimeResourceInventory inventory)
    {
        var counts = WorkspaceSmokeResourceCounts.FromInventory(inventory);
        var lines = new List<string>
        {
            "Runtime Inventory",
            "-----------------",
            $"Containers: {counts.Containers}",
            $"Networks: {counts.Networks}",
            $"Volumes: {counts.Volumes}",
            $"Projects: {counts.Projects}",
            string.Empty,
            "Owned resources:",
        };

        if (inventory.Projects.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            foreach (var project in inventory.Projects)
            {
                lines.Add($"- {project.OwnerKind} run {project.RunId}");
                lines.Add($"  project {project.Project}");
                lines.Add($"  containers: {project.Resources.Count(item => item.Type == RuntimeResourceType.Container)}");
                lines.Add($"  volumes: {project.Resources.Count(item => item.Type == RuntimeResourceType.Volume)}");
                lines.Add($"  networks: {project.Resources.Count(item => item.Type == RuntimeResourceType.Network)}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteResultSummary(string artifactDirectory, WorkspaceSmokeResult result)
    {
        File.WriteAllText(Path.Combine(artifactDirectory, "summary.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        var lines = new List<string>
        {
            $"template={result.TemplateId}",
            $"run_id={result.RunId}",
            $"workspace_path={result.WorkspacePath}",
            $"compose_project={result.ComposeProject}",
            $"status={result.Status}",
            $"phase={result.Phase}",
            $"failure_classification={result.FailureClassification}",
            $"failure_message={result.FailureMessage}",
            $"cleanup_verification_succeeded={result.CleanupVerificationSucceeded}",
            $"artifacts={result.ArtifactDirectory}",
        };

        foreach (var validator in result.Validators)
        {
            lines.Add($"validator.{validator.ValidatorId}.succeeded={validator.Succeeded}");
            lines.Add($"validator.{validator.ValidatorId}.message={validator.Message}");
            foreach (var pair in validator.Data.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"validator.{validator.ValidatorId}.{pair.Key}={pair.Value}");
            }
        }
        File.WriteAllLines(Path.Combine(artifactDirectory, "summary.txt"), lines);
    }

    public static void WriteMatrixSummary(string artifactDirectory, WorkspaceSmokeMatrixResult result)
    {
        File.WriteAllText(Path.Combine(artifactDirectory, "matrix-summary.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        var lines = new[]
        {
            $"matrix_run_id={result.MatrixRunId}",
            $"status={result.Status}",
            $"passed={result.PassedCount}",
            $"failed={result.FailedCount}",
            $"skipped={result.SkippedCount}",
            $"selected_templates={string.Join(',', result.SelectedTemplates)}",
        };
        File.WriteAllLines(Path.Combine(artifactDirectory, "matrix-summary.txt"), lines);
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static string RedactValue(string line)
    {
        var separatorIndex = line.IndexOf('=');
        return separatorIndex >= 0 ? line[..(separatorIndex + 1)] + "<redacted>" : line;
    }
}

public static class WorkspaceSmokeOwnershipLabelWriter
{
    public static void Apply(string composePath, WorkspaceDefinition definition, string templateId, string runId, string workspaceRoot)
    {
        var project = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var labels = new[]
        {
            $"{RuntimeOwnershipLabels.Owner}: \"smoke\"",
            $"{RuntimeOwnershipLabels.RunId}: \"{runId}\"",
            $"{RuntimeOwnershipLabels.Template}: \"{templateId}\"",
            $"{RuntimeOwnershipLabels.CreatedBy}: \"{RuntimeOwnershipLabels.CreatedByValue}\"",
            $"{RuntimeOwnershipLabels.Project}: \"{project}\"",
            $"{RuntimeOwnershipLabels.WorkspaceRoot}: \"{workspaceRoot.Replace("\\", "/", StringComparison.Ordinal)}\"",
            $"{RuntimeOwnershipLabels.ComposePath}: \"{composePath.Replace("\\", "/", StringComparison.Ordinal)}\"",
            $"{RuntimeOwnershipLabels.CreatedAt}: \"{DateTimeOffset.UtcNow:O}\"",
        };

        var lines = File.ReadAllLines(composePath).ToList();
        InsertLabels(lines, "services:", labels, ensureDefaultChild: false);
        InsertLabels(lines, "networks:", labels, ensureDefaultChild: true);
        InsertLabels(lines, "volumes:", labels, ensureDefaultChild: false);
        File.WriteAllLines(composePath, lines);
    }

    private static void InsertLabels(List<string> lines, string sectionHeader, IReadOnlyList<string> labels, bool ensureDefaultChild)
    {
        var sectionIndex = lines.FindIndex(line => string.Equals(line, sectionHeader, StringComparison.Ordinal));
        if (sectionIndex < 0)
        {
            if (!ensureDefaultChild)
            {
                return;
            }

            lines.Add(sectionHeader);
            lines.Add("  default:");
            sectionIndex = lines.Count - 2;
        }

        if (ensureDefaultChild && !lines.Skip(sectionIndex + 1).TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal)).Any(line => string.Equals(line, "  default:", StringComparison.Ordinal)))
        {
            lines.Insert(sectionIndex + 1, "  default:");
        }

        for (var index = sectionIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.EndsWith(":", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 < lines.Count && lines[index + 1].TrimStart().StartsWith("labels:", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Insert(index + 1, "    labels:");
            for (var labelIndex = 0; labelIndex < labels.Count; labelIndex++)
            {
                lines.Insert(index + 2 + labelIndex, "      " + labels[labelIndex]);
            }

            index += labels.Count + 1;
        }
    }
}

public sealed class WorkspaceSmokeLockService
{
    public IDisposable AcquireOracleExclusiveLock()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "opencode-oracle-smoke.lock");
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Runtime resource exhaustion: another Oracle smoke run already owns the host-wide smoke lock.", exception);
        }
    }
}
