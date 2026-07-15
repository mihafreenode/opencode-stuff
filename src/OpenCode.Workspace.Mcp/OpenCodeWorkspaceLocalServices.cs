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
        services.AddSingleton<IOpenCodeWorkspaceMcpService, OpenCodeWorkspaceMcpService>();
        services.AddSingleton<McpOperationStore>();
        services.AddHostedService(sp => sp.GetRequiredService<McpOperationStore>());
        return services;
    }
}
