using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class LocalReleaseBuildScriptTests
{
    [Fact]
    public void BuildReleaseScripts_Exist_And_Use_Windows_PowerShell_Entry_Point()
    {
        var repositoryRoot = TestPaths.RepositoryRoot;
        var scriptPath = Path.Combine(repositoryRoot, "tools", "build-release.ps1");
        var helperPath = Path.Combine(repositoryRoot, "tools", "build-release-from-wsl.sh");

        Assert.True(File.Exists(scriptPath));
        Assert.True(File.Exists(helperPath));

        var helper = File.ReadAllText(helperPath);
        Assert.Contains("powershell.exe", helper, StringComparison.Ordinal);
        Assert.Contains("wslpath -w", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReleaseScript_Defaults_To_WinX64_And_Derives_Repository_Root_From_Script_Path()
    {
        var script = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "build-release.ps1"));

        Assert.Contains("[string]$RuntimeIdentifier = \"win-x64\"", script, StringComparison.Ordinal);
        Assert.Contains("[string]$Configuration = \"Release\"", script, StringComparison.Ordinal);
        Assert.Contains("[string]$OutputRoot = \"artifacts/release\"", script, StringComparison.Ordinal);
        Assert.Contains("$RepositoryRoot = Split-Path $PSScriptRoot -Parent", script, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.slnx", script, StringComparison.Ordinal);
        Assert.Contains("Local release packaging currently supports win-x64 only.", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReleaseScript_Publishes_All_Five_Hosts_And_Uses_ReleaseTool()
    {
        var script = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "build-release.ps1"));
        var ciWorkflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));

        var projectPaths = new[]
        {
            "src/OpenCode.Workspace.Avalonia/OpenCode.Workspace.Avalonia.csproj",
            "src/OpenCode.Workspace.Cli/OpenCode.Workspace.Cli.csproj",
            "src/OpenCode.Workspace.Api/OpenCode.Workspace.Api.csproj",
            "src/OpenCode.Workspace.Mcp/OpenCode.Workspace.Mcp.csproj",
            "src/OpenCode.Workspace.RemoteBridge/OpenCode.Workspace.RemoteBridge.csproj",
        };

        foreach (var projectPath in projectPaths)
        {
            Assert.Contains(projectPath, script, StringComparison.Ordinal);
            Assert.Contains(projectPath, ciWorkflow, StringComparison.Ordinal);
        }

        Assert.Contains("OpenCode.Workspace.ReleaseTool", script, StringComparison.Ordinal);
        Assert.Contains("assemble", script, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.exe", script, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.Cli.exe", script, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.LocalHost.exe", script, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.Mcp.exe", script, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.RemoteBridge.exe", script, StringComparison.Ordinal);
        Assert.Contains("Get-BooleanDefault -Value $SelfContained -Default $true", script, StringComparison.Ordinal);
        Assert.Contains("bin\\local-host", script, StringComparison.Ordinal);
        Assert.Contains("bin\\remote-bridge", script, StringComparison.Ordinal);
        Assert.DoesNotContain("bin\\api\\", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReleaseScript_Invokes_Package_Validation_By_Default_And_Restricts_Cleanup_To_Artifacts()
    {
        var script = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tools", "build-release.ps1"));

        Assert.Contains("Get-BooleanDefault -Value $ValidatePackage -Default $true", script, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_EXISTING_PACKAGE_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("PackagedDistributionTests.ExtractedDistribution_ResolvesPackagedContent_AndHostsExitGracefully", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PackagedDistributionTests.PackagedMcp_RunSmoke_ReportsIncrementalProgress_AndShutsDownCleanly", script, StringComparison.Ordinal);
        Assert.Contains("$ArtifactsRoot", script, StringComparison.Ordinal);
        Assert.Contains("Refusing to clean path outside repository artifacts root", script, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker system prune", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagingDocs_Reference_Local_Windows_And_Wsl_Build_Flow_And_Packaged_Mcp_Path()
    {
        var doc = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "docs", "development", "packaging.md"));

        Assert.Contains(".\\tools\\build-release.ps1 -Clean", doc, StringComparison.Ordinal);
        Assert.Contains("./tools/build-release-from-wsl.sh -Clean", doc, StringComparison.Ordinal);
        Assert.Contains("artifacts/release/win-x64/package/", doc, StringComparison.Ordinal);
        Assert.Contains("bin/mcp", doc, StringComparison.Ordinal);
        Assert.Contains("freshly extracted archive", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationScripts_UseCurrentCliAssemblyName()
    {
        foreach (var relativePath in new[] { "tools/test-integration.ps1", "tools/test-integration.sh" })
        {
            var script = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, relativePath));
            Assert.Contains("OpenCode.Workspace.Cli.dll", script, StringComparison.Ordinal);
            Assert.DoesNotContain("opencode.dll", script, StringComparison.OrdinalIgnoreCase);
        }
    }
}
