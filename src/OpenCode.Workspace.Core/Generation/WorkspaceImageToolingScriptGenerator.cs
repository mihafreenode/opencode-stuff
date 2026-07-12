using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Generation;

public sealed class WorkspaceImageToolingScriptGenerator
{
    private readonly WorkspaceImageToolingLayoutBuilder _layoutBuilder = new();

    public string Generate(ResolvedWorkspace workspace)
        => _layoutBuilder.Build(workspace).CombinedScript;

    public WorkspaceImageToolingLayout GenerateLayout(ResolvedWorkspace workspace)
        => _layoutBuilder.Build(workspace);
}
