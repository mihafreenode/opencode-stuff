using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceSynchronizationStateServiceTests
{
    [Fact]
    public void WriteAndRead_RoundTripsSynchronizationMetadata()
    {
        var service = new WorkspaceSynchronizationStateService();
        var filePath = Path.GetTempFileName();

        try
        {
            service.Write(filePath, new WorkspaceSynchronizationStateDocument
            {
                DefaultEnvironment = "dev",
                Environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dev"] = new()
                    {
                        SynchronizationState = nameof(WorkspaceSynchronizationState.GitAhead),
                        DriftSummary = "Git source has changed since the last import.",
                        LastValidation = new WorkspaceSynchronizationOperationState
                        {
                            Status = "Succeeded",
                            Revision = "abc123",
                            TimestampUtc = new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero),
                            Summary = "Validated application.apx",
                        },
                        LastImport = new WorkspaceSynchronizationOperationState
                        {
                            Status = "Succeeded",
                            Revision = "abc122",
                            TimestampUtc = new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero),
                            Summary = "Imported workspace source",
                        },
                        ImportedRevision = "abc122",
                        ExportedRevision = "abc121",
                        LastSynchronizedGitRevision = "abc122",
                        LastDeploymentProfile = "development",
                        LastDeploymentResult = "Succeeded",
                    },
                },
            });

            var roundTripped = service.Read(filePath);

            Assert.NotNull(roundTripped);
            Assert.Equal("dev", roundTripped!.DefaultEnvironment);
            Assert.True(roundTripped.Environments.ContainsKey("dev"));
            Assert.Equal(nameof(WorkspaceSynchronizationState.GitAhead), roundTripped.Environments["dev"].SynchronizationState);
            Assert.Equal("abc122", roundTripped.Environments["dev"].ImportedRevision);
            Assert.Equal("Validated application.apx", roundTripped.Environments["dev"].LastValidation!.Summary);
            Assert.Equal("development", roundTripped.Environments["dev"].LastDeploymentProfile);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Read_WhenMissing_ReturnsNull()
    {
        var service = new WorkspaceSynchronizationStateService();
        var path = Path.Combine(Path.GetTempPath(), $"missing-sync-{Guid.NewGuid():N}.yaml");

        Assert.Null(service.Read(path));
    }

    [Fact]
    public void Read_WhenStateNamesAreUnknown_NormalizesToUnknown()
    {
        var service = new WorkspaceSynchronizationStateService();
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, """
defaultEnvironment: dev
environments:
  dev:
    synchronizationState: unexpected-state
""");

            var state = service.Read(filePath);

            Assert.NotNull(state);
            Assert.Equal(nameof(WorkspaceSynchronizationState.Unknown), state!.Environments["dev"].SynchronizationState);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ReadinessAndHealth_ReflectSynchronizationDrift()
    {
        var snapshot = CreateSnapshot(WorkspaceSynchronizationState.Diverged);
        var health = WorkspaceHealthEngine.Build(snapshot);
        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = health });

        Assert.Contains(health.Providers, provider => provider.ProviderKey == "synchronization" && provider.Status == WorkspaceHealthStatus.Degraded);
        Assert.Contains(readiness.AttentionItems, item => item.Key == "oracle-apex-sync");
        Assert.Contains(readiness.Evidence, section => section.Label == "Synchronization");
    }

    [Fact]
    public void Health_OracleApexWorkspaceProvider_ReturnsActionableFailures()
    {
        var source = CreateSnapshot(WorkspaceSynchronizationState.Unknown);
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = source.Record.Name,
                RootPath = source.Record.RootPath,
                RepositoryPath = source.Record.RepositoryPath,
                ConfigurationPath = source.Record.ConfigurationPath,
                CreatedUtc = source.Record.CreatedUtc,
                LastOpenedUtc = source.Record.LastOpenedUtc,
                LastOperationSucceeded = true,
            },
            Definition = source.Definition,
            Paths = source.Paths,
            ConfigurationPath = source.ConfigurationPath,
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = source.Safety,
            Session = source.Session,
            AppliedState = source.AppliedState,
            LocalRuntimeState = source.LocalRuntimeState,
            ResolvedRuntimePlan = source.ResolvedRuntimePlan,
            UpdateRequired = source.UpdateRequired,
            Health = source.Health,
            Readiness = source.Readiness,
            AvailableServices = source.AvailableServices,
            Synchronization = new WorkspaceSynchronizationSnapshot
            {
                IsSupported = true,
                State = WorkspaceSynchronizationState.Unknown,
                Summary = "Synchronization state is unknown.",
                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                {
                    EnvironmentName = "dev",
                    WorkspaceName = "TEST",
                    ParsingSchema = "TESTSCHEMA",
                    ApplicationId = 100,
                    SourcePath = "src/apex",
                    OrdsStatus = "Unavailable",
                },
                Environments = [],
            },
        };

        var health = WorkspaceHealthEngine.Build(snapshot);

        Assert.Contains(health.Providers, provider => provider.ProviderKey == "oracle-apex-workspace" && provider.Summary.Contains("SQLcl not installed", StringComparison.Ordinal));
    }

    [Fact]
    public void Readiness_UsesSpecificOracleApexFailureMessages()
    {
        var source = CreateSnapshot(WorkspaceSynchronizationState.Unknown);
        var snapshot = new WorkspaceSnapshot
        {
            Record = source.Record,
            Definition = source.Definition,
            Paths = source.Paths,
            ConfigurationPath = source.ConfigurationPath,
            RuntimeState = source.RuntimeState,
            Safety = source.Safety,
            Session = source.Session,
            AppliedState = source.AppliedState,
            LocalRuntimeState = source.LocalRuntimeState,
            ResolvedRuntimePlan = source.ResolvedRuntimePlan,
            UpdateRequired = source.UpdateRequired,
            Health = source.Health,
            Readiness = source.Readiness,
            AvailableServices = source.AvailableServices,
            Synchronization = new WorkspaceSynchronizationSnapshot
            {
                IsSupported = true,
                State = WorkspaceSynchronizationState.Unknown,
                Summary = "Synchronization state is unknown.",
                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                {
                    EnvironmentName = "dev",
                    WorkspaceName = "TEST",
                    ParsingSchema = "TESTSCHEMA",
                    ApplicationId = 100,
                    SourcePath = "src/apex",
                    SqlclVersion = string.Empty,
                    SynchronizationMetadataValid = false,
                },
                Environments = [],
            },
        };

        var health = WorkspaceHealthEngine.Build(snapshot);
        var readiness = WorkspaceReadinessEngine.Build(new WorkspaceReadinessInput { Snapshot = snapshot, Health = health });

        Assert.Equal("Synchronization metadata is missing.", readiness.Summary);
    }

    private static WorkspaceSnapshot CreateSnapshot(WorkspaceSynchronizationState state)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"apex-sync-{Guid.NewGuid():N}");
        var paths = WorkspacePathBuilder.Build(rootPath);
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(paths.WorkspaceYamlPath, "workspace:\n  name: apex-sync\n");

        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "apex-sync",
                RootPath = rootPath,
                RepositoryPath = rootPath,
                ConfigurationPath = "workspace.yaml",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "apex-sync", Image = "ubuntu:24.04" },
                Provider = new WorkspaceProviderDefinition { Type = "git" },
                Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = 22 },
                Features = ["core", "oracle-demo", "oracle-apex-demo", "oracle-apexlang-demo"],
                Services = ["oracle-demo", "oracle-ords"],
                Oracle = new OracleWorkspacePreferences
                {
                    Apex = new OracleApexWorkspacePreferences
                    {
                        DefaultEnvironment = "dev",
                        Environments = new Dictionary<string, OracleApexEnvironmentPreferences>(StringComparer.OrdinalIgnoreCase)
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
            },
            Paths = paths,
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Workspace is protected.",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot { LatestCommitSha = "abc123" },
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "apex-sync", State = WorkspaceSessionState.NotRunning },
            Synchronization = new WorkspaceSynchronizationSnapshot
            {
                IsSupported = true,
                State = state,
                Summary = "Synchronization needs attention.",
                HasDrift = true,
                RequiresExplicitDecision = state == WorkspaceSynchronizationState.Diverged,
                Environments =
                [
                    new WorkspaceSynchronizationEnvironmentSnapshot
                    {
                        EnvironmentName = "dev",
                        SyncMode = WorkspaceSynchronizationModes.Manual,
                        SourcePath = "src/apex",
                        State = state,
                        Summary = "Synchronization needs attention.",
                    },
                ],
                DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                {
                    EnvironmentName = "dev",
                    SyncMode = WorkspaceSynchronizationModes.Manual,
                    SourcePath = "src/apex",
                    State = state,
                    Summary = "Synchronization needs attention.",
                },
            },
        };
    }
}
