using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Knowledge;

public interface IKnowledgePackProvider
{
    string ProviderId { get; }

    string Version { get; }

    bool IsApplicable(WorkspaceDefinition definition, WorkspaceKnowledgePackDefinition configuration);

    Task<ProvisionedKnowledgePackContent> GenerateAsync(KnowledgePackContext context, CancellationToken cancellationToken = default);
}
