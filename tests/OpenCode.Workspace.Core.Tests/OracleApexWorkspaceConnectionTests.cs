using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleApexWorkspaceConnectionTests
{
    [Fact]
    public async Task ConnectExistingApplication_PersistsWorkspaceConfig_InitializesSyncState_AndExportsSource()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateDefinition("oracle-apexlang-demo")));
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateDefinition("oracle-apexlang-demo"));

            var result = await provider.ConnectExistingApplicationAsync(new OracleApexConnectExistingApplicationRequest
            {
                Snapshot = snapshot,
                EnvironmentName = "dev",
                WorkspaceName = "TEST",
                ParsingSchema = "TESTSCHEMA",
                ApplicationId = 100,
                ApplicationName = "Customer Orders Demo",
                Alias = "customer-orders-demo",
                SqlclProfile = "local-apex-dev",
                SourcePath = "src/apex",
            });

            var updatedDefinition = new WorkspaceYamlService().Read(paths.WorkspaceYamlPath);
            var syncState = new WorkspaceSynchronizationStateService().Read(paths.ApexMetadataPath);

            Assert.Equal("dev", updatedDefinition.Oracle.Apex.DefaultEnvironment);
            Assert.True(updatedDefinition.Oracle.Apex.Environments.ContainsKey("dev"));
            Assert.Equal(100, updatedDefinition.Oracle.Apex.Environments["dev"].ApplicationId);
            Assert.Equal("TEST", updatedDefinition.Oracle.Apex.Environments["dev"].Workspace);
            Assert.Equal("TESTSCHEMA", updatedDefinition.Oracle.Apex.Environments["dev"].ParsingSchema);
            Assert.Equal("local-apex-dev", updatedDefinition.Oracle.Apex.Environments["dev"].SqlclProfile);
            Assert.Equal("src/apex", updatedDefinition.Oracle.Apex.Environments["dev"].SourcePath);

            Assert.NotNull(syncState);
            Assert.Equal("dev", syncState!.DefaultEnvironment);
            Assert.True(syncState.Environments.ContainsKey("dev"));
            Assert.NotNull(syncState.Environments["dev"].LastExport);
            Assert.NotNull(syncState.Environments["dev"].LastValidation);
            Assert.Equal(nameof(WorkspaceSynchronizationState.InSync), syncState.Environments["dev"].SynchronizationState);
            Assert.DoesNotContain(syncState.Environments["dev"].OperationHistory, item => item.Operation == "Pull");
            Assert.False(string.IsNullOrWhiteSpace(syncState.Environments["dev"].SynchronizedSourceSignature));

            Assert.True(File.Exists(Path.Combine(root, "src", "apex", "application.apx")));
            Assert.True(File.Exists(Path.Combine(root, ".opencode", "knowledge", "apexlang-atlas", "atlas.json")));
            Assert.True(File.Exists(Path.Combine(root, "docs", "oracle-apex-atlas.md")));
            Assert.Contains("Connected Oracle APEX application 'Customer Orders Demo'", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Theory]
    [InlineData("same-source", "same-source", WorkspaceSynchronizationState.InSync)]
    [InlineData("local-change", "same-source", WorkspaceSynchronizationState.GitAhead)]
    [InlineData("same-source", "remote-change", WorkspaceSynchronizationState.DeploymentAhead)]
    [InlineData("local-change", "remote-change", WorkspaceSynchronizationState.Diverged)]
    public async Task Validate_ComputesExpectedDriftState(string sourceContent, string remoteContent, WorkspaceSynchronizationState expectedState)
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            File.WriteAllText(Path.Combine(sourcePath, "readme.txt"), "exported");
            CreateDeploymentProfile(sourcePath, "development");
            var baselineSignature = ComputeDirectorySignature(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), sourceContent);

            new WorkspaceSynchronizationStateService().Write(paths.ApexMetadataPath, new WorkspaceSynchronizationStateDocument
            {
                DefaultEnvironment = "dev",
                Environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>
                {
                    ["dev"] = new()
                    {
                        SynchronizationState = nameof(WorkspaceSynchronizationState.InSync),
                        SynchronizedSourceSignature = baselineSignature,
                        WorkspaceSourceSignature = baselineSignature,
                        RemoteSourceSignature = baselineSignature,
                    },
                },
            });

            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true)
            {
                RemoteApplicationContent = remoteContent,
            };
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.ValidateAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Equal(expectedState, result.Snapshot.State);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Pull_UpdatesSyncStateAndPullTimestamp()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "old-source");
            File.WriteAllText(Path.Combine(sourcePath, "readme.txt"), "exported");
            CreateDeploymentProfile(sourcePath, "development");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true)
            {
                RemoteApplicationContent = "new-remote-source",
            };
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.PullAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });
            var syncState = new WorkspaceSynchronizationStateService().Read(paths.ApexMetadataPath);

            Assert.Equal(WorkspaceSynchronizationState.InSync, result.Snapshot.State);
            Assert.NotNull(syncState);
            Assert.NotNull(syncState!.Environments["dev"].LastPull);
            Assert.Equal(nameof(WorkspaceSynchronizationState.InSync), syncState.Environments["dev"].SynchronizationState);
            Assert.Equal("new-remote-source", File.ReadAllText(Path.Combine(sourcePath, "application.apx")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Push_SuccessfullyUpdatesSyncMetadataAndSnapshot()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "git-ahead-source");
            File.WriteAllText(Path.Combine(sourcePath, "readme.txt"), "exported");
            CreateDeploymentProfile(sourcePath, "development");

            var baselineRoot = Path.Combine(root, ".baseline");
            Directory.CreateDirectory(baselineRoot);
            File.WriteAllText(Path.Combine(baselineRoot, "application.apx"), "same-source");
            File.WriteAllText(Path.Combine(baselineRoot, "readme.txt"), "exported");
            var baselineSignature = ComputeDirectorySignature(baselineRoot);

            new WorkspaceSynchronizationStateService().Write(paths.ApexMetadataPath, new WorkspaceSynchronizationStateDocument
            {
                DefaultEnvironment = "dev",
                Environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>
                {
                    ["dev"] = new()
                    {
                        SynchronizationState = nameof(WorkspaceSynchronizationState.GitAhead),
                        ApplicationName = "Customer Orders Demo",
                        SynchronizedSourceSignature = baselineSignature,
                        WorkspaceSourceSignature = baselineSignature,
                        RemoteSourceSignature = baselineSignature,
                    },
                },
            });

            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true)
            {
                RemoteApplicationContent = "same-source",
            };
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.PushAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });
            var syncState = new WorkspaceSynchronizationStateService().Read(paths.ApexMetadataPath);
            var syncYaml = File.ReadAllText(paths.ApexMetadataPath);

            Assert.Equal(WorkspaceSynchronizationState.InSync, result.Snapshot.State);
            Assert.NotNull(syncState);
            Assert.NotNull(syncState!.Environments["dev"].LastPush);
            Assert.Equal("Succeeded", syncState.Environments["dev"].LastPushResult);
            Assert.Equal("development", syncState.Environments["dev"].LastDeploymentProfile);
            Assert.Equal("Succeeded", syncState.Environments["dev"].LastDeploymentResult);
            Assert.False(string.IsNullOrWhiteSpace(syncState.Environments["dev"].LastImportedRevision));
            Assert.Equal(nameof(WorkspaceSynchronizationState.InSync), syncState.Environments["dev"].SynchronizationState);
            Assert.Equal(1, runtime.ImportCallCount);
            Assert.Contains("-deployment /workspace/src/apex/deployments/development.apx", runtime.LastImportSql, StringComparison.Ordinal);
            Assert.Contains("Validation started", result.Message, StringComparison.Ordinal);
            Assert.Contains("Validation succeeded", result.Message, StringComparison.Ordinal);
            Assert.Contains("Importing application into Oracle APEX", result.Message, StringComparison.Ordinal);
            Assert.Contains("Import completed", result.Message, StringComparison.Ordinal);
            Assert.Contains("Synchronization metadata updated", result.Message, StringComparison.Ordinal);
            Assert.Contains("Final sync state: InSync", result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ORACLE_DEMO_PASSWORD", syncYaml, StringComparison.Ordinal);
            Assert.DoesNotContain("demo_password", syncYaml, StringComparison.Ordinal);
            Assert.DoesNotContain("session", syncYaml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Push_ValidationFailure_PreventsImport()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "invalid-source");
            CreateDeploymentProfile(sourcePath, "development");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new FailingValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.PushAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Equal(0, runtime.ImportCallCount);
            Assert.Equal(WorkspaceSynchronizationState.ValidationFailed, result.Snapshot.State);
            Assert.Contains("Push aborted", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Push_DeploymentAhead_TransitionsToValidationFailed()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            File.WriteAllText(Path.Combine(sourcePath, "readme.txt"), "exported");
            CreateDeploymentProfile(sourcePath, "development");
            var baselineSignature = ComputeDirectorySignature(sourcePath);

            new WorkspaceSynchronizationStateService().Write(paths.ApexMetadataPath, new WorkspaceSynchronizationStateDocument
            {
                DefaultEnvironment = "dev",
                Environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>
                {
                    ["dev"] = new()
                    {
                        SynchronizationState = nameof(WorkspaceSynchronizationState.InSync),
                        SynchronizedSourceSignature = baselineSignature,
                        WorkspaceSourceSignature = baselineSignature,
                        RemoteSourceSignature = baselineSignature,
                    },
                },
            });

            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true)
            {
                RemoteApplicationContent = "remote-change",
            };
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.PushAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Equal(WorkspaceSynchronizationState.ValidationFailed, result.Snapshot.State);
            Assert.Equal(0, runtime.ImportCallCount);
            Assert.Contains("Pull Changes first", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DiscoverApplications_WhenWorkspaceMappingMissing_ThrowsClearError()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateDefinition("oracle-apexlang-demo")));
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: false);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateDefinition("oracle-apexlang-demo"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DiscoverApplicationsAsync(new OracleApexApplicationDiscoveryRequest
            {
                Snapshot = snapshot,
                EnvironmentName = "dev",
                WorkspaceName = "TEST",
                ParsingSchema = "TESTSCHEMA",
                SqlclProfile = "local-apex-dev",
                SourcePath = "src/apex",
            }));

            Assert.Contains("workspace 'TEST' is missing or is not mapped", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetStatus_GeneratesOracleDiagnosticsReport()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            CreateDeploymentProfile(sourcePath, "development");
            new WorkspaceSynchronizationStateService().Write(paths.ApexMetadataPath, new WorkspaceSynchronizationStateDocument
            {
                DefaultEnvironment = "dev",
                Environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>
                {
                    ["dev"] = new()
                    {
                        SynchronizationState = nameof(WorkspaceSynchronizationState.InSync),
                        ApplicationName = "Customer Orders Demo",
                    },
                },
            });
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            await provider.GetStatusAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            var diagnosticsPath = Path.Combine(root, "docs", "diagnostics", "oracle-apex.md");
            var diagnostics = File.ReadAllText(diagnosticsPath);

            Assert.Contains("# Oracle APEX Diagnostics", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Oracle version:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("APEX version:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("SQLcl version:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Active deployment profile:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Discovered deployment profiles:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Current sync state:", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Recent History", diagnostics, StringComparison.Ordinal);
            var deploymentsDoc = File.ReadAllText(Path.Combine(root, "docs", "oracle-apex-deployments.md"));
            Assert.Contains("# Oracle APEX Deployments", deploymentsDoc, StringComparison.Ordinal);
            Assert.Contains("Recommended Promotion Flow", deploymentsDoc, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Validate_UsesDeploymentProfile()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            File.WriteAllText(Path.Combine(sourcePath, "readme.txt"), "exported");
            CreateDeploymentProfile(sourcePath, "development");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true) { RemoteApplicationContent = "same-source" };
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.ValidateAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Equal(WorkspaceSynchronizationState.InSync, result.Snapshot.State);
            Assert.Contains("-deployment /workspace/src/apex/deployments/development.apx", runtime.LastValidationSql, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Validate_WhenDeploymentProfileIsMissing_ReturnsActionableFailure()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateConnectedDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateConnectedDefinition("oracle-apexlang-demo"));

            var result = await provider.ValidateAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Equal(WorkspaceSynchronizationState.ValidationFailed, result.Snapshot.State);
            Assert.Contains("Configured deployment profile 'development' was not found", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Validate_UsesEnvironmentOverrideForDeploymentProfile()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(CreateMultiEnvironmentDefinition("oracle-apexlang-demo")));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            File.WriteAllText(Path.Combine(sourcePath, "readme.txt"), "exported");
            CreateDeploymentProfile(sourcePath, "development");
            CreateDeploymentProfile(sourcePath, "production");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true) { RemoteApplicationContent = "same-source" };
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, CreateMultiEnvironmentDefinition("oracle-apexlang-demo"));

            var result = await provider.ValidateAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "production" });

            Assert.Equal(WorkspaceSynchronizationState.InSync, result.Snapshot.State);
            Assert.Contains("-deployment /workspace/src/apex/deployments/production.apx", runtime.LastValidationSql, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetStatus_DefaultDeploymentSelection_UsesSingleDiscoveredProfile()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            var definition = CreateDefinitionWithoutConfiguredDeployment("oracle-apexlang-demo");
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(definition));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            CreateDeploymentProfile(sourcePath, "development");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, definition);

            var result = await provider.GetStatusAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Equal("development", result.Snapshot.DefaultEnvironment!.ActiveDeploymentProfile);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetStatus_WhenDeploymentProfileIsDuplicated_ReportsValidationIssue()
    {
        var root = CreateTempRoot();

        try
        {
            var paths = WorkspacePathBuilder.Build(root);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ApexMetadataPath)!);
            var definition = CreateConnectedDefinition("oracle-apexlang-demo");
            File.WriteAllText(paths.WorkspaceYamlPath, new WorkspaceYamlService().Write(definition));
            var sourcePath = Path.Combine(root, "src", "apex");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "application.apx"), "same-source");
            CreateDeploymentProfile(sourcePath, "first-profile", declaredProfileName: "development");
            CreateDeploymentProfile(sourcePath, "second-profile", declaredProfileName: "development");
            var runtime = new ScriptedOracleApexContainerRuntime(root, workspaceMappingExists: true);
            var provider = new OracleApexWorkspaceSynchronizationProvider(new WorkspaceSynchronizationStateService(), runtime, new SuccessfulValidationProcessRunner(), new WorkspaceYamlService());
            var snapshot = CreateSnapshot(root, paths, definition);

            var result = await provider.GetStatusAsync(new WorkspaceSynchronizationRequest { Snapshot = snapshot, EnvironmentName = "dev" });

            Assert.Contains("Duplicate Oracle APEX deployment profile", result.Snapshot.DefaultEnvironment!.DeploymentValidation, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static WorkspaceDefinition CreateDefinition(string name)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
            Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
            Services = ["oracle-demo", "oracle-ords"],
            Skills = [],
            Mcp = [],
        };

    private static WorkspaceDefinition CreateConnectedDefinition(string name)
    {
        var definition = CreateDefinition(name);
        return new WorkspaceDefinition
        {
            Workspace = definition.Workspace,
            Provider = definition.Provider,
            Runtime = definition.Runtime,
            Features = definition.Features.ToList(),
            Services = definition.Services.ToList(),
            Skills = definition.Skills.ToList(),
            Mcp = definition.Mcp.ToList(),
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = new()
                        {
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            ApplicationId = 100,
                            SqlclProfile = "local-apex-dev",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                            DeploymentProfile = "development",
                        },
                    },
                },
            },
        };
    }

    private static WorkspaceDefinition CreateMultiEnvironmentDefinition(string name)
    {
        var definition = CreateConnectedDefinition(name);
        return new WorkspaceDefinition
        {
            Workspace = definition.Workspace,
            Provider = definition.Provider,
            Runtime = definition.Runtime,
            Features = definition.Features.ToList(),
            Services = definition.Services.ToList(),
            Skills = definition.Skills.ToList(),
            Mcp = definition.Mcp.ToList(),
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = new()
                        {
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            ApplicationId = 100,
                            SqlclProfile = "local-apex-dev",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                            DeploymentProfile = "development",
                        },
                        ["production"] = new()
                        {
                            Workspace = "PROD",
                            ParsingSchema = "PRODSCHEMA",
                            ApplicationId = 42,
                            SqlclProfile = "prod-apex",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                            DeploymentProfile = "production",
                        },
                    },
                },
            },
        };
    }

    private static WorkspaceDefinition CreateDefinitionWithoutConfiguredDeployment(string name)
    {
        var definition = CreateConnectedDefinition(name);
        return new WorkspaceDefinition
        {
            Workspace = definition.Workspace,
            Provider = definition.Provider,
            Runtime = definition.Runtime,
            Features = definition.Features.ToList(),
            Services = definition.Services.ToList(),
            Skills = definition.Skills.ToList(),
            Mcp = definition.Mcp.ToList(),
            Oracle = new OracleWorkspacePreferences
            {
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = "dev",
                    Environments = new Dictionary<string, OracleApexEnvironmentPreferences>
                    {
                        ["dev"] = new()
                        {
                            Workspace = "TEST",
                            ParsingSchema = "TESTSCHEMA",
                            ApplicationId = 100,
                            SqlclProfile = "local-apex-dev",
                            SyncMode = WorkspaceSynchronizationModes.Manual,
                            SourcePath = "src/apex",
                        },
                    },
                },
            },
        };
    }

    private static WorkspaceSnapshot CreateSnapshot(string root, WorkspacePaths paths, WorkspaceDefinition definition)
    {
        return new()
        {
            Record = new WorkspaceRecord
            {
                Name = definition.Workspace.Name,
                RootPath = root,
                RepositoryPath = root,
                ConfigurationPath = "workspace.yaml",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = definition,
            Paths = paths,
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Protected.",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot { LatestCommitSha = "head123" },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = definition.Workspace.Name, State = WorkspaceSessionState.Resumable },
            AppliedState = new WorkspaceAppliedState { DesiredStateHash = "desired", WorkspaceDefinitionHash = "definition", AppliedUtc = DateTimeOffset.UtcNow, AppVersion = "test" },
            LocalRuntimeState = new WorkspaceRuntimeStateRecord { ResolvedEngine = "docker", ResolvedPlatform = "linux/amd64", CompatibilityMode = "native" },
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", IsAvailable = true, HostPlatform = new HostPlatformInfo() },
            UpdateRequired = false,
            Synchronization = new WorkspaceSynchronizationSnapshot(),
            Health = new WorkspaceHealthSnapshot(),
            Readiness = new WorkspaceReadinessSnapshot(),
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oracle-apex-connect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string ComputeDirectorySignature(string root)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/').StartsWith("deployments/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var pathBytes = System.Text.Encoding.UTF8.GetBytes(relativePath + "\n");
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            var contentBytes = File.ReadAllBytes(file);
            sha.TransformBlock(contentBytes, 0, contentBytes.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static void CreateDeploymentProfile(string sourcePath, string profileName, string? declaredProfileName = null)
    {
        var deploymentsPath = Path.Combine(sourcePath, "deployments");
        Directory.CreateDirectory(deploymentsPath);
        File.WriteAllText(Path.Combine(deploymentsPath, profileName + ".apx"), $"""
deployment {declaredProfileName ?? profileName} (
    workspace: TEST
    parsing-schema: TESTSCHEMA
    application-id: 100
)
""");
    }

    private sealed class SuccessfulValidationProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
        {
            var applicationPath = arguments.Skip(1).FirstOrDefault()?.ToString();
            Assert.NotNull(applicationPath);
            Assert.True(File.Exists(applicationPath!));
            return Task.FromResult(new ProcessResult
            {
                Command = $"{fileName} {string.Join(' ', arguments)}",
                ExitCode = 0,
                StandardOutput = "Validated",
                StandardError = string.Empty,
                StandardOutputLines = ["Validated"],
                StandardErrorLines = Array.Empty<string>(),
                Duration = TimeSpan.FromMilliseconds(10),
            });
        }
    }

    private sealed class FailingValidationProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, Action<bool, string>? onOutput = null, CancellationToken cancellationToken = default, TimeSpan? timeout = null, Action<string>? onDiagnostic = null)
            => Task.FromResult(new ProcessResult
            {
                Command = $"{fileName} {string.Join(' ', arguments)}",
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardError = "Validation failed",
                StandardOutputLines = Array.Empty<string>(),
                StandardErrorLines = ["Validation failed"],
                Duration = TimeSpan.FromMilliseconds(10),
            });
    }

    private sealed class ScriptedOracleApexContainerRuntime : IContainerRuntime
    {
        private readonly string _root;
        private readonly bool _workspaceMappingExists;

        public string RemoteApplicationContent { get; init; } = """
application customer-orders-demo (
    id: 100
    name: Customer Orders Demo
    alias: CUSTOMER-ORDERS-DEMO
    version: 1.2
    workspace: TEST
    parsing-schema: TESTSCHEMA
)
""";
        public int ImportCallCount { get; private set; }
        public string LastImportSql { get; private set; } = string.Empty;
        public string LastValidationSql { get; private set; } = string.Empty;

        public ScriptedOracleApexContainerRuntime(string root, bool workspaceMappingExists)
        {
            _root = root;
            _workspaceMappingExists = workspaceMappingExists;
        }

        public string RuntimeId => "docker";
        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => "workspace";
        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => [];
        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotImplementedException();
        public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RestartServiceAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> RepairOracleOrdsGatewayAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> ProbeHttpGetFromWorkspaceAsync(WorkspaceDefinition definition, string url, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Success("status=200\nlocation=\nbody=ORDS 24.2 landing"));
        public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            var command = string.Join(" ", arguments);
            if (command.Contains("scripts/sqlcl.sh -version", StringComparison.Ordinal))
            {
                return Task.FromResult(Success("SQLcl 26.1"));
            }

            var sqlFile = Directory.GetFiles(Path.Combine(_root, ".opencode", "apex", "queries"), "*.sql").Single();
            var sql = File.ReadAllText(sqlFile);
            if (sql.Contains("from apex_release", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Success("26.1.0"));
            }

            if (sql.Contains("FROM all_users", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Success(sql.Contains("PRODSCHEMA", StringComparison.OrdinalIgnoreCase) ? "PRODSCHEMA" : "TESTSCHEMA"));
            }

            if (sql.Contains("FROM apex_workspace_schemas", StringComparison.OrdinalIgnoreCase))
            {
                if (!_workspaceMappingExists)
                {
                    return Task.FromResult(Success(string.Empty));
                }

                return Task.FromResult(Success(sql.Contains("PROD", StringComparison.OrdinalIgnoreCase) ? "PROD|PRODSCHEMA" : "TEST|TESTSCHEMA"));
            }

            if (sql.Contains("FROM apex_applications", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Success("100|Customer Orders Demo|customer-orders-demo"));
            }

            if (sql.Contains("apex export -applicationid 100", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("apex export -applicationid 42", StringComparison.OrdinalIgnoreCase))
            {
                var marker = "-dir /workspace/";
                var start = sql.IndexOf(marker, StringComparison.Ordinal);
                var end = sql.IndexOf(" -force", start, StringComparison.Ordinal);
                var relativePath = sql[(start + marker.Length)..end].Trim();
                var exportDir = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar), "customer-orders-demo");
                Directory.CreateDirectory(exportDir);
                File.WriteAllText(Path.Combine(exportDir, "application.apx"), RemoteApplicationContent);
                File.WriteAllText(Path.Combine(exportDir, "readme.txt"), "exported");
                return Task.FromResult(Success("Exported"));
            }

            if (sql.Contains("apex import -workspace TEST -schema TESTSCHEMA -id 100", StringComparison.OrdinalIgnoreCase))
            {
                ImportCallCount++;
                LastImportSql = sql;
                return Task.FromResult(Success("Imported"));
            }

            if (sql.Contains("apex import -workspace PROD -schema PRODSCHEMA -id 42", StringComparison.OrdinalIgnoreCase))
            {
                ImportCallCount++;
                LastImportSql = sql;
                return Task.FromResult(Success("Imported"));
            }

            if (sql.Contains("apex validate -workspace", StringComparison.OrdinalIgnoreCase))
            {
                LastValidationSql = sql;
                return Task.FromResult(Success("Validated"));
            }

            return Task.FromResult(Success("OK"));
        }

        private static ProcessResult Success(string output)
            => new()
            {
                Command = "docker exec",
                ExitCode = 0,
                StandardOutput = output,
                StandardError = string.Empty,
                StandardOutputLines = string.IsNullOrWhiteSpace(output) ? Array.Empty<string>() : [output],
                StandardErrorLines = Array.Empty<string>(),
                Duration = TimeSpan.FromMilliseconds(10),
            };
    }
}
