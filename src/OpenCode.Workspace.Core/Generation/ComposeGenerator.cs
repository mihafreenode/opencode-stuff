using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

/// <summary>
/// Generates compose.yaml as a transparent runtime artifact. The generated file
/// must stay readable because contributors will inspect it when troubleshooting,
/// but durable changes still belong in workspace.yaml and catalog manifests.
/// </summary>
public sealed class ComposeGenerator
{
    public string Generate(ResolvedWorkspace workspace, WorkspacePaths paths)
    {
        var slug = WorkspacePathBuilder.Slugify(workspace.Definition.Workspace.Name);
        var workspaceDependencies = workspace.Services
            .OrderBy(service => service.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var builder = new StringBuilder();

        builder.AppendLine("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES");
        builder.AppendLine("# Source inputs: workspace.yaml and catalog manifests under catalog/.");
        builder.AppendLine("# User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.");
        builder.AppendLine("services:");
        builder.AppendLine("  workspace:");
        builder.AppendLine($"    image: {workspace.Definition.Workspace.Image}");
        builder.AppendLine($"    container_name: {slug}-workspace");
        builder.AppendLine("    tty: true");
        builder.AppendLine("    stdin_open: true");
        builder.AppendLine("    working_dir: /workspace");
        builder.AppendLine("    command:");
        builder.AppendLine("      - bash");
        builder.AppendLine("      - -lc");
        builder.AppendLine("      - while sleep 3600; do :; done");
        if (workspace.Definition.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine("    ports:");
            builder.AppendLine("      - \"127.0.0.1:${MARIMO_PORT}:2718\"");
        }
        builder.AppendLine("    volumes:");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.InboxPath)}:/opt/opencode-workspace/inbox");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.WorkspacePath)}:/workspace");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.UserPath)}:/user");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.HomePath)}:/home/opencode");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.ConfigPath)}:/opt/opencode-workspace/config");
        AppendDependsOn(builder, workspaceDependencies, service => service.WorkspaceDependsOnCondition);

        foreach (var service in workspace.Services)
        {
            builder.AppendLine($"  {service.Id}:");
            builder.AppendLine($"    image: {service.Image}");

            if (service.Profiles.Count > 0)
            {
                builder.AppendLine("    profiles:");
                foreach (var profile in service.Profiles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"      - {profile}");
                }
            }

            if (service.HostPorts.Count > 0)
            {
                builder.AppendLine("    ports:");
                foreach (var port in service.HostPorts)
                {
                    builder.AppendLine($"      - \"{port}\"");
                }
            }

            if (service.Environment.Count > 0)
            {
                builder.AppendLine("    environment:");
                foreach (var pair in service.Environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"      {pair.Key}: \"{pair.Value}\"");
                }
            }

            if (!string.IsNullOrWhiteSpace(service.Restart))
            {
                builder.AppendLine($"    restart: {service.Restart}");
            }

            if (service.Healthcheck is not null && service.Healthcheck.Test.Count > 0)
            {
                builder.AppendLine("    healthcheck:");
                builder.AppendLine("      test:");
                foreach (var testPart in service.Healthcheck.Test)
                {
                    builder.AppendLine($"        - \"{EscapeYamlDoubleQuoted(testPart)}\"");
                }

                if (!string.IsNullOrWhiteSpace(service.Healthcheck.Interval))
                {
                    builder.AppendLine($"      interval: {service.Healthcheck.Interval}");
                }

                if (!string.IsNullOrWhiteSpace(service.Healthcheck.Timeout))
                {
                    builder.AppendLine($"      timeout: {service.Healthcheck.Timeout}");
                }

                if (service.Healthcheck.Retries is not null)
                {
                    builder.AppendLine($"      retries: {service.Healthcheck.Retries.Value}");
                }

                if (!string.IsNullOrWhiteSpace(service.Healthcheck.StartPeriod))
                {
                    builder.AppendLine($"      start_period: {service.Healthcheck.StartPeriod}");
                }
            }

            if (service.DependsOn.Count > 0)
            {
                builder.AppendLine("    depends_on:");
                foreach (var dependency in service.DependsOn)
                {
                    builder.AppendLine($"      - {dependency}");
                }
            }

            if (service.Volumes.Count > 0)
            {
                builder.AppendLine("    volumes:");
                foreach (var volume in service.Volumes)
                {
                    builder.AppendLine($"      - {ResolveVolumeBinding(volume, paths)}");
                }
            }
        }

        var namedVolumes = workspace.Services.SelectMany(service => service.Volumes)
            .Select(volume => ResolveVolumeBinding(volume, paths))
            .Select(volume => volume.Split(':', 2)[0])
            .Where(volume => !volume.Contains('/') && !volume.Contains('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(volume => volume, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (namedVolumes.Count > 0)
        {
            builder.AppendLine("volumes:");
            foreach (var volume in namedVolumes)
            {
                builder.AppendLine($"  {volume}:");
            }
        }

        return builder.ToString();
    }

    private static string ResolveVolumeBinding(string volume, WorkspacePaths paths)
    {
        return volume
            .Replace("${WORKSPACE_DOCKER_PATH}", WorkspacePathBuilder.ToDockerVolumePath(paths.RootPath), StringComparison.Ordinal)
            .Replace("${WORKSPACE_TUTORIAL_DOCKER_PATH}", WorkspacePathBuilder.ToDockerVolumePath(Path.Combine(paths.RootPath, "tutorial")), StringComparison.Ordinal)
            .Replace("${WORKSPACE_LOCAL_DOCKER_PATH}", WorkspacePathBuilder.ToDockerVolumePath(Path.Combine(paths.RootPath, ".local")), StringComparison.Ordinal);
    }

    private static void AppendDependsOn(StringBuilder builder, IReadOnlyList<ServiceManifest> services, Func<ServiceManifest, string?> getCondition)
    {
        if (services.Count == 0)
        {
            return;
        }

        var usesConditionalForm = services.Any(service => !string.IsNullOrWhiteSpace(getCondition(service)));
        builder.AppendLine("    depends_on:");

        foreach (var service in services)
        {
            if (!usesConditionalForm)
            {
                builder.AppendLine($"      - {service.Id}");
                continue;
            }

            builder.AppendLine($"      {service.Id}:");
            builder.AppendLine($"        condition: {ResolveDependsOnCondition(getCondition(service))}");
        }
    }

    private static string ResolveDependsOnCondition(string? condition)
        => string.IsNullOrWhiteSpace(condition) ? "service_started" : condition.Trim();

    private static string EscapeYamlDoubleQuoted(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
