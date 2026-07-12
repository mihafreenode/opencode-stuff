using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class DockerWorkspaceImageBuilder : IWorkspaceImageBuilder
{
    private readonly IContainerRuntime _containerRuntime;

    public DockerWorkspaceImageBuilder(IContainerRuntime containerRuntime)
    {
        _containerRuntime = containerRuntime;
    }

    public async Task EnsureImageAsync(WorkspaceDefinition definition, WorkspacePaths paths, GeneratedWorkspaceArtifacts artifacts, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        if (await IsImageCurrentAsync(artifacts, log, cancellationToken))
        {
            log?.Invoke(new CommandLogEntry { Source = "app", Message = "Building Workspace Image: reusing existing image." });
            return;
        }

        log?.Invoke(new CommandLogEntry { Source = "app", Message = "Building Workspace Image" });
        var buildArguments = new List<string>
        {
            "compose",
            "--project-name",
            WorkspacePathBuilder.Slugify(definition.Workspace.Name),
            "--file",
            paths.ComposePath,
        };

        foreach (var profile in GetComposeProfiles(definition))
        {
            buildArguments.Add("--profile");
            buildArguments.Add(profile);
        }

        buildArguments.Add("build");
        buildArguments.Add("workspace");

        var result = await _containerRuntime.RunSimpleDockerCommandAsync(buildArguments, log, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Workspace image build failed.{Environment.NewLine}{result.StandardError}{Environment.NewLine}{result.StandardOutput}".Trim());
        }

        if (!await IsImageCurrentAsync(artifacts, log, cancellationToken))
        {
            throw new InvalidOperationException("Workspace image build completed but the expected image tag or hash label is still missing.");
        }
    }

    private async Task<bool> IsImageCurrentAsync(GeneratedWorkspaceArtifacts artifacts, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var inspect = await _containerRuntime.RunSimpleDockerCommandAsync(
        [
            "image",
            "inspect",
            artifacts.WorkspaceImageTag,
            "--format",
            $"{{{{index .Config.Labels \"{WorkspaceImageBuildPlanner.ImageHashLabel}\"}}}}",
        ], log, cancellationToken);

        return inspect.IsSuccess
            && string.Equals(inspect.StandardOutput.Trim(), artifacts.WorkspaceImageInputHash, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetComposeProfiles(WorkspaceDefinition definition)
    {
        var profiles = new List<string>();
        if (definition.Services.Contains("oracle-ords", StringComparer.OrdinalIgnoreCase))
        {
            profiles.Add("oracle-apex");
        }

        return profiles;
    }
}
