using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.Mcp;

namespace OpenCode.Workspace.Api.IntegrationTests;

internal sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _configuration = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiTestFactory(Dictionary<string, string?>? configuration = null, Action<IServiceCollection>? configureServices = null)
    {
        if (configuration is not null)
        {
            foreach (var pair in configuration)
            {
                _configuration[pair.Key] = pair.Value;
            }
        }

        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_configuration));
        builder.ConfigureServices(services =>
        {
            _configureServices?.Invoke(services);
        });
    }
}

internal sealed class ApiIntegrationEnvironment : IDisposable
{
    public ApiIntegrationEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "opencode-api-integration", Guid.NewGuid().ToString("n"));
        WorkspaceStateRoot = Path.Combine(Root, "state");
        SmokeArtifactsRoot = Path.Combine(Root, "artifacts", "template-smoke");
        WorkspaceParentRoot = Path.Combine(Root, "workspaces");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WorkspaceStateRoot);
        Directory.CreateDirectory(SmokeArtifactsRoot);
        Directory.CreateDirectory(WorkspaceParentRoot);
    }

    public string Root { get; }
    public string WorkspaceStateRoot { get; }
    public string SmokeArtifactsRoot { get; }
    public string WorkspaceParentRoot { get; }

    public ApiTestFactory CreateFactory(Action<IServiceCollection>? configureServices = null)
        => new(new Dictionary<string, string?>
        {
            ["mcp:catalogRoot"] = Path.Combine(TestPaths.RepositoryRoot, "catalog"),
            ["mcp:workspaceStateRoot"] = WorkspaceStateRoot,
            ["mcp:smokeArtifactsRoot"] = SmokeArtifactsRoot,
        }, configureServices);

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class ServiceCollectionExtensions
{
    public static void ReplaceSingleton<TService>(this IServiceCollection services, TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
    }
}
