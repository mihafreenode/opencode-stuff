using System.Text.Json;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class SmokeRuntimeOwnershipService
{
    private readonly IProcessRunner _processRunner;

    public SmokeRuntimeOwnershipService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<SmokeOwnedResource>> DiscoverOwnedResourcesAsync(string? runId = null, CancellationToken cancellationToken = default)
    {
        var resources = new List<SmokeOwnedResource>();
        resources.AddRange(await DiscoverResourcesAsync("container", ["ps", "-a", "-q", "--filter", $"label={SmokeRuntimeOwnershipLabels.Owner}={SmokeRuntimeOwnershipLabels.OwnerValue}"], runId, cancellationToken));
        resources.AddRange(await DiscoverResourcesAsync("network", ["network", "ls", "-q", "--filter", $"label={SmokeRuntimeOwnershipLabels.Owner}={SmokeRuntimeOwnershipLabels.OwnerValue}"], runId, cancellationToken));
        resources.AddRange(await DiscoverResourcesAsync("volume", ["volume", "ls", "-q", "--filter", $"label={SmokeRuntimeOwnershipLabels.Owner}={SmokeRuntimeOwnershipLabels.OwnerValue}"], runId, cancellationToken));
        return resources;
    }

    public async Task<SmokeCleanupResult> CleanupAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default)
    {
        var resources = await DiscoverOwnedResourcesAsync(options.IncludeAll ? null : options.RunId, cancellationToken);
        var actions = new List<string>();
        var errors = new List<string>();

        foreach (var project in resources.Where(item => !string.IsNullOrWhiteSpace(item.Project)).GroupBy(item => item.Project, StringComparer.OrdinalIgnoreCase))
        {
            var workspaceRoot = project.Select(item => item.WorkspaceRoot).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var composePath = project.Select(item => item.ComposePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (string.IsNullOrWhiteSpace(composePath) || !File.Exists(composePath))
            {
                continue;
            }

            actions.Add($"compose-down:{project.Key}");
            if (options.DryRun)
            {
                continue;
            }

            var result = await _processRunner.RunAsync("docker", ["compose", "--project-name", project.Key, "--file", composePath, "down", "-v", "--remove-orphans"], workspaceRoot, cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                errors.Add($"compose-down:{project.Key}:{result.StandardError}");
            }
        }

        foreach (var resource in resources)
        {
            actions.Add($"remove:{resource.ResourceType}:{resource.Identifier}");
            if (options.DryRun)
            {
                continue;
            }

            var args = resource.ResourceType switch
            {
                "container" => new[] { "rm", "-f", resource.Identifier },
                "network" => new[] { "network", "rm", resource.Identifier },
                "volume" => new[] { "volume", "rm", resource.Identifier },
                _ => Array.Empty<string>(),
            };
            if (args.Length == 0)
            {
                continue;
            }

            var result = await _processRunner.RunAsync("docker", args, cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                errors.Add($"remove:{resource.ResourceType}:{resource.Identifier}:{result.StandardError}");
            }
        }

        var remaining = await DiscoverOwnedResourcesAsync(options.IncludeAll ? null : options.RunId, cancellationToken);
        if (!options.DryRun && remaining.Count > 0)
        {
            errors.Add($"cleanup-incomplete:{remaining.Count}");
        }

        return new SmokeCleanupResult
        {
            Succeeded = errors.Count == 0,
            DryRun = options.DryRun,
            Resources = resources,
            Actions = actions,
            Errors = errors,
        };
    }

    public async Task<SmokeResourcePreflight> CapturePreflightAsync(CancellationToken cancellationToken = default)
    {
        var activeResources = await DiscoverOwnedResourcesAsync(null, cancellationToken);
        var memory = await RunDiagnosticAsync(OperatingSystem.IsWindows()
            ? ("powershell.exe", new[] { "-NoProfile", "-Command", "Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize,FreePhysicalMemory | Format-List" })
            : ("bash", new[] { "-lc", "free -m" }), cancellationToken);
        var dockerInfo = await RunDiagnosticAsync(("docker", new[] { "info", "--format", "{{json .MemTotal}}" }), cancellationToken);
        var dockerDisk = await RunDiagnosticAsync(("docker", new[] { "system", "df" }), cancellationToken);
        var dockerStats = await RunDiagnosticAsync(("docker", new[] { "stats", "--no-stream", "--format", "table {{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}" }), cancellationToken);

        return new SmokeResourcePreflight
        {
            HostMemorySummary = memory,
            DockerMemorySummary = dockerInfo,
            DockerDiskUsageSummary = dockerDisk,
            DockerStatsSummary = dockerStats,
            ActiveSmokeResources = activeResources,
        };
    }

    private async Task<IReadOnlyList<SmokeOwnedResource>> DiscoverResourcesAsync(string resourceType, IReadOnlyList<string> listArguments, string? runId, CancellationToken cancellationToken)
    {
        var listResult = await _processRunner.RunAsync("docker", listArguments, cancellationToken: cancellationToken);
        if (!listResult.IsSuccess)
        {
            return Array.Empty<SmokeOwnedResource>();
        }

        var ids = listResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<SmokeOwnedResource>();
        }

        var inspectResult = await _processRunner.RunAsync("docker", ["inspect", .. ids], cancellationToken: cancellationToken);
        if (!inspectResult.IsSuccess)
        {
            return Array.Empty<SmokeOwnedResource>();
        }

        using var document = JsonDocument.Parse(inspectResult.StandardOutput);
        var resources = new List<SmokeOwnedResource>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var labels = element.TryGetProperty("Config", out var config) && config.TryGetProperty("Labels", out var configLabels)
                ? configLabels
                : element.TryGetProperty("Labels", out var directLabels)
                    ? directLabels
                    : default;
            if (labels.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var owner = GetLabel(labels, SmokeRuntimeOwnershipLabels.Owner);
            if (!string.Equals(owner, SmokeRuntimeOwnershipLabels.OwnerValue, StringComparison.Ordinal))
            {
                continue;
            }

            var resourceRunId = GetLabel(labels, SmokeRuntimeOwnershipLabels.RunId);
            if (!string.IsNullOrWhiteSpace(runId) && !string.Equals(resourceRunId, runId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resources.Add(new SmokeOwnedResource(
                resourceType,
                element.TryGetProperty("Name", out var name) ? name.GetString()?.TrimStart('/') ?? string.Empty : ids[resources.Count],
                GetLabel(labels, SmokeRuntimeOwnershipLabels.Project),
                resourceRunId,
                GetLabel(labels, SmokeRuntimeOwnershipLabels.Template),
                GetLabel(labels, SmokeRuntimeOwnershipLabels.WorkspaceRoot),
                GetLabel(labels, SmokeRuntimeOwnershipLabels.ComposePath)));
        }

        return resources;
    }

    private async Task<string> RunDiagnosticAsync((string fileName, string[] arguments) command, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(command.fileName, command.arguments, cancellationToken: cancellationToken);
        return result.IsSuccess ? result.StandardOutput : result.StandardError;
    }

    private static string GetLabel(JsonElement labels, string name)
        => labels.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
