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
            Capabilities = Array.Empty<CapabilityManifest>(),
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
    public void ComposeGenerator_ForWorkspaceWithoutServices_OmitsWorkspaceDependsOn()
    {
        var generator = new ComposeGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Id = "odip-analiza",
                Name = "Odip Analiza",
                Image = "ubuntu:24.04",
            },
            Provider = new WorkspaceProviderDefinition
            {
                Type = "git",
                Url = "git@ssh.dev.azure.com:v3/KOPA-Projects/ODIP/Analiza",
            },
            Runtime = new WorkspaceRuntimeDefinition
            {
                Default = "default",
            },
            Features = new List<string> { "core", "document-processing", "ocr-processing", "spellcheck" },
            Services = new List<string>(),
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };

        var resolved = new ResolvedWorkspace
        {
            Definition = definition,
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = Array.Empty<string>(),
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        };

        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "odip-analiza"));
        var compose = generator.Generate(resolved, paths);

        Assert.DoesNotContain("depends_on:", compose);
    }

    [Fact]
    public void ComposeGenerator_ForSimpleWorkspaceDependencies_UsesArrayForm()
    {
        var generator = new ComposeGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "analiza-with-db",
                Image = "ubuntu:24.04",
            },
            Features = new List<string> { "core" },
            Services = new List<string> { "oracle" },
        };

        var resolved = new ResolvedWorkspace
        {
            Definition = definition,
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = new[]
            {
                new ServiceManifest
                {
                    Id = "oracle",
                    Image = "gvenzl/oracle-free:23-slim-faststart",
                },
            },
            AptPackages = Array.Empty<string>(),
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        };

        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "analiza-with-db"));
        var compose = generator.Generate(resolved, paths);

        Assert.Contains("    depends_on:", compose);
        Assert.Contains("      - oracle", compose);
        Assert.DoesNotContain("condition:", compose);
    }

    [Fact]
    public void ComposeGenerator_ForConditionalWorkspaceDependencies_UsesObjectForm()
    {
        var generator = new ComposeGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "oracle-demo",
                Image = "ubuntu:24.04",
            },
            Features = new List<string> { "core", "oracle-demo" },
            Services = new List<string> { "oracle-demo" },
        };

        var resolved = new ResolvedWorkspace
        {
            Definition = definition,
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = new[]
            {
                new ServiceManifest
                {
                    Id = "oracle-demo",
                    Image = "gvenzl/oracle-free:23-slim-faststart",
                    WorkspaceDependsOnCondition = "service_healthy",
                },
            },
            AptPackages = Array.Empty<string>(),
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        };

        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "oracle-demo"));
        var compose = generator.Generate(resolved, paths);

        Assert.Contains("    depends_on:", compose);
        Assert.Contains("      oracle-demo:", compose);
        Assert.Contains("        condition: service_healthy", compose);
    }

    [Fact]
    public void EnvironmentFileGenerator_IncludesOracleDefaultsForOracleDemoWorkspaces()
    {
        var generator = new EnvironmentFileGenerator();
        var content = generator.Generate(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
            Services = new List<string> { "oracle-demo" },
        });

        Assert.Contains("ORACLE_PASSWORD=change-on-first-demo", content);
        Assert.Contains("ORACLE_DEMO_SERVICE=FREEPDB1", content);
        Assert.Contains("ORACLE_HOST_PORT=1521", content);
        Assert.Contains("ORACLE_ORDS_BASE_URL=http://localhost:8181/ords", content);
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
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = new[] { "git", "curl" },
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        });

        Assert.Contains("# GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES", script);
        Assert.Contains("apt-get install -y git curl", script);
        Assert.Contains("https://deb.nodesource.com/setup_22.x", script);
        Assert.Contains("apt-get remove -y nodejs npm || true", script);
        Assert.Contains("apt-get install -y nodejs", script);
        Assert.Contains("apt-cache policy nodejs | sed -n '1,20p'", script);
        Assert.Contains("useradd -m -d /home/opencode -s /bin/bash opencode", script);
        Assert.Contains("npm --version", script);
        Assert.Contains("python --version", script);
        Assert.Contains("python3 --version", script);
        Assert.Contains("which python", script);
        Assert.Contains("which python3", script);
        Assert.Contains("node -e \"console.log(process.version)\"", script);
        Assert.Contains("npm install -g opencode-ai", script);
        Assert.Contains("curl -sS https://starship.rs/install.sh | sh -s -- -y", script);
        Assert.Contains("source /opt/opencode-workspace/config/opencode-shell-init.sh", script);
        Assert.Contains("opencode --version", script);
    }

    [Fact]
    public void ProvisioningGenerator_ForOracleWorkspace_UsesDynamicLibaioHelper()
    {
        var generator = new ProvisioningScriptGenerator();
        var script = generator.Generate(new ResolvedWorkspace
        {
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
                Features = new List<string> { "core", "oracle-demo" },
                Services = new List<string> { "oracle-demo" },
                Terminal = new TerminalPreferences
                {
                    Prompt = new TerminalPromptPreferences { Provider = "starship" },
                    Utilities = new TerminalUtilityPreferences(),
                },
            },
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = new[] { "curl", "rlwrap", "unzip", "libaio1" },
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        });

        Assert.Contains("Detected Ubuntu version", script);
        Assert.Contains("Selected libaio package", script);
        Assert.Contains("if apt-cache policy libaio1 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then", script);
        Assert.Contains("elif apt-cache policy libaio1t64 | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then", script);
        Assert.Contains("dpkg -L \"${oracle_libaio_pkg}\"", script);
        Assert.Contains("ln -sf /usr/lib/x86_64-linux-gnu/libaio.so.1t64 /usr/lib/x86_64-linux-gnu/libaio.so.1", script);
        Assert.Contains("ldconfig", script);
        Assert.Contains("Selected Java package", script);
        Assert.Contains("if apt-cache policy openjdk-21-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then", script);
        Assert.Contains("elif apt-cache policy openjdk-17-jre-headless | grep -F \"Candidate:\" | grep -Fvq \"(none)\"; then", script);
        Assert.Contains("No compatible Java runtime package found for SQLcl", script);
        Assert.Contains("oracle_sqlplus_root=/opt/oracle/instantclient", script);
        Assert.Contains("if command -v sqlplus >/dev/null 2>&1 && sqlplus -v; then", script);
        Assert.Contains("SQL*Plus already installed and valid; skipping reinstall.", script);
        Assert.Contains("instantclient-basiclite-linux.x64", script);
        Assert.Contains("instantclient-sqlplus-linux.x64", script);
        Assert.Contains("ldd \"${sqlplus_launcher}\"", script);
        Assert.Contains("/etc/ld.so.conf.d/oracle-instantclient.conf", script);
        Assert.Contains("export ORACLE_CLIENT_HOME=${oracle_client_home}", script);
        Assert.Contains("sqlplus -S \"${oracle_connection}\" @\"${oracle_sqlplus_probe_script}\"", script);
        Assert.Contains("SQL*Plus validation query failed", script);
        Assert.Contains("validate_sqlcl_install()", script);
        Assert.Contains("if validate_sqlcl_install /opt/sqlcl; then", script);
        Assert.Contains("SQLcl already installed and valid; skipping reinstall.", script);
        Assert.Contains("Existing SQLcl install missing or invalid. Reinstalling.", script);
        Assert.Contains("oracle_sqlcl_extract=/tmp/sqlcl-extract", script);
        Assert.Contains("Failed to download the official SQLcl zip archive", script);
        Assert.Contains("Unexpected SQLcl layout: staged sqlcl/bin/sql was not created", script);
        Assert.Contains("Diagnostic only: SqlCli.class was not found in staged dbtools-sqlcl.jar", script);
        Assert.Contains("rm -rf /opt/sqlcl", script);
        Assert.Contains("cp -a \"${oracle_sqlcl_extract}/.\" /opt/sqlcl/", script);
        Assert.Contains("sql -v", script);
        Assert.Contains("SELECT 'Connection OK' AS status FROM dual;", script);
        Assert.Contains("SQLcl connectivity probe failed on attempt ${attempt}/5", script);
        Assert.Contains("Staged SQLcl install failed runtime validation", script);
        Assert.Contains("Reinstalled SQLcl failed runtime validation after activation", script);
        Assert.Contains("java -version", script);
        Assert.DoesNotContain("apt-get install -y curl rlwrap unzip libaio1", script);
    }

    [Fact]
    public void ProvisioningGenerator_ForOracleApexWorkspace_WaitsForOrdsAndApex()
    {
        var generator = new ProvisioningScriptGenerator();
        var script = generator.Generate(new ResolvedWorkspace
        {
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-apex-demo" },
                Features = new List<string> { "core", "oracle-demo", "oracle-apex-demo" },
                Services = new List<string> { "oracle-demo", "oracle-ords" },
            },
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = new[] { "curl", "rlwrap", "unzip" },
            NpmPackages = Array.Empty<string>(),
            PipPackages = Array.Empty<string>(),
            PostInstallCommands = Array.Empty<string>(),
        });

        Assert.Contains("oracle_ords_url=http://oracle-ords:8080/ords", script);
        Assert.Contains("oracle_apex_url=http://oracle-ords:8080/ords/apex_admin", script);
        Assert.Contains("ORDS endpoint did not become reachable", script);
        Assert.Contains("OracleRuntimeFailure: APEX login route not reachable", script);
    }

    [Fact]
    public void ProvisioningGenerator_ForDocumentationWorkspace_IncludesToolingAndFontSetup()
    {
        var generator = new ProvisioningScriptGenerator();
        var script = generator.Generate(new ResolvedWorkspace
        {
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "documentation-features" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 24 },
                Features = new List<string> { "core", "document-processing" },
                Terminal = new TerminalPreferences
                {
                    Prompt = new TerminalPromptPreferences { Provider = "starship" },
                    Utilities = new TerminalUtilityPreferences(),
                },
            },
            Features = Array.Empty<FeatureManifest>(),
            Capabilities = Array.Empty<CapabilityManifest>(),
            Services = Array.Empty<ServiceManifest>(),
            AptPackages = new[] { "pandoc", "fonts-crosextra-carlito", "fonts-jetbrains-mono" },
            NpmPackages = new[] { "playwright", "@mermaid-js/mermaid-cli" },
            PipPackages = new[] { "weasyprint", "pypdf", "pymupdf", "reportlab" },
            PostInstallCommands = new[]
            {
                "command -v typst >/dev/null 2>&1 || install /tmp/typst-install/typst-*/typst /usr/local/bin/typst",
                "playwright install chromium",
                "fc-cache -fv",
            },
        });

        Assert.Contains("apt-get install -y pandoc fonts-crosextra-carlito fonts-jetbrains-mono", script);
        Assert.Contains("https://deb.nodesource.com/setup_24.x", script);
        Assert.Contains("apt-get remove -y nodejs npm || true", script);
        Assert.Contains("npm install -g playwright @mermaid-js/mermaid-cli", script);
        Assert.Contains("pip3 install --break-system-packages weasyprint pypdf pymupdf reportlab", script);
        Assert.Contains("command -v typst", script);
        Assert.Contains("playwright install chromium", script);
        Assert.Contains("fc-cache -fv", script);
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
        var workspaceShell = generator.GenerateOpencodeWorkspaceShellScript(definition);
        var screenConfig = generator.GenerateScreenConfiguration();

        Assert.Contains("GENERATED FILE", starship);
        Assert.Contains("git_branch", starship);
        Assert.Contains("starship init bash", shellInit);
        Assert.Contains("zoxide init bash", shellInit);
        Assert.Contains("completion.bash", shellInit);
        Assert.Contains("COLORTERM=truecolor", shellInit);
        Assert.Contains("LC_ALL", shellInit);
        Assert.Contains("export JAVA_HOME=", shellInit);
        Assert.Contains("export ORACLE_CLIENT_HOME=${oracle_client_home}", shellInit);
        Assert.Contains("export LD_LIBRARY_PATH=${oracle_client_home}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}", shellInit);
        Assert.Contains("export PATH=${oracle_client_home}:${PATH}", shellInit);
        Assert.Contains("opencode session list", workspaceShell);
        Assert.Contains("opencode --session", workspaceShell);
        Assert.Contains("Found 0 OpenCode sessions. Starting new session.", workspaceShell);
        Assert.Contains("export ORACLE_CLIENT_HOME=${oracle_client_home}", workspaceShell);
        Assert.Contains("cleanup_terminal_state()", workspaceShell);
        Assert.Contains("printf '\\e[?1000l\\e[?1002l\\e[?1003l\\e[?1006l'", workspaceShell);
        Assert.Contains("stty sane || true", workspaceShell);
        Assert.Contains("trap cleanup_terminal_state EXIT", workspaceShell);
        Assert.Contains("screen-256color", screenConfig);
        Assert.Contains("defutf8 on", screenConfig);
    }

    [Fact]
    public void TerminalArtifactsGenerator_ForOracleWorkspace_UsesNormalOpenCodeLaunch()
    {
        var generator = new TerminalArtifactsGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
            Services = new List<string> { "oracle-demo" },
        };

        var workspaceShell = generator.GenerateOpencodeWorkspaceShellScript(definition);

        Assert.Contains("run_opencode opencode", workspaceShell);
        Assert.Contains("if run_opencode opencode --session \"$resume_session\"; then", workspaceShell);
        Assert.Contains("cleanup_terminal_state()", workspaceShell);
        Assert.DoesNotContain("oracle_prompt_file=", workspaceShell);
        Assert.DoesNotContain("opencode -p", workspaceShell);
        Assert.DoesNotContain("| opencode", workspaceShell);
    }

    [Fact]
    public void AttachArtifactsGenerator_UsesOpencodeUserAndCreatesSessionWhenMissing()
    {
        var generator = new AttachArtifactsGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "attach-demo" },
        };
        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "attach-demo"));
        var wrapper = generator.GenerateWindowsTerminalWrapper(definition, paths);

        Assert.Contains("$dockerExecArgs = @('exec', '-it', '--user', $attachUser, '-w', $workspaceDirectory", wrapper);
        Assert.Contains("Invoke-DockerCheck", wrapper);
        Assert.Contains("Test-AttachPreconditions", wrapper);
        Assert.Contains("@('exec', $containerName, 'test', '-x', $workspaceShellScript)", wrapper);
        Assert.Contains("Script not found", wrapper);
        Assert.Contains("User $attachUser does not exist.", wrapper);
        Assert.Contains("Working directory missing: $workspaceDirectory", wrapper);
        Assert.Contains("Script is not marked executable", wrapper);
        Assert.DoesNotContain("$scriptCheck.Output -notmatch", wrapper);
        Assert.Contains("$attemptedCommand = \"$dockerExe exec -it --user $attachUser -w $workspaceDirectory $containerName bash $workspaceShellScript\"", wrapper);
        Assert.Contains("$attachPrefix = '[attach:attach-demo]'", wrapper);
        Assert.Contains("Write-AttachMessage \"Expected container name: $containerName\"", wrapper);
        Assert.Contains("Write-AttachMessage \"Attempted command: $attemptedCommand\"", wrapper);
        Assert.Contains("Write-AttachMessage \"docker ps:\"", wrapper);
        Assert.Contains("Write-AttachMessage \"docker compose ps:\"", wrapper);
        Assert.Contains(paths.AttachDiagnosticsLogPath, wrapper);
        Assert.Contains(paths.ComposePath, wrapper);
        Assert.Contains("/opt/opencode-workspace/config/opencode-workspace-shell.sh", wrapper);
        Assert.Contains("$disableMouseReporting", wrapper);
        Assert.Contains("[Console]::Write($disableMouseReporting)", wrapper);

        var diagnostics = generator.GenerateTerminalDiagnosticsWrapper(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "attach-demo" },
        });

        Assert.Contains("[attach] Workspace: $workspaceName", diagnostics);
        Assert.Contains("[attach] User: opencode", diagnostics);
        Assert.Contains("[attach] Container: $containerName", diagnostics);
        Assert.Contains("UTF8: ✓ λ € — • │ ─  ", diagnostics);
    }

    [Fact]
    public void AttachPreflight_DoesNotReportFalsePrerequisiteFailures()
    {
        var generator = new AttachArtifactsGenerator();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "odip-analiza" },
        };
        var paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), "odip-analiza"));
        var wrapper = generator.GenerateWindowsTerminalWrapper(definition, paths);

        Assert.Contains("Write-AttachMessage \"Verified script exists: $workspaceShellScript\"", wrapper);
        Assert.Contains("@('exec', $containerName, 'test', '-x', $workspaceShellScript)", wrapper);
        Assert.Contains("Write-AttachMessage \"Verified script is executable: $workspaceShellScript\"", wrapper);
        Assert.Contains("Write-AttachMessage \"Verified user exists: $attachUser\"", wrapper);
        Assert.Contains("Write-AttachMessage \"Verified working directory exists: $workspaceDirectory\"", wrapper);
        Assert.Contains("Write-AttachMessage \"docker exec failed with exit code $ExitCode\"", wrapper);
        Assert.Contains("Write-AttachMessage \"Preflight checks passed.\"", wrapper);
        Assert.DoesNotContain("$scriptCheck.Output -notmatch", wrapper);
        Assert.DoesNotContain("Windows file attributes", wrapper, StringComparison.OrdinalIgnoreCase);
    }
}
