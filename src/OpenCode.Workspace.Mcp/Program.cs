using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpenCode.Workspace.Mcp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        var options = new OpenCodeWorkspaceMcpOptions();
        builder.Configuration.GetSection("mcp").Bind(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ISystemClock, SystemClock>();
        builder.Services.AddSingleton<IOpenCodeWorkspaceMcpService, OpenCodeWorkspaceMcpService>();
        builder.Services.AddSingleton<McpOperationStore>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<OpenCodeWorkspaceMcpTools>()
            .WithResources<OpenCodeWorkspaceMcpResources>();

        await builder.Build().RunAsync();
    }
}
