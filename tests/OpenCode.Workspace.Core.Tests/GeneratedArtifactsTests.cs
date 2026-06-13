using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class GeneratedArtifactsTests
{
    [Fact]
    public void ComposeGenerator_IncludesGeneratedHeaderAndServices()
    {
        var generator = new ComposeGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "demo-workspace",
                Image = "ubuntu:24.04",
            },
            Features = new List<string> { "core" },
            Services = new List<string> { "postgres", "pgadmin" },
        };

        var resolved = new ResolvedWorkspace
        {
            Definition = definition,
            Features = Array.Empty<FeatureManifest>(),
            Services = new[]
            {
                new ServiceManifest
                {
                    Id = "postgres",
                    Image = "postgres:17",
                    HostPorts = new List<string> { "15432:5432" },
                    Environment = new Dictionary<string, string> { ["POSTGRES_DB"] = "app" },
                    Volumes = new List<string> { "postgres-data:/var/lib/postgresql/data" },
                },
                new ServiceManifest
                {
                    Id = "pgadmin",
                    Image = "dpage/pgadmin4:9",
                    HostPorts = new List<string> { "18080:80" },
                    DependsOn = new List<string> { "postgres" },
                },
            },
            AptPackages = Array.Empty<string>(),
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        };

        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "demo-workspace"));
        var compose = generator.Generate(resolved, paths);

        Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", compose);
        Assert.Contains("container_name: demo-workspace-workspace", compose);
        Assert.Contains("  postgres:", compose);
        Assert.Contains("  pgadmin:", compose);
        Assert.Contains("volumes:", compose);
    }

    [Fact]
    public void ProvisioningGenerator_IncludesGeneratedHeaderAndOpenCodeInstall()
    {
        var generator = new ProvisioningScriptGenerator();
        var script = generator.Generate(new ResolvedWorkspace
        {
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "demo" },
                Terminal = new TerminalPreferences
                {
                    Prompt = new TerminalPromptPreferences { Provider = "starship" },
                    Utilities = new TerminalUtilityPreferences { Zoxide = true, Fzf = true },
                },
            },
            Features = Array.Empty<FeatureManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = new[] { "git", "curl" },
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        });

        Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", script);
        Assert.Contains("apt-get install -y git curl", script);
        Assert.Contains("useradd -m -d /home/opencode -s /bin/bash opencode", script);
        Assert.Contains("npm install -g opencode-ai", script);
        Assert.Contains("curl -sS https://starship.rs/install.sh | sh -s -- -y", script);
        Assert.Contains("source /opt/opencode-workspace/config/opencode-shell-init.sh", script);
        Assert.Contains("opencode --version", script);
    }

    [Fact]
    public void TerminalArtifactsGenerator_CreatesManagedConfigs()
    {
        var generator = new TerminalArtifactsGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "terminal-demo" },
            Terminal = new TerminalPreferences
            {
                Prompt = new TerminalPromptPreferences { Provider = "starship" },
                Utilities = new TerminalUtilityPreferences { Zoxide = true, Fzf = true },
            },
        };

        var starship = generator.GenerateStarshipConfig(definition);
        var shellInit = generator.GenerateShellInitScript(definition);
        var workspaceShell = generator.GenerateOpencodeWorkspaceShellScript();
        var screenConfig = generator.GenerateScreenConfiguration();

        Assert.Contains("GENERATED FILE", starship);
        Assert.Contains("git_branch", starship);
        Assert.Contains("starship init bash", shellInit);
        Assert.Contains("zoxide init bash", shellInit);
        Assert.Contains("completion.bash", shellInit);
        Assert.Contains("COLORTERM=truecolor", shellInit);
        Assert.Contains("LC_ALL", shellInit);
        Assert.Contains("opencode session list", workspaceShell);
        Assert.Contains("opencode --session", workspaceShell);
        Assert.Contains("Found 0 OpenCode sessions. Starting new session.", workspaceShell);
        Assert.Contains("screen-256color", screenConfig);
        Assert.Contains("defutf8 on", screenConfig);
    }

    [Fact]
    public void AttachArtifactsGenerator_UsesOpencodeUserAndCreatesSessionWhenMissing()
    {
        var generator = new AttachArtifactsGenerator();
        var wrapper = generator.GenerateWindowsTerminalWrapper(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "attach-demo" },
        });

        Assert.Contains("$dockerExe exec -it --user opencode -w /workspace", wrapper);
        Assert.Contains("/opt/opencode-workspace/config/opencode-workspace-shell.sh", wrapper);

        var diagnostics = generator.GenerateTerminalDiagnosticsWrapper(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "attach-demo" },
        });

        Assert.Contains("[attach] Workspace: $workspaceName", diagnostics);
        Assert.Contains("[attach] User: opencode", diagnostics);
        Assert.Contains("[attach] Container: $containerName", diagnostics);
        Assert.Contains("UTF8: ✓ λ € — • │ ─  ", diagnostics);
    }
}
