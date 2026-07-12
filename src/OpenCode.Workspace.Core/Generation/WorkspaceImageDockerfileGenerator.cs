using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Generation;

public sealed class WorkspaceImageDockerfileGenerator
{
    public string Generate(ResolvedWorkspace workspace, WorkspaceImageBuildPlan imagePlan, GeneratedArtifactRuntimeMetadata? runtimeMetadata = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(GeneratedArtifactRuntimeMetadataBuilder.BuildCommentHeader(
            runtimeMetadata,
            "Source inputs: workspace.yaml, catalog manifests, and generated workspace image tooling script.",
            "User edits are not preserved. Edit workspace.yaml or catalog manifests instead."));
        builder.AppendLine($"FROM {workspace.Definition.Workspace.Image}");
        builder.AppendLine("SHELL [\"/bin/bash\", \"-lc\"]");
        builder.AppendLine($"LABEL {WorkspaceImageBuildPlanner.ImageHashLabel}=\"{imagePlan.InputHash}\"");
        builder.AppendLine($"COPY {imagePlan.ToolingScriptRelativePath.Replace('\\', '/')} /tmp/workspace-image-tooling.sh");
        builder.AppendLine("RUN bash /tmp/workspace-image-tooling.sh && rm -f /tmp/workspace-image-tooling.sh");
        return builder.ToString();
    }
}
