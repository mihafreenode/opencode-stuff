using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceImageBuildPlanner
{
    private const string LayerSchemaVersion = "1";
    public const string DockerfileRelativePath = "mounts/config/workspace-image.Dockerfile";
    public const string ToolingScriptRelativePath = "mounts/config/workspace-image-tooling.sh";
    public const string ImageHashLabel = "opencode.workspace.image-input-hash";

    public static WorkspaceImageBuildPlan Create(ResolvedWorkspace workspace, GeneratedArtifactRuntimeMetadata? runtimeMetadata)
    {
        var inputHash = WorkspaceAppliedStateService.ComputeHash(
            LayerSchemaVersion,
            workspace.Definition.Workspace.Image,
            workspace.Definition.Runtime.GetEffectiveNodeMajorVersion().ToString(System.Globalization.CultureInfo.InvariantCulture),
            runtimeMetadata?.TargetPlatform ?? string.Empty,
            string.Join("\n", workspace.AptPackages),
            string.Join("\n", workspace.NpmPackages),
            string.Join("\n", workspace.PipPackages),
            string.Join("\n", workspace.PostInstallCommands),
            workspace.Definition.Terminal.Prompt.Provider,
            workspace.Definition.Terminal.InstallIfMissing.ToString(System.Globalization.CultureInfo.InvariantCulture),
            workspace.Definition.Terminal.Utilities.Zoxide.ToString(System.Globalization.CultureInfo.InvariantCulture),
            workspace.Definition.Terminal.Utilities.Fzf.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OracleWorkspaceFamily.Detect(workspace.Definition).ToString());
        var slug = WorkspacePathBuilder.Slugify(workspace.Definition.Workspace.Name);
        var imageTag = $"opencode-workspace-{slug}:{inputHash[..12].ToLowerInvariant()}";
        return new WorkspaceImageBuildPlan
        {
            ImageTag = imageTag,
            InputHash = inputHash,
            DockerfileRelativePath = DockerfileRelativePath,
            ToolingScriptRelativePath = ToolingScriptRelativePath,
        };
    }
}
