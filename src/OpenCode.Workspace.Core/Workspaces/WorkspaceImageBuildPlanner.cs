using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceImageBuildPlanner
{
    private const string LayerSchemaVersion = "2";
    public const string DockerfileRelativePath = "mounts/config/workspace-image.Dockerfile";
    public const string ToolingScriptRelativePath = "mounts/config/workspace-image-tooling.sh";
    public const string ImageHashLabel = "opencode.workspace.image-input-hash";

    public static WorkspaceImageBuildPlan Create(ResolvedWorkspace workspace, GeneratedArtifactRuntimeMetadata? runtimeMetadata)
    {
        _ = runtimeMetadata;
        var layout = new WorkspaceImageToolingLayoutBuilder().Build(workspace);
        var categoryHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkspaceImageToolingLayoutBuilder.BaseImageCategory] = WorkspaceAppliedStateService.ComputeHash(
                LayerSchemaVersion,
                WorkspaceImageToolingLayoutBuilder.BaseImageCategory,
                workspace.Definition.Workspace.Image),
        };

        foreach (var layerScript in layout.LayerScripts)
        {
            categoryHashes[layerScript.CategoryId] = WorkspaceAppliedStateService.ComputeHash(
                LayerSchemaVersion,
                layerScript.CategoryId,
                layerScript.Content);
        }

        var inputHash = WorkspaceAppliedStateService.ComputeHash(
            LayerSchemaVersion,
            string.Join("\n", WorkspaceImageToolingLayoutBuilder.GetOrderedCategories().Select(category => category + "\n" + categoryHashes.GetValueOrDefault(category, string.Empty))));
        var slug = WorkspacePathBuilder.Slugify(workspace.Definition.Workspace.Name);
        var imageTag = $"opencode-workspace-{slug}:{inputHash[..12].ToLowerInvariant()}";
        return new WorkspaceImageBuildPlan
        {
            ImageTag = imageTag,
            InputHash = inputHash,
            InputCategoryHashes = categoryHashes,
            DockerfileRelativePath = DockerfileRelativePath,
            ToolingScriptRelativePath = ToolingScriptRelativePath,
            LayerScripts = layout.LayerScripts,
        };
    }
}
