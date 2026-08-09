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
        services.AddSingleton<LocalHostClientAccessor>();
        services.AddSingleton<McpControllerSessionContext>();
        services.AddSingleton<LocalHostOperationStore>();
        services.AddSingleton<LocalHostMcpProxyService>();
        services.AddSingleton<IOpenCodeWorkspaceMcpService>(sp => sp.GetRequiredService<LocalHostMcpProxyService>());
        services.AddHostedService<McpControllerSessionHostedService>();
        return services;
    }
}
