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
        services.AddSingleton<IOpenCodeWorkspaceMcpService>(sp => sp.GetRequiredService<OpenCodeWorkspaceMcpService>());
        services.AddHostedService<McpControllerSessionHostedService>();
        return services;
    }
}
