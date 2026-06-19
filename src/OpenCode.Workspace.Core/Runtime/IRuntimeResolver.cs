using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public interface IRuntimeResolver
{
    Task<ResolvedRuntimePlan> ResolveAsync(WorkspaceDefinition definition, HostPlatformInfo hostPlatform, CancellationToken cancellationToken = default);
}
