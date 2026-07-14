using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class RuntimeOwnershipService
{
    private static readonly string[] RequiredLabels =
    [
        RuntimeOwnershipLabels.Owner,
        RuntimeOwnershipLabels.RunId,
        RuntimeOwnershipLabels.Template,
        RuntimeOwnershipLabels.CreatedBy,
        RuntimeOwnershipLabels.Project,
        RuntimeOwnershipLabels.WorkspaceRoot,
        RuntimeOwnershipLabels.ComposePath,
        RuntimeOwnershipLabels.CreatedAt,
    ];

    private readonly IContainerRuntime _containerRuntime;

    public RuntimeOwnershipService(IContainerRuntime containerRuntime)
    {
        _containerRuntime = containerRuntime;
    }

    public async Task<IReadOnlyList<RuntimeOwnedResource>> DiscoverOwnedResourcesAsync(RuntimeOwnershipQuery? query = null, CancellationToken cancellationToken = default)
    {
        var all = new List<RuntimeOwnedResource>();
        all.AddRange(await DiscoverResourcesAsync(RuntimeResourceType.Container, ["ps", "-a", "--no-trunc", "--format", "{{.ID}}"], cancellationToken));
        all.AddRange(await DiscoverResourcesAsync(RuntimeResourceType.Network, ["network", "ls", "--no-trunc", "-q"], cancellationToken));
        all.AddRange(await DiscoverResourcesAsync(RuntimeResourceType.Volume, ["volume", "ls", "-q"], cancellationToken));
        return ApplyQuery(all, query).ToArray();
    }

    public async Task<IReadOnlyList<RuntimeProjectInventory>> DiscoverProjectsAsync(RuntimeOwnershipQuery? query = null, CancellationToken cancellationToken = default)
        => (await BuildInventoryAsync(query, cancellationToken)).Projects;

    public async Task<RuntimeResourceInventory> BuildInventoryAsync(RuntimeOwnershipQuery? query = null, CancellationToken cancellationToken = default)
    {
        var resources = (await DiscoverOwnedResourcesAsync(query, cancellationToken)).ToArray();
        var projects = resources.GroupBy(item => item.Project, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RuntimeProjectInventory
            {
                Project = group.Key,
                OwnerKind = group.Select(item => item.OwnerKind).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                RunId = group.Select(item => item.RunId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                Template = group.Select(item => item.Template).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                WorkspaceRoot = group.Select(item => item.WorkspaceRoot).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                ComposePath = group.Select(item => item.ComposePath).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                CreatedAt = group.Select(item => item.CreatedAt).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                Resources = group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            })
            .OrderBy(item => item.Project, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RuntimeResourceInventory
        {
            Resources = resources,
            Projects = projects,
            Orphans = DetectOrphans(resources),
            StaleRuntimes = DetectStaleRuntimes(resources),
            DuplicateRunIds = DetectDuplicateRunIds(resources),
            MissingRequiredLabels = DetectMissingRequiredLabels(resources),
            MissingComposeFiles = DetectMissingComposeFiles(resources),
            MissingWorkspaceDirectories = DetectMissingWorkspaceDirectories(resources),
        };
    }

    public async Task<RuntimeCleanupResult> CleanupAsync(RuntimeCleanupOptions options, CancellationToken cancellationToken = default)
    {
        var filter = new RuntimeOwnershipQuery
        {
            OwnerKind = options.OwnerKind,
            Project = options.Project,
            RunId = options.RunId,
            WorkspaceRoot = options.WorkspaceRoot,
            ComposePath = options.ComposePath,
        };
        var resources = (await DiscoverOwnedResourcesAsync(filter, cancellationToken)).ToArray();
        var actions = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var composeDownAttempted = false;
        var composeDownSucceeded = true;
        var fallbackRemovalRequired = false;

        foreach (var project in resources.Where(item => !string.IsNullOrWhiteSpace(item.Project)).GroupBy(item => item.Project, StringComparer.OrdinalIgnoreCase))
        {
            var composePath = project.Select(item => item.ComposePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (string.IsNullOrWhiteSpace(composePath) || !File.Exists(composePath))
            {
                continue;
            }

            var templateId = project.Select(item => item.Template).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var requiredOracleService = IsOracleTemplate(templateId) ? "oracle-demo" : null;
            var inspection = ComposeProjectInspector.InspectFile(composePath, requiredOracleService);
            if (!inspection.IsValid)
            {
                foreach (var inspectionError in inspection.Errors)
                {
                    warnings.Add($"compose-validation:{project.Key}:{inspectionError}");
                }

                composeDownSucceeded = false;
                fallbackRemovalRequired = true;
                continue;
            }

            actions.Add($"compose-down:{project.Key}");
            composeDownAttempted = true;
            if (options.DryRun)
            {
                continue;
            }

            var arguments = new List<string> { "compose", "--project-name", project.Key, "--file", composePath };
            foreach (var profile in inspection.Profiles)
            {
                arguments.Add("--profile");
                arguments.Add(profile);
            }

            arguments.AddRange(["down", "-v", "--remove-orphans"]);
            var result = await _containerRuntime.RunSimpleDockerCommandAsync(arguments, cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                warnings.Add($"compose-down:{project.Key}:{result.StandardError}");
                composeDownSucceeded = false;
                fallbackRemovalRequired = true;
            }
        }

        var resourcesToRemove = options.DryRun
            ? resources
            : (await DiscoverOwnedResourcesAsync(filter, cancellationToken)).ToArray();
        if (resourcesToRemove.Length > 0)
        {
            fallbackRemovalRequired = true;
        }

        foreach (var resource in resourcesToRemove)
        {
            actions.Add($"remove:{resource.Type}:{resource.Name}");
            if (options.DryRun)
            {
                continue;
            }

            var result = await _containerRuntime.RunSimpleDockerCommandAsync(BuildRemoveArguments(resource), cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                errors.Add($"remove:{resource.Type}:{resource.Name}:{result.StandardError}");
            }
        }

        var verificationSucceeded = true;
        if (!options.DryRun)
        {
            var verificationErrors = await VerifyCleanupAsync(filter, cancellationToken);
            verificationSucceeded = verificationErrors.Count == 0;
            errors.AddRange(verificationErrors);
        }

        return new RuntimeCleanupResult
        {
            Succeeded = errors.Count == 0,
            DryRun = options.DryRun,
            ComposeDownAttempted = composeDownAttempted,
            ComposeDownSucceeded = composeDownSucceeded,
            FallbackRemovalRequired = fallbackRemovalRequired,
            VerificationSucceeded = options.DryRun || verificationSucceeded,
            Filter = filter,
            Resources = resources,
            Actions = actions,
            Warnings = warnings,
            Errors = errors,
        };
    }

    private static bool IsOracleTemplate(string? templateId)
        => !string.IsNullOrWhiteSpace(templateId)
           && templateId.StartsWith("oracle-", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<string>> VerifyCleanupAsync(RuntimeOwnershipQuery? query = null, CancellationToken cancellationToken = default)
    {
        var remaining = await DiscoverOwnedResourcesAsync(query, cancellationToken);
        return remaining.Count == 0
            ? Array.Empty<string>()
            : [$"cleanup-incomplete:{remaining.Count}"];
    }

    public IReadOnlyList<RuntimeInventoryIssue> DetectOrphans(RuntimeResourceInventory inventory)
        => inventory.Orphans;

    public IReadOnlyList<RuntimeInventoryIssue> DetectOrphans(IReadOnlyList<RuntimeOwnedResource> resources)
        => resources.Where(item => item.IsOrphaned)
            .Select(item => new RuntimeInventoryIssue { Kind = "orphan", Message = $"{item.Name} is orphaned.", Project = item.Project, RunId = item.RunId, ResourceName = item.Name })
            .ToArray();

    public async Task<RuntimeResourcePreflight> CapturePreflightAsync(CancellationToken cancellationToken = default)
    {
        var activeResources = await DiscoverOwnedResourcesAsync(null, cancellationToken);
        var memory = await RunHostDiagnosticAsync(OperatingSystem.IsWindows()
            ? ["powershell.exe", "-NoProfile", "-Command", "Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize,FreePhysicalMemory | Format-List"]
            : ["bash", "-lc", "free -m"], cancellationToken);
        var dockerMemory = await _containerRuntime.RunSimpleDockerCommandAsync(["info", "--format", "{{json .MemTotal}}"], cancellationToken: cancellationToken);
        var dockerDisk = await _containerRuntime.RunSimpleDockerCommandAsync(["system", "df"], cancellationToken: cancellationToken);
        var dockerStats = await _containerRuntime.RunSimpleDockerCommandAsync(["stats", "--no-stream", "--format", "table {{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}"], cancellationToken: cancellationToken);

        return new RuntimeResourcePreflight
        {
            HostMemorySummary = memory,
            DockerMemorySummary = dockerMemory.IsSuccess ? dockerMemory.StandardOutput : dockerMemory.StandardError,
            DockerDiskUsageSummary = dockerDisk.IsSuccess ? dockerDisk.StandardOutput : dockerDisk.StandardError,
            DockerStatsSummary = dockerStats.IsSuccess ? dockerStats.StandardOutput : dockerStats.StandardError,
            ActiveOwnedResources = activeResources,
        };
    }

    private async Task<IReadOnlyList<RuntimeOwnedResource>> DiscoverResourcesAsync(RuntimeResourceType type, IReadOnlyList<string> listArguments, CancellationToken cancellationToken)
    {
        var listResult = await _containerRuntime.RunSimpleDockerCommandAsync(listArguments, cancellationToken: cancellationToken);
        if (!listResult.IsSuccess)
        {
            return Array.Empty<RuntimeOwnedResource>();
        }

        var ids = listResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<RuntimeOwnedResource>();
        }

        var inspectResult = await _containerRuntime.RunSimpleDockerCommandAsync(BuildInspectArguments(type, ids), cancellationToken: cancellationToken);
        if (!inspectResult.IsSuccess)
        {
            return Array.Empty<RuntimeOwnedResource>();
        }

        using var document = JsonDocument.Parse(inspectResult.StandardOutput);
        var resources = new List<RuntimeOwnedResource>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var labels = GetLabels(element);
            if (labels.Count == 0 || !labels.TryGetValue(RuntimeOwnershipLabels.Owner, out var ownerKind) || string.IsNullOrWhiteSpace(ownerKind))
            {
                continue;
            }

            var name = element.TryGetProperty("Name", out var nameElement)
                ? (nameElement.GetString() ?? string.Empty).TrimStart('/')
                : element.TryGetProperty("Labels", out var labelNames) && labelNames.TryGetProperty("Name", out var directName)
                    ? directName.GetString() ?? string.Empty
                    : string.Empty;
            var resource = new RuntimeOwnedResource
            {
                ResourceId = element.TryGetProperty("Id", out var idElement) ? idElement.GetString() ?? string.Empty : name,
                Name = string.IsNullOrWhiteSpace(name) ? element.TryGetProperty("Name", out var altName) ? altName.GetString() ?? string.Empty : string.Empty : name,
                Type = type,
                Labels = labels,
                OwnerKind = ownerKind,
                RunId = GetLabel(labels, RuntimeOwnershipLabels.RunId),
                Project = GetLabel(labels, RuntimeOwnershipLabels.Project),
                Template = GetLabel(labels, RuntimeOwnershipLabels.Template),
                WorkspaceRoot = GetLabel(labels, RuntimeOwnershipLabels.WorkspaceRoot),
                ComposePath = GetLabel(labels, RuntimeOwnershipLabels.ComposePath),
                CreatedAt = GetLabel(labels, RuntimeOwnershipLabels.CreatedAt),
                Status = ExtractStatus(type, element),
                IsOrphaned = IsOrphaned(labels),
                IsStale = IsStale(GetLabel(labels, RuntimeOwnershipLabels.CreatedAt)),
                MissingLabels = RequiredLabels.Where(label => !labels.ContainsKey(label) || string.IsNullOrWhiteSpace(labels[label])).ToArray(),
            };
            resources.Add(resource);
        }

        return resources;
    }

    private static IReadOnlyList<RuntimeOwnedResource> ApplyQuery(IEnumerable<RuntimeOwnedResource> resources, RuntimeOwnershipQuery? query)
    {
        if (query is null)
        {
            return resources.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return resources.Where(item =>
                (string.IsNullOrWhiteSpace(query.OwnerKind) || string.Equals(item.OwnerKind, query.OwnerKind, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(query.RunId) || string.Equals(item.RunId, query.RunId, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(query.Project) || string.Equals(item.Project, query.Project, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(query.WorkspaceRoot) || FileSystemPathComparer.AreEquivalent(item.WorkspaceRoot, query.WorkspaceRoot))
                && (string.IsNullOrWhiteSpace(query.ComposePath) || FileSystemPathComparer.AreEquivalent(item.ComposePath, query.ComposePath)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildInspectArguments(RuntimeResourceType type, IReadOnlyList<string> ids)
        => type switch
        {
            RuntimeResourceType.Container => ["inspect", .. ids],
            RuntimeResourceType.Network => ["network", "inspect", .. ids],
            RuntimeResourceType.Volume => ["volume", "inspect", .. ids],
            _ => ["inspect", .. ids],
        };

    private static IReadOnlyList<string> BuildRemoveArguments(RuntimeOwnedResource resource)
        => resource.Type switch
        {
            RuntimeResourceType.Container => ["rm", "-f", resource.Name],
            RuntimeResourceType.Network => ["network", "rm", resource.Name],
            RuntimeResourceType.Volume => ["volume", "rm", resource.Name],
            _ => Array.Empty<string>(),
        };

    private static Dictionary<string, string> GetLabels(JsonElement element)
    {
        JsonElement labels = default;
        if (element.TryGetProperty("Config", out var config) && config.TryGetProperty("Labels", out var configLabels))
        {
            labels = configLabels;
        }
        else if (element.TryGetProperty("Labels", out var directLabels))
        {
            labels = directLabels;
        }

        if (labels.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return labels.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static string GetLabel(IReadOnlyDictionary<string, string> labels, string name)
        => labels.TryGetValue(name, out var value) ? value : string.Empty;

    private static string ExtractStatus(RuntimeResourceType type, JsonElement element)
        => type switch
        {
            RuntimeResourceType.Container when element.TryGetProperty("State", out var state) && state.TryGetProperty("Status", out var status) => status.GetString() ?? string.Empty,
            RuntimeResourceType.Network => "created",
            RuntimeResourceType.Volume => "created",
            _ => string.Empty,
        };

    private static bool IsOrphaned(IReadOnlyDictionary<string, string> labels)
    {
        var composePath = GetLabel(labels, RuntimeOwnershipLabels.ComposePath);
        var workspaceRoot = GetLabel(labels, RuntimeOwnershipLabels.WorkspaceRoot);
        return !string.IsNullOrWhiteSpace(composePath) && !File.Exists(composePath)
            || !string.IsNullOrWhiteSpace(workspaceRoot) && !Directory.Exists(workspaceRoot);
    }

    private static bool IsStale(string createdAt)
        => DateTimeOffset.TryParse(createdAt, out var timestamp) && timestamp < DateTimeOffset.UtcNow.AddDays(-1);

    private static IReadOnlyList<RuntimeInventoryIssue> DetectStaleRuntimes(IReadOnlyList<RuntimeOwnedResource> resources)
        => resources.Where(item => item.IsStale)
            .Select(item => new RuntimeInventoryIssue { Kind = "stale-runtime", Message = $"{item.Name} is older than 24 hours.", Project = item.Project, RunId = item.RunId, ResourceName = item.Name })
            .ToArray();

    private static IReadOnlyList<RuntimeInventoryIssue> DetectDuplicateRunIds(IReadOnlyList<RuntimeOwnedResource> resources)
        => resources.Where(item => !string.IsNullOrWhiteSpace(item.RunId))
            .GroupBy(item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Project).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => new RuntimeInventoryIssue { Kind = "duplicate-run-id", Message = $"Run id {group.Key} is used by multiple projects.", RunId = group.Key })
            .ToArray();

    private static IReadOnlyList<RuntimeInventoryIssue> DetectMissingRequiredLabels(IReadOnlyList<RuntimeOwnedResource> resources)
        => resources.Where(item => item.MissingLabels.Count > 0)
            .Select(item => new RuntimeInventoryIssue { Kind = "missing-labels", Message = $"{item.Name} is missing labels: {string.Join(", ", item.MissingLabels)}.", Project = item.Project, RunId = item.RunId, ResourceName = item.Name })
            .ToArray();

    private static IReadOnlyList<RuntimeInventoryIssue> DetectMissingComposeFiles(IReadOnlyList<RuntimeOwnedResource> resources)
        => resources.Where(item => !string.IsNullOrWhiteSpace(item.ComposePath) && !File.Exists(item.ComposePath))
            .Select(item => new RuntimeInventoryIssue { Kind = "missing-compose", Message = $"{item.Name} references missing compose file {item.ComposePath}.", Project = item.Project, RunId = item.RunId, ResourceName = item.Name })
            .ToArray();

    private static IReadOnlyList<RuntimeInventoryIssue> DetectMissingWorkspaceDirectories(IReadOnlyList<RuntimeOwnedResource> resources)
        => resources.Where(item => !string.IsNullOrWhiteSpace(item.WorkspaceRoot) && !Directory.Exists(item.WorkspaceRoot))
            .Select(item => new RuntimeInventoryIssue { Kind = "missing-workspace", Message = $"{item.Name} references missing workspace directory {item.WorkspaceRoot}.", Project = item.Project, RunId = item.RunId, ResourceName = item.Name })
            .ToArray();

    private static async Task<string> RunHostDiagnosticAsync(IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var processRunner = new ProcessRunner();
        var result = await processRunner.RunAsync(command[0], command.Skip(1).ToArray(), cancellationToken: cancellationToken);
        return result.IsSuccess ? result.StandardOutput : result.StandardError;
    }
}

public sealed class SmokeRuntimeOwnershipService
{
    private readonly RuntimeOwnershipService _inner;
    private readonly IContainerRuntime _containerRuntime;
    private static readonly string[] LegacySmokeProjectPrefixes = ["oracle-plsql-demo-runtime-smoke-", "oracle-apex-demo-runtime-smoke-", "oracle-apexlang-demo-runtime-smoke-", "demo-apexlang", "demo-apex", "demo-plsql"];

    public SmokeRuntimeOwnershipService(IContainerRuntime containerRuntime)
    {
        _containerRuntime = containerRuntime;
        _inner = new RuntimeOwnershipService(containerRuntime);
    }

    public Task<IReadOnlyList<RuntimeOwnedResource>> DiscoverOwnedResourcesAsync(string? runId = null, CancellationToken cancellationToken = default)
        => _inner.DiscoverOwnedResourcesAsync(new RuntimeOwnershipQuery { OwnerKind = SmokeRuntimeOwnershipLabels.OwnerValue, RunId = runId }, cancellationToken);

    public async Task<SmokeCleanupResult> CleanupAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default)
    {
        var result = await _inner.CleanupAsync(new RuntimeCleanupOptions
        {
            DryRun = options.DryRun,
            OutputFormat = options.OutputFormat,
            OwnerKind = SmokeRuntimeOwnershipLabels.OwnerValue,
            RunId = options.IncludeAll ? null : options.RunId,
        }, cancellationToken);

        return new SmokeCleanupResult
        {
            Succeeded = result.Succeeded,
            DryRun = result.DryRun,
            ComposeDownAttempted = result.ComposeDownAttempted,
            ComposeDownSucceeded = result.ComposeDownSucceeded,
            FallbackRemovalRequired = result.FallbackRemovalRequired,
            VerificationSucceeded = result.VerificationSucceeded,
            Resources = result.Resources,
            Actions = result.Actions,
            Warnings = result.Warnings,
            Errors = result.Errors,
            SuspectedLegacyProjects = await DiscoverLegacyProjectsAsync(cancellationToken),
        };
    }

    public async Task<SmokeResourcePreflight> CapturePreflightAsync(CancellationToken cancellationToken = default)
    {
        var result = await _inner.CapturePreflightAsync(cancellationToken);
        return new SmokeResourcePreflight
        {
            HostMemorySummary = result.HostMemorySummary,
            DockerMemorySummary = result.DockerMemorySummary,
            DockerDiskUsageSummary = result.DockerDiskUsageSummary,
            DockerStatsSummary = result.DockerStatsSummary,
            ActiveSmokeResources = result.ActiveOwnedResources,
        };
    }

    public async Task<IReadOnlyList<LegacyRuntimeProject>> DiscoverLegacyProjectsAsync(CancellationToken cancellationToken = default)
    {
        var containers = await ListComposeResourcesAsync(["ps", "-a", "--format", "{{.Names}}|{{.Label \"com.docker.compose.project\"}}|{{.Label \"io.opencode.workspace.owner\"}}|{{.Label \"com.docker.compose.service\"}}"], cancellationToken);
        var networks = await ListComposeResourcesAsync(["network", "ls", "--format", "{{.Name}}|{{.Label \"com.docker.compose.project\"}}|{{.Label \"io.opencode.workspace.owner\"}}"], cancellationToken);
        var volumes = await ListComposeResourcesAsync(["volume", "ls", "--format", "{{.Name}}|{{.Label \"com.docker.compose.project\"}}|{{.Label \"io.opencode.workspace.owner\"}}"], cancellationToken);

        return containers.Where(item => string.IsNullOrWhiteSpace(item.Owner) && IsLegacyProjectName(item.Project))
            .GroupBy(item => item.Project, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var project = group.Key;
                var containerNames = group.Select(item => item.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
                var services = group.Select(item => item.Service).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var projectNetworks = networks.Where(item => string.Equals(item.Project, project, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.Owner)).Select(item => item.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
                var projectVolumes = volumes.Where(item => string.Equals(item.Project, project, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.Owner)).Select(item => item.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
                var eligible = services.Contains("oracle-demo", StringComparer.OrdinalIgnoreCase)
                    && services.Contains("oracle-ords", StringComparer.OrdinalIgnoreCase)
                    && (services.Contains("workspace", StringComparer.OrdinalIgnoreCase) || containerNames.Any(name => name.EndsWith("-workspace", StringComparison.OrdinalIgnoreCase)));

                return new LegacyRuntimeProject
                {
                    Project = project,
                    ContainerNames = containerNames,
                    NetworkNames = projectNetworks,
                    VolumeNames = projectVolumes,
                    EligibleForCleanup = eligible,
                    Reason = eligible
                        ? "Compose project matches the legacy Oracle smoke container/service pattern and has no ownership labels."
                        : "Compose project looks like a legacy Oracle smoke runtime but did not meet the strict cleanup safety criteria.",
                };
            })
            .OrderBy(item => item.Project, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<LegacyCleanupResult> CleanupLegacyAsync(LegacyCleanupOptions options, CancellationToken cancellationToken = default)
    {
        var projects = (await DiscoverLegacyProjectsAsync(cancellationToken)).Where(item => item.EligibleForCleanup).ToArray();
        var actions = new List<string>();
        var errors = new List<string>();

        foreach (var project in projects)
        {
            actions.Add($"remove-legacy-project:{project.Project}");
            if (options.DryRun)
            {
                continue;
            }

            foreach (var container in project.ContainerNames)
            {
                var result = await _innerCleanup(["rm", "-f", container], cancellationToken);
                if (!result.IsSuccess)
                {
                    errors.Add($"legacy-container:{container}:{result.StandardError}");
                }
            }

            foreach (var network in project.NetworkNames)
            {
                var result = await _innerCleanup(["network", "rm", network], cancellationToken);
                if (!result.IsSuccess)
                {
                    errors.Add($"legacy-network:{network}:{result.StandardError}");
                }
            }

            foreach (var volume in project.VolumeNames)
            {
                var result = await _innerCleanup(["volume", "rm", volume], cancellationToken);
                if (!result.IsSuccess)
                {
                    errors.Add($"legacy-volume:{volume}:{result.StandardError}");
                }
            }
        }

        return new LegacyCleanupResult
        {
            Succeeded = errors.Count == 0,
            DryRun = options.DryRun,
            Projects = projects,
            Actions = actions,
            Errors = errors,
        };
    }

    private async Task<IReadOnlyList<(string Name, string Project, string Owner, string Service)>> ListComposeResourcesAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var result = await _innerCleanup(args, cancellationToken);
        if (!result.IsSuccess)
        {
            return Array.Empty<(string Name, string Project, string Owner, string Service)>();
        }

        return result.StandardOutputLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length >= 3)
            .Select(parts => (
                Name: parts[0].Trim(),
                Project: parts[1].Trim(),
                Owner: parts[2].Trim(),
                Service: parts.Length > 3 ? parts[3].Trim() : string.Empty))
            .ToArray();
    }

    private static bool IsLegacyProjectName(string project)
        => !string.IsNullOrWhiteSpace(project)
            && LegacySmokeProjectPrefixes.Any(prefix => project.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private Task<ProcessResult> _innerCleanup(IReadOnlyList<string> args, CancellationToken cancellationToken)
        => _containerRuntime.RunSimpleDockerCommandAsync(args, cancellationToken: cancellationToken);
}
