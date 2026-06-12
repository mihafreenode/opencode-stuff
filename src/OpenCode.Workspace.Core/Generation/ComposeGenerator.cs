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
        builder.AppendLine("    volumes:");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.InboxPath)}:/opt/opencode-workspace/inbox");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.WorkspacePath)}:/workspace");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.UserPath)}:/user");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.HomePath)}:/home/opencode");
        builder.AppendLine($"      - {WorkspacePathBuilder.ToDockerVolumePath(paths.ConfigPath)}:/opt/opencode-workspace/config");

        foreach (var service in workspace.Services)
        {
            builder.AppendLine($"  {service.Id}:");
            builder.AppendLine($"    image: {service.Image}");

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
                    builder.AppendLine($"      - {volume}");
                }
            }
        }

        var namedVolumes = workspace.Services.SelectMany(service => service.Volumes)
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
}
