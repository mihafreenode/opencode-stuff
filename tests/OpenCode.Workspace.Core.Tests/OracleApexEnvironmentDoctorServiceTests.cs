using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexEnvironmentDoctorServiceTests
{
    [Fact]
    public async Task DiagnoseAsync_FailsWhenDeploymentProfileMissing()
    {
        var root = CreateTempRoot();
        try
        {
            WriteWorkspace(root, deploymentProfile: null);
            var service = new OracleApexEnvironmentDoctorService(processRunner: new FakeProcessRunner());

            var result = await service.DiagnoseAsync(new OracleApexDevelopmentEnvironmentConfiguration
            {
                WorkspaceRoot = root,
                EnvironmentName = "dev",
                SqlclProfile = "local-apex-dev",
                ApplicationId = 100,
                SourcePath = "src/apex",
                DeploymentProfile = "development",
                BuilderUrl = "https://example.test/ords/r/apex/app-builder/home",
                ApplicationUrl = "https://example.test/ords/r/demo/home",
            });

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Checks, check => check.Name == "Deployment profile" && check.Severity == DiagnosticSeverity.Error);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task DiagnoseAsync_WarnsWhenAtlasStateMissing()
    {
        var root = CreateTempRoot();
        try
        {
            WriteWorkspace(root, deploymentProfile: "development", withAtlas: false);
            var service = new OracleApexEnvironmentDoctorService(processRunner: new FakeProcessRunner());

            var result = await service.DiagnoseAsync(new OracleApexDevelopmentEnvironmentConfiguration
            {
                WorkspaceRoot = root,
                EnvironmentName = "dev",
                SqlclProfile = "local-apex-dev",
                ApplicationId = 100,
                SourcePath = "src/apex",
                DeploymentProfile = "development",
                BuilderUrl = "https://example.test/ords/r/apex/app-builder/home",
                ApplicationUrl = "https://example.test/ords/r/demo/home",
            });

            Assert.True(result.HasWarnings);
            Assert.Contains(result.Checks, check => check.Name == "Atlas catalog" && check.Severity == DiagnosticSeverity.Warning);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task DiagnoseAsync_SucceedsForReadyWorkspace()
    {
        var root = CreateTempRoot();
        try
        {
            WriteWorkspace(root, deploymentProfile: "development", withAtlas: true);
            var service = new OracleApexEnvironmentDoctorService(processRunner: new FakeProcessRunner());

            var result = await service.DiagnoseAsync(new OracleApexDevelopmentEnvironmentConfiguration
            {
                WorkspaceRoot = root,
                EnvironmentName = "dev",
                SqlclProfile = "local-apex-dev",
                ApplicationId = 100,
                SourcePath = "src/apex",
                DeploymentProfile = "development",
                BuilderUrl = "https://example.test/ords/r/apex/app-builder/home",
                ApplicationUrl = "https://example.test/ords/r/demo/home",
            });

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(result.Checks, check => check.Severity == DiagnosticSeverity.Error);
        }
        finally { DeleteTempRoot(root); }
    }

    private static void WriteWorkspace(string root, string? deploymentProfile, bool withAtlas = true)
    {
        Directory.CreateDirectory(root);
        var sourceRoot = Path.Combine(root, "src", "apex");
        Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(Path.Combine(sourceRoot, "application.apx"), "application demo (\n id: 100\n name: Demo\n alias: DEMO\n)\n");
        if (!string.IsNullOrWhiteSpace(deploymentProfile))
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "deployments"));
            File.WriteAllText(Path.Combine(sourceRoot, "deployments", $"{deploymentProfile}.apx"), $"deployment {deploymentProfile} (\n workspace: TEST\n parsing-schema: TESTSCHEMA\n application-id: 100\n)\n");
        }

        if (withAtlas)
        {
            var atlasPath = Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas");
            Directory.CreateDirectory(atlasPath);
            File.WriteAllText(Path.Combine(atlasPath, "state.json"), "{}\n");
        }

        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-apexlang", Image = "ubuntu:24.04" },
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = new() { Workspace = "TEST", ParsingSchema = "TESTSCHEMA", ApplicationId = 100, SqlclProfile = "local-apex-dev", SourcePath = "src/apex", DeploymentProfile = deploymentProfile },
                    },
                },
            },
        };
        File.WriteAllText(Path.Combine(root, "workspace.yaml"), new WorkspaceYamlService().Write(definition));
        Directory.CreateDirectory(Path.Combine(root, ".opencode"));
        new WorkspaceSynchronizationStateService().Write(WorkspacePathBuilder.Build(root).ApexMetadataPath, new WorkspaceSynchronizationStateDocument
        {
            DefaultEnvironment = "dev",
            Environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState> { ["dev"] = new() { SynchronizationState = nameof(WorkspaceSynchronizationState.InSync) } },
        });
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oracle-apex-doctor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            TestFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var command = string.Join(" ", arguments);
            return Task.FromResult(new ProcessResult
            {
                Command = $"{fileName} {command}".Trim(),
                ExitCode = 0,
                StandardOutput = "SQLcl: Release 24.1\n",
                StandardError = string.Empty,
                StandardOutputLines = ["SQLcl: Release 24.1"],
                StandardErrorLines = Array.Empty<string>(),
                Duration = TimeSpan.Zero,
            });
        }
    }
}
