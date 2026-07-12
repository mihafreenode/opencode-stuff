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
            "Source inputs: workspace.yaml, catalog manifests, and generated workspace image tooling scripts.",
            "User edits are not preserved. Edit workspace.yaml or catalog manifests instead."));
        builder.AppendLine($"FROM {workspace.Definition.Workspace.Image}");
        builder.AppendLine("SHELL [\"/bin/bash\", \"-lc\"]");

        foreach (var layerScript in imagePlan.LayerScripts)
        {
            var sourcePath = layerScript.RelativePath.Replace('\\', '/');
            var targetPath = $"/tmp/{Path.GetFileName(layerScript.RelativePath)}";
            builder.AppendLine($"COPY {sourcePath} {targetPath}");
            builder.AppendLine($"RUN bash {targetPath} && rm -f {targetPath}");
        }

        builder.AppendLine($"LABEL {WorkspaceImageBuildPlanner.ImageHashLabel}=\"{imagePlan.InputHash}\"");
        return builder.ToString();
    }
}
