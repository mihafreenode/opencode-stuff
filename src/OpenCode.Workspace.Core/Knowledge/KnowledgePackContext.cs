using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Knowledge;

public sealed class KnowledgePackContext
{
    public required WorkspaceDefinition Definition { get; init; }

    public required WorkspacePaths Paths { get; init; }

    public required WorkspaceKnowledgePackDefinition Configuration { get; init; }

    public required string ProviderRootPath { get; init; }

    public required string GeneratedRootPath { get; init; }

    public required string DocsRootPath { get; init; }

    public required string IndexesRootPath { get; init; }

    public required string PromptsRootPath { get; init; }

    public required string SharedCacheRootPath { get; init; }

    public bool ExplicitRegenerationRequested { get; init; }
}
