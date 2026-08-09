using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCode.Workspace.RemoteBridge;

namespace OpenCode.Workspace.RemoteBridge.Tests;

public sealed class RemoteBridgeConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "remote-bridge-config-tests", Guid.NewGuid().ToString("n"));
    private readonly string? _previousEnvironmentValue = Environment.GetEnvironmentVariable("RemoteAccess__PublicOrigin");
    private readonly string? _previousEnabledEnvironmentValue = Environment.GetEnvironmentVariable("RemoteAccess__Enabled");

    [Fact]
    public async Task ConfigurationPrecedence_IsPackageThenUserThenEnvironmentThenCommandLine()
    {
        var packageRoot = Path.Combine(_root, "package");
        var packageConfig = Path.Combine(packageRoot, "config", "remote-bridge", "appsettings.json");
        var userConfig = Path.Combine(_root, "user", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(packageConfig)!);
        Directory.CreateDirectory(Path.GetDirectoryName(userConfig)!);
        await File.WriteAllTextAsync(packageConfig, """{"RemoteAccess":{"Enabled":false,"PublicOrigin":"https://package.example.test"}}""");
        await File.WriteAllTextAsync(userConfig, """{"RemoteAccess":{"PublicOrigin":"https://user.example.test"}}""");
        Environment.SetEnvironmentVariable("RemoteAccess__Enabled", "false");
        Environment.SetEnvironmentVariable("RemoteAccess__PublicOrigin", null);

        await using (var userApp = RemoteBridgeApplication.Build([], customize: null, packageRoot, userConfig))
        {
            var userOptions = userApp.Services.GetRequiredService<IOptions<RemoteBridgeOptions>>().Value;
            Assert.Equal("https://user.example.test", userOptions.RemoteAccess.PublicOrigin);
        }

        Environment.SetEnvironmentVariable("RemoteAccess__PublicOrigin", "https://environment.example.test");
        await using (var environmentApp = RemoteBridgeApplication.Build([], customize: null, packageRoot, userConfig))
        {
            var environmentOptions = environmentApp.Services.GetRequiredService<IOptions<RemoteBridgeOptions>>().Value;
            Assert.Equal("https://environment.example.test", environmentOptions.RemoteAccess.PublicOrigin);
        }

        await using var app = RemoteBridgeApplication.Build(
            ["--RemoteAccess:Enabled=false", "--RemoteAccess:PublicOrigin=https://command.example.test"],
            customize: null,
            packageRoot,
            userConfig);
        var options = app.Services.GetRequiredService<IOptions<RemoteBridgeOptions>>().Value;

        Assert.False(options.RemoteAccess.Enabled);
        Assert.Equal("https://command.example.test", options.RemoteAccess.PublicOrigin);
    }

    [Fact]
    public async Task DisabledProgram_ExitsWithoutStartingListener()
    {
        var completed = OpenCode.Workspace.RemoteBridge.Program.Main(["--RemoteAccess:Enabled=false"]);
        await completed.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(completed.IsCompletedSuccessfully);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RemoteAccess__PublicOrigin", _previousEnvironmentValue);
        Environment.SetEnvironmentVariable("RemoteAccess__Enabled", _previousEnabledEnvironmentValue);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
