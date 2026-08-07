using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Workspace.Mcp;

public static class OpenCodeWorkspaceLocalServices
{
    public static IServiceCollection AddOpenCodeWorkspaceLocalServices(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new OpenCodeWorkspaceMcpOptions();
        configuration.GetSection("mcp").Bind(options);
        services.AddSingleton(options);
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<LocalHostClientAccessor>();
        services.AddSingleton<McpControllerSessionContext>();
        services.AddSingleton<LocalHostOperationStore>();
        services.AddSingleton<OpenCodeWorkspaceMcpService>();
        services.AddSingleton<LocalHostMcpProxyService>();
        // MCP is an adapter over LocalHost. The in-process service remains only as a fallback
        // for read-only startup diagnostics when LocalHost cannot yet be reached.
        services.AddSingleton<IOpenCodeWorkspaceMcpService>(sp => sp.GetRequiredService<LocalHostMcpProxyService>());
        services.AddHostedService<McpControllerSessionHostedService>();
        return services;
    }
}
