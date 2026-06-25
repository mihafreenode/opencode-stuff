using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Platform.Windows.Tests;

public sealed class WindowsHostEnvironmentIntegrationTests
{
    [Fact]
    public void LocalizationLoading_ReturnsEnglishAndSlovenianStrings()
    {
        var root = TestPaths.RepositoryRoot;
        var localizationRoot = Path.Combine(root, "Localization");

        var english = new PoLocalizationService(localizationRoot, "en");
        var slovenian = new PoLocalizationService(localizationRoot, "sl");

        Assert.Equal("OpenCode Workspace Manager", english.Get("app.title"));
        Assert.Equal("Nadzorna plošča delovnih prostorov", slovenian.Get("dashboard.title"));
    }

    [Fact]
    public async Task WindowsHostBuildIntegration_DotNetHostIsAvailable()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync("cmd.exe", ["/c", "dotnet", "--info"]);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.StandardOutputLines, line => line.Contains(".NET SDK", StringComparison.OrdinalIgnoreCase));
    }
}
