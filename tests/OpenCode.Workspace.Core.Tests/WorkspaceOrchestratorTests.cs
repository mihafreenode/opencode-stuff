using System.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceOrchestratorTests
{
    [Fact]
    public void CreateWorkspace_WritesCanonicalAndGeneratedFiles()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));

            Assert.True(File.Exists(snapshot.Paths.WorkspaceYamlPath));
            Assert.True(File.Exists(snapshot.Paths.ComposePath));
            Assert.True(File.Exists(snapshot.Paths.EnvironmentFilePath));
            Assert.True(File.Exists(snapshot.Paths.ProvisionScriptPath));
            Assert.True(File.Exists(snapshot.Paths.StarshipConfigPath));
            Assert.True(File.Exists(snapshot.Paths.ShellInitScriptPath));
            Assert.True(File.Exists(snapshot.Paths.OpencodeWorkspaceShellPath));
            Assert.True(File.Exists(snapshot.Paths.ScreenConfigPath));
            Assert.True(File.Exists(snapshot.Paths.AttachWrapperScriptPath));
            Assert.True(File.Exists(snapshot.Paths.TerminalDiagnosticsScriptPath));
            Assert.True(Directory.Exists(Path.Combine(tempRoot, ".git")));
            Assert.True(File.Exists(snapshot.Paths.TimelinePath));
            Assert.True(File.Exists(snapshot.Paths.CheckpointIndexPath));
            Assert.Contains("workspace-created", File.ReadAllText(snapshot.Paths.TimelinePath));
            Assert.Contains("GENERATED FILE", File.ReadAllText(snapshot.Paths.ComposePath));
            Assert.Contains("npm install -g opencode-ai", File.ReadAllText(snapshot.Paths.ProvisionScriptPath));
            Assert.Contains("/home/opencode/.local/share/opencode/log", File.ReadAllText(snapshot.Paths.ProvisionScriptPath));
            Assert.Contains("Initializing OpenCode user directories", File.ReadAllText(snapshot.Paths.OpencodeWorkspaceShellPath));
            Assert.Equal(WorkspaceSafetyLevel.AtRisk, snapshot.Safety.OverallStatus);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void CreateWorkspace_ForOracleDemo_WritesTutorialAndSkillFiles()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-demo-workspace", Image = "ubuntu:24.04" },
                Features = ["core", "oracle-demo"],
                Services = ["oracle-demo"],
            });

            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "oracle-demo.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "ORACLE-DEMO.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, ".opencode", "context", "oracle-demo.json")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "workspace-tutorial.json")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "init", "01-create-demo-user.sql")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "START-HERE-ORACLE.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "opencode-start.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "verify-oracle-demo.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "knowledge", "skills", "oracle-explain-procedure.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, ".local", "oracle", "network", "admin", "README.md")));

            var topLevelGuide = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "ORACLE-DEMO.md"));
            var agentContext = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, ".opencode", "context", "oracle-demo.json"));
            var startHere = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "START-HERE-ORACLE.md"));
            var openCodeStart = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "tutorial", "oracle", "opencode-start.md"));
            var verifyScript = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "scripts", "verify-oracle-demo.sh"));

            Assert.Contains("# Oracle Demo Connection", topLevelGuide);
            Assert.Contains("sqlplus -S demo_user/demo_password@//oracle-demo:1521/FREEPDB1", topLevelGuide);
            Assert.Contains("Run:", topLevelGuide);
            Assert.Contains("scripts/verify-oracle-demo.sh", topLevelGuide);
            Assert.Contains("Do not inspect `.env` for normal demo verification.", topLevelGuide);
            Assert.Contains("\"kind\": \"oracle-demo-connection\"", agentContext);
            Assert.Contains("\"connectString\": \"demo_user/demo_password@//oracle-demo:1521/FREEPDB1\"", agentContext);
            Assert.Contains("\"verifyScript\": \"scripts/verify-oracle-demo.sh\"", agentContext);
            Assert.Contains("demo_user/demo_password@//oracle-demo:1521/FREEPDB1", startHere);
            Assert.Contains("Before asking for connection details, read `ORACLE-DEMO.md` or `.opencode/context/oracle-demo.json`.", startHere);
            Assert.Contains("Use the known local demo connection. Do not ask for credentials.", startHere);
            Assert.Contains("scripts/verify-oracle-demo.sh", startHere);
            Assert.Contains("Start Oracle first", openCodeStart);
            Assert.Contains("Before asking for connection details, read `ORACLE-DEMO.md` or `.opencode/context/oracle-demo.json`.", openCodeStart);
            Assert.Contains("demo_user/demo_password@//oracle-demo:1521/FREEPDB1", openCodeStart);
            Assert.Contains("Do not ask for credentials", openCodeStart);
            Assert.Contains("Run scripts/verify-oracle-demo.sh", openCodeStart);
            Assert.Contains("sqlplus -S demo_user/demo_password@//oracle-demo:1521/FREEPDB1 <<'EOF'", verifyScript);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var mode = File.GetUnixFileMode(Path.Combine(snapshot.Paths.RootPath, "scripts", "verify-oracle-demo.sh"));
                Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
            }
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void CreateWorkspace_ForDocumentProcessing_WritesDocumentationGuidesAndScripts()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));

            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "docs", "documentation-features.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "DOCUMENTATION-FEATURES.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-documentation-tooling.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "scripts", "demo-documentation-workflows.sh")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "samples", "documentation", "report.md")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "samples", "documentation", "report.html")));
            Assert.True(File.Exists(Path.Combine(snapshot.Paths.RootPath, "samples", "documentation", "architecture.mmd")));

            var quickGuide = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "DOCUMENTATION-FEATURES.md"));
            var validationScript = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-documentation-tooling.sh"));
            var demoScript = File.ReadAllText(Path.Combine(snapshot.Paths.RootPath, "scripts", "demo-documentation-workflows.sh"));

            Assert.Contains("scripts/validate-documentation-tooling.sh", quickGuide);
            Assert.Contains("scripts/demo-documentation-workflows.sh", quickGuide);
            Assert.Contains("require_command pandoc", validationScript);
            Assert.Contains("require_command node", validationScript);
            Assert.Contains("node -e \"console.log(process.version)\"", validationScript);
            Assert.Contains("fc-list | sort", validationScript);
            Assert.Contains("fc-match Calibri", validationScript);
            Assert.Contains("pandoc \"${workspace_root}/samples/documentation/report.md\"", demoScript);
            Assert.Contains("weasyprint \"${workspace_root}/samples/documentation/report.html\"", demoScript);
            Assert.Contains("mmdc -p \"${output_dir}/mermaid-puppeteer.json\" -i \"${workspace_root}/samples/documentation/architecture.mmd\"", demoScript);
            Assert.Contains("pdfinfo \"${output_dir}/markdown-report.pdf\"", demoScript);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var validationMode = File.GetUnixFileMode(Path.Combine(snapshot.Paths.RootPath, "scripts", "validate-documentation-tooling.sh"));
                var demoMode = File.GetUnixFileMode(Path.Combine(snapshot.Paths.RootPath, "scripts", "demo-documentation-workflows.sh"));
                Assert.True(validationMode.HasFlag(UnixFileMode.UserExecute));
                Assert.True(demoMode.HasFlag(UnixFileMode.UserExecute));
            }
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void WriteAppliedState_WritesAppliedStateFile()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));

            orchestrator.WriteAppliedState(snapshot);

            Assert.True(File.Exists(snapshot.Paths.AppliedStatePath));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_AfterAppliedState_DoesNotRequireUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.False(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_AfterRuntimeOnlyReload_DoesNotRequireUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var reloaded = orchestrator.LoadSnapshot(tempRoot);
            var runtimeOnlySnapshot = new WorkspaceSnapshot
            {
                Record = reloaded.Record,
                Definition = reloaded.Definition,
                Paths = reloaded.Paths,
                RuntimeState = WorkspaceRuntimeState.Running,
                Safety = reloaded.Safety,
                Session = reloaded.Session,
                AppliedState = reloaded.AppliedState,
                UpdateRequired = reloaded.UpdateRequired,
            };

            Assert.False(runtimeOnlySnapshot.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenWorkspaceYamlChanges_RequiresUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            File.WriteAllText(snapshot.Paths.WorkspaceYamlPath, File.ReadAllText(snapshot.Paths.WorkspaceYamlPath).Replace("ubuntu:24.04", "ubuntu:22.04", StringComparison.Ordinal));

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenSelectedFeaturesChange_RequiresUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            orchestrator.WriteAppliedState(snapshot);

            var updatedDefinition = CreateDefinition("core", "document-processing");
            File.WriteAllText(snapshot.Paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(updatedDefinition));

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenRelevantCatalogPlanChanges_RequiresUpdate()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var originalResolver = CreateResolver();
            var originalOrchestrator = CreateOrchestrator(tempRoot, originalResolver);
            var snapshot = originalOrchestrator.CreateWorkspace(tempRoot, CreateDefinition("core", "document-processing"));
            originalOrchestrator.WriteAppliedState(snapshot);

            var changedResolver = CreateResolver(additionalDocumentProcessingAptPackage: "tesseract-ocr");
            var changedOrchestrator = CreateOrchestrator(tempRoot, changedResolver);

            var reloaded = changedOrchestrator.LoadSnapshot(tempRoot);

            Assert.True(reloaded.UpdateRequired);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void CreatePermissionRepairArguments_UsesHelperContainerAndTargetMount()
    {
        var arguments = DockerService.CreatePermissionRepairArguments("C:\\Workspaces\\Demo");

        Assert.Equal("run", arguments[0]);
        Assert.Contains("ubuntu:24.04", arguments);
        Assert.Contains("C:\\Workspaces\\Demo:/target", arguments);
        Assert.Contains("chmod -R u+rwX,go+rwX /target || true", arguments[^1]);
    }

    [Fact]
    public void OpenFolderAsWorkspace_InitializesGitAndCreatesWorkspaceWithoutBlockingSavePoint()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "notes.txt"), "draft notes");
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());

            var snapshot = orchestrator.OpenFolderAsWorkspace(tempRoot);

            Assert.True(File.Exists(snapshot.Paths.WorkspaceYamlPath));
            Assert.True(Directory.Exists(Path.Combine(tempRoot, ".git")));
            Assert.Null(snapshot.Safety.LocalRecovery.LatestSavePointUtc);
            Assert.Equal(WorkspaceSafetyLevel.AtRisk, snapshot.Safety.OverallStatus);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task PublishAsync_RecordsBlockedPublishInTimeline()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var timelineService = new WorkspaceTimelineService();
            var orchestrator = CreateOrchestratorWithProvider(tempRoot, CreateResolver(), new FakeWorkspaceProvider(), timelineService);
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));

            var review = await orchestrator.PublishAsync(snapshot);
            var timeline = timelineService.Load(snapshot.Paths.TimelinePath);

            Assert.True(review.IsBlocked);
            Assert.Contains(timeline.Events, item => item.Type == "publish-attempted");
            Assert.Contains(timeline.Events, item => item.Type == "publish-blocked");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenUnknownHiddenFolderExists_ReturnsNeedsReview()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            Directory.CreateDirectory(Path.Combine(tempRoot, ".foo"));
            File.WriteAllText(Path.Combine(tempRoot, ".foo", "state.json"), "{}");

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.Equal(WorkspaceSafetyLevel.NeedsReview, reloaded.Safety.OverallStatus);
            Assert.True(reloaded.Safety.IgnorePolicy.HasUnknownHiddenFolders);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenSecretCandidateExists_ReturnsAtRisk()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            File.WriteAllText(Path.Combine(tempRoot, ".env"), "API_KEY=secret");

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.Equal(WorkspaceSafetyLevel.AtRisk, reloaded.Safety.OverallStatus);
            Assert.True(reloaded.Safety.IgnorePolicy.HasSecretCandidates);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void LoadSnapshot_WhenTimelineIsIgnored_ReturnsNeedsReview()
    {
        Assert.True(CanRunGit(), "Git is required for workspace persistence tests.");
        var tempRoot = CreateTempRoot();

        try
        {
            var orchestrator = CreateOrchestrator(tempRoot, CreateResolver());
            orchestrator.CreateWorkspace(tempRoot, CreateDefinition("core"));
            File.AppendAllText(Path.Combine(tempRoot, ".gitignore"), "history/*.yaml\n");

            var reloaded = orchestrator.LoadSnapshot(tempRoot);

            Assert.Equal(WorkspaceSafetyLevel.NeedsReview, reloaded.Safety.OverallStatus);
            Assert.True(reloaded.Safety.IgnorePolicy.HasDurableIgnoreConflicts);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task ManagedWorkspaceOperations_RegenerateStaleComposeBeforeDockerComposeDownAndUp()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var dockerRunner = new ComposeGuardProcessRunner();
            var orchestrator = CreateOrchestratorWithProviderAndDocker(tempRoot, CreateResolver(), new FakeWorkspaceProvider(), new DockerService(dockerRunner), new WorkspaceTimelineService());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateAnalizaDefinition());
            var logEntries = new List<CommandLogEntry>();

            WriteInvalidWorkspaceDependsOnCompose(snapshot.Paths.ComposePath);
            Assert.Contains("condition: service_healthy", File.ReadAllText(snapshot.Paths.ComposePath));

            await orchestrator.RemoveDockerResourcesAsync(snapshot, entry => logEntries.Add(entry));

            var repairedAfterRemove = File.ReadAllText(snapshot.Paths.ComposePath);
            Assert.DoesNotContain("condition: service_healthy", repairedAfterRemove);
            Assert.DoesNotContain("oracle:", repairedAfterRemove);
            Assert.DoesNotContain("depends_on:", repairedAfterRemove);

            WriteInvalidWorkspaceDependsOnCompose(snapshot.Paths.ComposePath);
            Assert.Contains("condition: service_healthy", File.ReadAllText(snapshot.Paths.ComposePath));

            await orchestrator.StartAsync(snapshot, entry => logEntries.Add(entry));

            var repairedAfterStart = File.ReadAllText(snapshot.Paths.ComposePath);
            Assert.DoesNotContain("condition: service_healthy", repairedAfterStart);
            Assert.DoesNotContain("oracle:", repairedAfterStart);
            Assert.DoesNotContain("depends_on:", repairedAfterStart);
            Assert.Contains(logEntries, entry => entry.Message.Contains("Stale compose detected for this managed workspace.", StringComparison.Ordinal));
            Assert.Contains(logEntries, entry => entry.Message.Contains("Compose regenerated/repaired.", StringComparison.Ordinal));
            Assert.True(logEntries.Count(entry => entry.Message.Contains("Regenerated stale compose.yaml before Docker operation", StringComparison.Ordinal)) >= 2);
            Assert.DoesNotContain(logEntries, entry => entry.Message.Contains("Docker Compose validation failed.", StringComparison.Ordinal));
            Assert.Contains(dockerRunner.Commands, command => command.Contains(" compose ", StringComparison.Ordinal) && command.Contains(" config", StringComparison.Ordinal));
            Assert.Contains(dockerRunner.Commands, command => command.Contains(" down ", StringComparison.Ordinal));
            Assert.Contains(dockerRunner.Commands, command => command.Contains(" up -d", StringComparison.Ordinal));
            Assert.Equal(0, dockerRunner.StaleComposeObservedCount);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task RecoverWorkspace_RegeneratesStaleComposeBeforeDockerComposeConfig()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var dockerRunner = new ComposeGuardProcessRunner();
            var orchestrator = CreateOrchestratorWithProviderAndDocker(tempRoot, CreateResolver(), new FakeWorkspaceProvider(), new DockerService(dockerRunner), new WorkspaceTimelineService());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateAnalizaDefinition());
            var logEntries = new List<CommandLogEntry>();

            WriteInvalidWorkspaceDependsOnCompose(snapshot.Paths.ComposePath);
            Assert.Contains("condition: service_healthy", File.ReadAllText(snapshot.Paths.ComposePath));

            await orchestrator.RecoverAsync(snapshot, entry => logEntries.Add(entry));

            var repairedCompose = File.ReadAllText(snapshot.Paths.ComposePath);
            Assert.DoesNotContain("condition: service_healthy", repairedCompose);
            Assert.DoesNotContain("oracle:", repairedCompose);
            Assert.DoesNotContain("depends_on:", repairedCompose);
            Assert.Contains(logEntries, entry => entry.Message.Contains("Compose regenerated/repaired.", StringComparison.Ordinal));
            Assert.Contains(logEntries, entry => entry.Message.Contains("Regenerated stale compose.yaml before Docker operation.", StringComparison.Ordinal));
            Assert.Contains(dockerRunner.Commands, command => command.Contains(" config", StringComparison.Ordinal));
            Assert.Equal(0, dockerRunner.StaleComposeObservedCount);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task RecoverWorkspace_RegeneratesAttachScriptFromFixedTemplate()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var dockerRunner = new ComposeGuardProcessRunner();
            var orchestrator = CreateOrchestratorWithProviderAndDocker(tempRoot, CreateResolver(), new FakeWorkspaceProvider(), new DockerService(dockerRunner), new WorkspaceTimelineService());
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateAnalizaDefinition());

            var createdScript = File.ReadAllText(snapshot.Paths.OpencodeWorkspaceShellPath);
            Assert.Contains("set -euo pipefail", createdScript);
            Assert.DoesNotContain("Failed at line $LINENO", createdScript, StringComparison.Ordinal);
            var createdGuardIndex = createdScript.IndexOf("if [ -d /opt/oracle/instantclient ]; then", StringComparison.Ordinal);
            var createdProbeIndex = createdScript.IndexOf("oracle_client_home=$(find /opt/oracle/instantclient", StringComparison.Ordinal);
            Assert.True(createdGuardIndex >= 0 && createdProbeIndex > createdGuardIndex);

            var userOwnedFilePath = Path.Combine(snapshot.Paths.RootPath, "notes.txt");
            const string userOwnedContents = "keep me";
            File.WriteAllText(userOwnedFilePath, userOwnedContents);

            File.WriteAllText(snapshot.Paths.OpencodeWorkspaceShellPath, string.Join("\n", new[]
            {
                "#!/usr/bin/env bash",
                "set -euo pipefail",
                "oracle_client_home=$(find /opt/oracle/instantclient -maxdepth 2 -type f -name 'libsqlplus.so' -printf '%h\\n' 2>/dev/null | while read -r dir; do if ls \"$dir\"/libclntsh.so* >/dev/null 2>&1; then printf '%s\\n' \"$dir\"; break; fi; done)",
                string.Empty,
            }));

            await orchestrator.RecoverAsync(snapshot);

            var regeneratedScript = File.ReadAllText(snapshot.Paths.OpencodeWorkspaceShellPath);
            Assert.Contains("set -euo pipefail", regeneratedScript);
            Assert.DoesNotContain("Failed at line $LINENO", regeneratedScript, StringComparison.Ordinal);
            var guardIndex = regeneratedScript.IndexOf("if [ -d /opt/oracle/instantclient ]; then", StringComparison.Ordinal);
            var probeIndex = regeneratedScript.IndexOf("oracle_client_home=$(find /opt/oracle/instantclient", StringComparison.Ordinal);
            Assert.True(guardIndex >= 0 && probeIndex > guardIndex);
            Assert.NotEqual(string.Join("\n", new[]
            {
                "#!/usr/bin/env bash",
                "set -euo pipefail",
                "oracle_client_home=$(find /opt/oracle/instantclient -maxdepth 2 -type f -name 'libsqlplus.so' -printf '%h\\n' 2>/dev/null | while read -r dir; do if ls \"$dir\"/libclntsh.so* >/dev/null 2>&1; then printf '%s\\n' \"$dir\"; break; fi; done)",
                string.Empty,
            }), regeneratedScript);
            Assert.Equal(userOwnedContents, File.ReadAllText(userOwnedFilePath));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task AttachAsync_WhenWorkspaceContainerIsNotRunning_DoesNotLaunchTerminal()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var terminalLauncher = new RecordingTerminalLauncher();
            var dockerRunner = new MissingContainerProcessRunner();
            var orchestrator = CreateOrchestratorWithProviderAndDocker(
                tempRoot,
                CreateResolver(),
                new FakeWorkspaceProvider(),
                new DockerService(dockerRunner),
                new WorkspaceTimelineService(),
                terminalLauncher: terminalLauncher);
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateAnalizaDefinition());
            var logEntries = new List<CommandLogEntry>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.AttachAsync(snapshot, entry => logEntries.Add(entry)));

            Assert.Contains("Container 'odip-analiza-workspace' is not running", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, terminalLauncher.LaunchCount);
            Assert.Contains(logEntries, entry => entry.Message.Contains("Checking for the expected workspace container", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public async Task LaunchAttachForRunningWorkspaceAsync_WhenOpencodeUserIsMissing_ProvisionsBeforeAttachInitialization()
    {
        var tempRoot = CreateTempRoot();

        try
        {
            var terminalLauncher = new RecordingTerminalLauncher();
            var dockerRunner = new ProvisionBeforeAttachProcessRunner();
            var orchestrator = CreateOrchestratorWithProviderAndDocker(
                tempRoot,
                CreateResolver(),
                new FakeWorkspaceProvider(),
                new DockerService(dockerRunner),
                new WorkspaceTimelineService(),
                terminalLauncher: terminalLauncher);
            var snapshot = orchestrator.CreateWorkspace(tempRoot, CreateAnalizaDefinition());
            var logEntries = new List<CommandLogEntry>();

            await orchestrator.LaunchAttachForRunningWorkspaceAsync(snapshot, entry => logEntries.Add(entry));

            Assert.True(dockerRunner.ProvisioningRan);
            Assert.True(dockerRunner.DirectoryInitializationRanAfterProvisioning);
            Assert.Equal(1, terminalLauncher.LaunchCount);
            Assert.Contains(logEntries, entry => entry.Message.Contains("Workspace container is running but not provisioned. Running provisioning before attach.", StringComparison.Ordinal));
            Assert.DoesNotContain(logEntries, entry => entry.Message.Contains("Run provisioning/recover workspace", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static WorkspaceDefinition CreateDefinition(params string[] features)
    {
        return new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Name = "smoke-workspace",
                Image = "ubuntu:24.04",
            },
            Features = features.ToList(),
            Services = new List<string> { "postgres", "pgadmin" },
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };
    }

    private static WorkspaceDefinition CreateAnalizaDefinition()
    {
        return new WorkspaceDefinition
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
                Node = 22,
            },
            Features = new List<string> { "core", "document-processing", "ocr-processing", "spellcheck" },
            Services = new List<string>(),
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };
    }

    private static void WriteInvalidWorkspaceDependsOnCompose(string composePath)
    {
        File.WriteAllText(composePath, string.Join("\n", new[]
        {
            "services:",
            "  workspace:",
            "    image: ubuntu:24.04",
            "    depends_on:",
            "      oracle:",
            "        condition: service_healthy",
            string.Empty,
        }));
    }

    private static WorkspaceResolver CreateResolver(string? additionalDocumentProcessingAptPackage = null)
    {
        var documentPackages = new List<string>
        {
            "pandoc",
            "poppler-utils",
            "graphviz",
            "plantuml",
            "libreoffice",
            "fonts-dejavu",
            "fonts-liberation",
            "fonts-crosextra-carlito",
            "fonts-crosextra-caladea",
            "fonts-noto",
            "fonts-noto-cjk",
            "fonts-noto-extra",
            "fonts-noto-color-emoji",
            "fonts-roboto",
            "fonts-inter",
            "fonts-firacode",
            "fonts-jetbrains-mono",
        };
        if (!string.IsNullOrWhiteSpace(additionalDocumentProcessingAptPackage))
        {
            documentPackages.Add(additionalDocumentProcessingAptPackage);
        }

        return new WorkspaceResolver(
            new[]
            {
                new FeatureManifest
                {
                    Id = "core",
                    AlwaysEnabled = true,
                    Dependencies = new DependencySet { Apt = new List<string> { "git", "curl" } },
                },
                new FeatureManifest
                {
                    Id = "document-processing",
                    Dependencies = new DependencySet
                    {
                        Apt = documentPackages,
                        Npm = new List<string> { "playwright", "@mermaid-js/mermaid-cli" },
                        Pip = new List<string> { "weasyprint", "markdown-it-py", "pypdf", "pymupdf", "reportlab" },
                    },
                    PostInstall =
                    [
                        "command -v typst >/dev/null 2>&1 || install /tmp/typst-install/typst-*/typst /usr/local/bin/typst",
                        "playwright install chromium",
                        "fc-cache -fv",
                    ],
                },
                new FeatureManifest
                {
                    Id = "ocr-processing",
                    Dependencies = new DependencySet(),
                },
                new FeatureManifest
                {
                    Id = "spellcheck",
                    Dependencies = new DependencySet(),
                },
                new FeatureManifest
                {
                    Id = "oracle-demo",
                    Dependencies = new DependencySet { Apt = new List<string> { "curl", "unzip" } },
                },
            },
            new[]
            {
                new ServiceManifest
                {
                    Id = "postgres",
                    Image = "postgres:17",
                    HostPorts = new List<string> { "15432:5432" },
                    Volumes = new List<string> { "postgres-data:/var/lib/postgresql/data" },
                },
                new ServiceManifest
                {
                    Id = "pgadmin",
                    Image = "dpage/pgadmin4:9",
                    HostPorts = new List<string> { "18080:80" },
                    DependsOn = new List<string> { "postgres" },
                },
                new ServiceManifest
                {
                    Id = "oracle-demo",
                    Image = "gvenzl/oracle-free:23-slim-faststart",
                    HostPorts = new List<string> { "1521:1521" },
                    Profiles = new List<string> { "oracle-demo" },
                    WorkspaceDependsOnCondition = "service_healthy",
                    Volumes = new List<string> { "oracle-demo-data:/opt/oracle/oradata", "${WORKSPACE_TUTORIAL_DOCKER_PATH}/oracle/init:/container-entrypoint-initdb.d" },
                },
            });
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string tempRoot, WorkspaceResolver resolver)
    {
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        return CreateOrchestratorWithProvider(tempRoot, resolver, new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService), new WorkspaceTimelineService(), ignorePolicyService);
    }

    private static WorkspaceOrchestrator CreateOrchestratorWithProviderAndDocker(string tempRoot, WorkspaceResolver resolver, IWorkspaceProvider provider, DockerService dockerService, WorkspaceTimelineService timelineService, WorkspaceIgnorePolicyService? ignorePolicyService = null, ITerminalLauncher? terminalLauncher = null)
    {
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceRepository(GetAppDataRoot(tempRoot)),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            timelineService,
            new WorkspaceSafetyService(),
            ignorePolicyService ?? new WorkspaceIgnorePolicyService(),
            provider,
            dockerService,
            terminalLauncher ?? new NoOpTerminalLauncher());
    }

    private static WorkspaceOrchestrator CreateOrchestratorWithProvider(string tempRoot, WorkspaceResolver resolver, IWorkspaceProvider provider, WorkspaceTimelineService timelineService, WorkspaceIgnorePolicyService? ignorePolicyService = null)
    {
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceRepository(GetAppDataRoot(tempRoot)),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            timelineService,
            new WorkspaceSafetyService(),
            ignorePolicyService ?? new WorkspaceIgnorePolicyService(),
            provider,
            new DockerService(new ProcessRunner()),
            new NoOpTerminalLauncher());
    }

    private static string CreateTempRoot() => Path.Combine(Path.GetTempPath(), $"opencode-workspace-manager-{Guid.NewGuid():N}");

    private static void DeleteTempRoot(string tempRoot)
    {
        var appDataRoot = GetAppDataRoot(tempRoot);
        if (Directory.Exists(tempRoot))
        {
            TestFileSystem.DeleteDirectoryIfExists(tempRoot);
        }

        if (Directory.Exists(appDataRoot))
        {
            TestFileSystem.DeleteDirectoryIfExists(appDataRoot);
        }
    }

    private static string GetAppDataRoot(string tempRoot)
        => Path.Combine(Path.GetDirectoryName(tempRoot) ?? Path.GetTempPath(), $"{Path.GetFileName(tempRoot)}-appdata");

    private static bool CanRunGit()
    {
        try
        {
            using var process = Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(5000);
            return process is not null && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingTerminalLauncher : ITerminalLauncher
    {
        public int LaunchCount { get; private set; }

        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            LaunchCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWorkspaceProvider : IWorkspaceProvider
    {
        public string Type => "git";

        public Task InitializeWorkspaceAsync(WorkspacePaths paths, WorkspaceDefinition definition, bool createInitialSavePoint, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<WorkspaceGitState> GetGitStateAsync(WorkspacePaths paths, WorkspaceDefinition definition, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkspaceGitState
            {
                IsRepository = true,
                WorkingCopyName = "users/test/demo-20260613-1542",
                CurrentBranch = "users/test/demo-20260613-1542",
                LatestCommitSha = "abc123",
                LatestCommitUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                IsSafeWorkingCopy = true,
                StatusSummary = "clean",
            });
        }

        public Task<bool> CreateSavePointAsync(WorkspacePaths paths, WorkspaceDefinition definition, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<WorkspacePublishReview> PublishAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkspacePublishReview
            {
                IsBlocked = true,
                Message = "Your local work is safe. The remote workspace changed and needs review before publishing.",
                WorkingCopyName = "users/test/demo-20260613-1542",
                RemoteName = "origin",
                RemoteBranch = "origin/users/test/demo-20260613-1542",
                AheadCount = 1,
                BehindCount = 1,
                LatestCommitSha = "abc123",
                LatestSavePointUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
        }

        public Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Updated." });

        public Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspacePublishReview { Message = "Published review Working Copy." });

        public Task<string> ExportPatchAsync(WorkspacePaths paths, WorkspaceDefinition definition, string outputPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(outputPath);
    }

    private sealed class ComposeGuardProcessRunner : IProcessRunner
    {
        public List<string> Commands { get; } = new();

        public int StaleComposeObservedCount { get; private set; }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var argumentList = arguments.ToList();
            var command = string.Join(' ', new[] { fileName }.Concat(argumentList));
            Commands.Add(command);

            if (string.Equals(fileName, "docker", StringComparison.OrdinalIgnoreCase))
            {
                var composePath = TryGetComposePath(argumentList);
                if (composePath is not null && File.Exists(composePath))
                {
                    var compose = File.ReadAllText(composePath);
                    if (compose.Contains("condition: service_healthy", StringComparison.Ordinal)
                        || compose.Contains("      oracle:", StringComparison.Ordinal))
                    {
                        StaleComposeObservedCount++;
                        return Task.FromResult(CreateResult(command, 1, standardError: "stale invalid compose.yaml reached DockerService"));
                    }
                }

                if (argumentList.Contains("ps") && argumentList.Contains("--status"))
                {
                    return Task.FromResult(CreateResult(command, 0, standardOutput: "workspace"));
                }

                if (argumentList.Count > 0 && argumentList[0] == "ps")
                {
                    return Task.FromResult(CreateResult(command, 0, standardOutput: "odip-analiza-workspace"));
                }

                return Task.FromResult(CreateResult(command, 0));
            }

            return Task.FromResult(CreateResult(command, 0));
        }

        private static string? TryGetComposePath(IReadOnlyList<string> argumentList)
        {
            for (var index = 0; index < argumentList.Count - 1; index++)
            {
                if (string.Equals(argumentList[index], "--file", StringComparison.Ordinal))
                {
                    return argumentList[index + 1];
                }
            }

            return null;
        }

        private static ProcessResult CreateResult(string command, int exitCode, string standardOutput = "", string standardError = "")
        {
            return new ProcessResult
            {
                Command = command,
                ExitCode = exitCode,
                StandardOutput = standardOutput,
                StandardError = standardError,
                StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? Array.Empty<string>() : standardOutput.Split(Environment.NewLine),
                StandardErrorLines = string.IsNullOrWhiteSpace(standardError) ? Array.Empty<string>() : standardError.Split(Environment.NewLine),
                Duration = TimeSpan.FromMilliseconds(10),
            };
        }
    }

    private sealed class MissingContainerProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var argumentList = arguments.ToList();
            var command = string.Join(' ', new[] { fileName }.Concat(argumentList));

            if (string.Equals(fileName, "docker", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Contains("config", StringComparer.Ordinal))
                {
                    return Task.FromResult(CreateResult(command, 0));
                }

                if (argumentList.Contains("up", StringComparer.Ordinal))
                {
                    return Task.FromResult(CreateResult(command, 0));
                }

                if (argumentList.Contains("ps", StringComparer.Ordinal) && argumentList.Contains("--services", StringComparer.Ordinal))
                {
                    return Task.FromResult(CreateResult(command, 0, standardOutput: "workspace"));
                }

                if (argumentList.Count > 0 && argumentList[0] == "ps")
                {
                    return Task.FromResult(CreateResult(command, 0, standardOutput: string.Empty));
                }
            }

            return Task.FromResult(CreateResult(command, 0));
        }
    }

    private sealed class ProvisionBeforeAttachProcessRunner : IProcessRunner
    {
        public bool ProvisioningRan { get; private set; }

        public bool DirectoryInitializationRanAfterProvisioning { get; private set; }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var argumentList = arguments.ToList();
            var command = string.Join(' ', new[] { fileName }.Concat(argumentList));

            if (string.Equals(fileName, "docker", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Contains("ps", StringComparer.Ordinal) && argumentList.Contains("--services", StringComparer.Ordinal))
                {
                    return Task.FromResult(CreateResult(command, 0, standardOutput: "workspace"));
                }

                if (argumentList.Count > 0 && argumentList[0] == "ps")
                {
                    return Task.FromResult(CreateResult(command, 0, standardOutput: "odip-analiza-workspace"));
                }

                if (argumentList.Count >= 4 && argumentList[0] == "exec" && argumentList[^2] == "id" && argumentList[^1] == "opencode")
                {
                    return Task.FromResult(ProvisioningRan
                        ? CreateResult(command, 0, standardOutput: "uid=1001(opencode) gid=1001(opencode) groups=1001(opencode)")
                        : CreateResult(command, 1, standardError: "id: 'opencode': no such user"));
                }

                if (argumentList.Count >= 4 && argumentList[0] == "exec" && argumentList[2] == "bash" && argumentList[3] == "/opt/opencode-workspace/config/provision.sh")
                {
                    ProvisioningRan = true;
                    return Task.FromResult(CreateResult(command, 0));
                }

                if (argumentList.Count >= 5 && argumentList[0] == "exec" && argumentList[2] == "bash" && argumentList[3] == "-lc")
                {
                    DirectoryInitializationRanAfterProvisioning = ProvisioningRan;
                    return Task.FromResult(ProvisioningRan
                        ? CreateResult(command, 0)
                        : CreateResult(command, 1, standardError: "Workspace container is running but not provisioned. Run provisioning/recover workspace."));
                }

                if (argumentList.Contains("config", StringComparer.Ordinal) || argumentList.Contains("up", StringComparer.Ordinal))
                {
                    return Task.FromResult(CreateResult(command, 0));
                }
            }

            return Task.FromResult(CreateResult(command, 0));
        }
    }

    private static ProcessResult CreateResult(string command, int exitCode, string standardOutput = "", string standardError = "")
    {
        return new ProcessResult
        {
            Command = command,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            StandardOutputLines = string.IsNullOrWhiteSpace(standardOutput) ? Array.Empty<string>() : standardOutput.Split(Environment.NewLine),
            StandardErrorLines = string.IsNullOrWhiteSpace(standardError) ? Array.Empty<string>() : standardError.Split(Environment.NewLine),
            Duration = TimeSpan.FromMilliseconds(10),
        };
    }
}
