using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Mcp;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class ApiContractTests : IDisposable
{
    private readonly ApiIntegrationEnvironment _environment = new();

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Health_And_Template_Routes_Work_Through_Http_Pipeline()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
            });
        });
        using var client = factory.CreateClient();

        var live = await client.GetFromJsonAsync<ApiHealthResponse>("/api/v1/health/live");
        Assert.NotNull(live);
        Assert.Equal("live", live!.Status);

        var templates = await client.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<WorkspaceTemplateSummaryModel>>>("/api/v1/templates");
        Assert.NotNull(templates);
        Assert.Contains(templates!.Data, item => item.TemplateId == "empty-workspace");

        var template = await client.GetFromJsonAsync<ApiEnvelope<WorkspaceTemplateDetailModel>>("/api/v1/templates/empty-workspace");
        Assert.NotNull(template);
        Assert.Equal("empty-workspace", template!.Data.Summary.TemplateId);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Unknown_Template_Returns_404_Error_Envelope()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/templates/does-not-exist");
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_template", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Workspace_Routes_Create_And_List_Workspaces_With_Real_Side_Effects()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest
        {
            TemplateId = "empty-workspace",
            WorkspaceName = "api-demo",
            DestinationRoot = _environment.WorkspaceParentRoot,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiEnvelope<WorkspaceRecordModel>>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.True(Directory.Exists(created!.Data.WorkspaceRoot));
        Assert.True(File.Exists(Path.Combine(created.Data.WorkspaceRoot, "workspace.yaml")));

        var list = await client.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<WorkspaceRecordModel>>>("/api/v1/workspaces");
        Assert.Contains(list!.Data, item => item.WorkspaceId == created.Data.WorkspaceId);

        var validate = await client.PostAsync($"/api/v1/workspaces/{created.Data.WorkspaceId}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Runtime_Endpoints_Map_Filters_And_Do_Not_Expose_Broad_Operations()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                ListRuntimeResourcesHandler = query => Task.FromResult(new RuntimeResourceInventory
                {
                    Resources = [new RuntimeOwnedResource { ResourceId = query.Project ?? string.Empty, Name = query.OwnerKind ?? string.Empty, RunId = query.RunId ?? string.Empty, Type = RuntimeResourceType.Container }],
                }),
                RunRuntimeDoctorHandler = _ => Task.FromResult(new RuntimeResourceInventory()),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/resources?owner=smoke&runId=run-1&project=proj&workspaceRoot=/tmp/ws");
        Assert.Equal("smoke", response!.Data.Resources[0].Name);
        Assert.Equal("run-1", response.Data.Resources[0].RunId);

        var doctor = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/doctor?owner=smoke");
        Assert.NotNull(doctor);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Smoke_Operation_Endpoints_Start_Poll_And_Cancel_Using_Real_Http_Pipeline()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceTemplateHandler = _ => Task.FromResult(new WorkspaceTemplateDetailModel { Summary = new WorkspaceTemplateSummaryModel { TemplateId = "empty-workspace" } }),
                RunSmokeHandler = async (request, cancellationToken) =>
                {
                    await gate.Task.WaitAsync(cancellationToken);
                    return new WorkspaceSmokeResult
                    {
                        TemplateId = request.TemplateId,
                        RunId = "run-1",
                        Status = WorkspaceSmokeStatus.Passed,
                        Phase = WorkspaceSmokePhase.Completed,
                        FailureClassification = WorkspaceSmokeFailureClassification.None,
                        CleanupVerificationSucceeded = true,
                        ArtifactDirectory = _environment.SmokeArtifactsRoot,
                    };
                },
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/smoke/runs", new StartSmokeRunRequest { TemplateId = "empty-workspace", Timeout = "00:05:00" });
        var operation = await start.Content.ReadFromJsonAsync<McpOperationModel>();
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal("queued", operation!.CurrentPhase);

        var queued = await client.GetFromJsonAsync<ApiEnvelope<McpOperationModel>>($"/api/v1/operations/{operation.OperationId}");
        Assert.Equal(McpOperationStatus.Running, queued!.Data.Status);

        var cancel = await client.PostAsync($"/api/v1/operations/{operation.OperationId}/cancel", null);
        var cancelled = await cancel.Content.ReadFromJsonAsync<ApiEnvelope<McpOperationModel>>();
        Assert.True(cancelled!.Data.CancellationRequested);

        gate.SetCanceled();
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task Smoke_Definitions_And_Cleanup_DryRun_Return_Stable_Contracts()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var definitions = await client.GetFromJsonAsync<ApiEnvelope<WorkspaceSmokeDefinitionCatalogResult>>("/api/v1/smoke/definitions");
        Assert.Equal("1", definitions!.Data.SchemaVersion);

        var cleanup = await client.PostAsJsonAsync("/api/v1/smoke/cleanup", new CleanupSmokeRequest { DryRun = true, IncludeAll = true });
        var cleanupResult = await cleanup.Content.ReadFromJsonAsync<ApiEnvelope<SmokeCleanupResult>>();
        Assert.True(cleanupResult!.Data.DryRun);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Publish_Assessment_Route_Returns_Typed_Assessment()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                PublishAssessmentHandler = workspaceId => Task.FromResult(new WorkspacePublishAssessmentModel
                {
                    WorkspaceId = workspaceId,
                    WorkspaceName = "alpha",
                    CurrentBranch = "users/test/alpha",
                    Summary = "Remote backup changed since your last sync. Update and review the Working Copy before publishing.",
                    ConfirmationMessage = string.Empty,
                    Findings = ["Ahead/behind: 1/2"],
                    Warnings = ["This is the first publish for the current Working Copy. Upstream tracking will be created."],
                    CanPublish = false,
                    IsBlocked = true,
                    RequiresConfirmation = false,
                    RequiresSavePoint = false,
                    HasRemoteConfigured = true,
                    RemoteName = "origin",
                    RemoteBranch = "origin/users/test/alpha",
                    AheadCount = 1,
                    BehindCount = 2,
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspacePublishAssessmentRecord>>("/api/v1/local-host/workspaces/alpha/publish-assessment");

        Assert.NotNull(response);
        Assert.Equal("alpha", response!.Data.WorkspaceId);
        Assert.Equal("alpha", response.Data.WorkspaceName);
        Assert.Contains("Ahead/behind: 1/2", response.Data.Findings);
        Assert.Contains("Upstream tracking will be created.", string.Join(Environment.NewLine, response.Data.Warnings), StringComparison.Ordinal);
        Assert.True(response.Data.IsBlocked);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Recovery_Assessment_Route_Returns_Typed_Assessment()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                RecoveryAssessmentHandler = _ => Task.FromResult(new WorkspaceRecoveryAssessmentModel
                {
                    Title = "Recover Workspace",
                    Summary = "Recovery validates generated files.",
                    Findings = ["Generated runtime files are out of date and need repair."],
                    ConfirmationMessage = "Run workspace recovery now?",
                    WorkspaceName = "alpha",
                    StatusSummary = "Workspace needs repair",
                    RecoverActions = ["Regenerate runtime files"],
                    CurrentProblems = ["Runtime files need repair"],
                    PreviousFailureContext = ["Port 1521 is already in use."],
                    WillNotChange = ["Delete project files"],
                    ManualActionSummary = "Port 1521 is already in use.",
                    ManualActions = ["Stop the other workspace"],
                    AdvancedDetails = "details",
                    LastCheckedAt = DateTimeOffset.UtcNow,
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceRecoveryAssessmentRecord>>("/api/v1/local-host/workspaces/alpha/recovery-assessment");

        Assert.NotNull(response);
        Assert.Equal("Recover Workspace", response!.Data.Title);
        Assert.Contains("Runtime files need repair", response.Data.CurrentProblems);
        Assert.Contains("Port 1521 is already in use.", response.Data.PreviousFailureContext);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Status_Route_Returns_Typed_State()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                SynchronizationStatusHandler = (_, _) => Task.FromResult(new WorkspaceSynchronizationStatusResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.Diverged,
                        Summary = "Oracle APEX source and Git workspace have diverged.",
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = "dev",
                            ActiveDeploymentProfile = "default",
                            AvailableDeploymentProfiles = ["default"],
                            State = WorkspaceSynchronizationState.Diverged,
                            Summary = "Diverged",
                        },
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<WorkspaceSynchronizationStatusResult>>("/api/v1/local-host/workspaces/alpha/synchronization/status");

        Assert.NotNull(response);
        Assert.Equal(WorkspaceSynchronizationState.Diverged, response!.Data.Snapshot.State);
        Assert.Equal("default", response.Data.Snapshot.DefaultEnvironment?.ActiveDeploymentProfile);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Backup_Route_Returns_Durable_Backup_Operation_Record()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = "alpha", RootPath = Path.Combine(_environment.WorkspaceParentRoot, "alpha"), RepositoryPath = Path.Combine(_environment.WorkspaceParentRoot, "alpha"), ConfigurationPath = "workspace.yaml" },
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = "alpha", Name = "alpha", Image = "ubuntu:24.04" } },
            Paths = WorkspacePathBuilder.Build(Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "workspace.yaml"),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.Protected, Headline = "Protected", Message = "Protected", LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot() },
            Session = new WorkspaceSessionSnapshot(),
            Health = new WorkspaceHealthSnapshot { OverallStatus = WorkspaceHealthStatus.Healthy, Summary = "Ready" },
            Readiness = new WorkspaceReadinessSnapshot { Summary = "Ready" },
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };

        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel { WorkspaceId = workspaceId, Name = snapshot.Definition.Workspace.Name, WorkspaceRoot = snapshot.Paths.RootPath, Snapshot = snapshot }),
                BackupWorkspaceHandler = async (_, destinationPath, _, _, cancellationToken) =>
                {
                    await gate.Task.WaitAsync(cancellationToken);
                    return new WorkspaceBackupOperationResultModel
                    {
                        Workspace = new WorkspaceRecordModel { WorkspaceId = snapshot.Definition.Workspace.Id, Name = snapshot.Definition.Workspace.Name, WorkspaceRoot = snapshot.Paths.RootPath, Snapshot = snapshot },
                        Message = $"Backup created at '{destinationPath}' with 1 file(s).",
                        Export = new WorkspaceBackupExportResult
                        {
                            ArchivePath = destinationPath,
                            FileCount = 1,
                            ArchiveSizeBytes = 512,
                            IncludedEntries = [],
                            ExcludedEntries = [],
                            Warnings = [],
                        },
                        Manifest = new WorkspaceBackupManifestResult
                        {
                            ManifestPath = Path.ChangeExtension(destinationPath, null) + "-backup-manifest.yaml",
                            ArchiveEntryPath = "backup-manifest.yaml",
                            IncludedFileCount = 1,
                            ExcludedFileCount = 0,
                            WarningCount = 0,
                        },
                    };
                },
            });
        });
        using var client = factory.CreateClient();

        var destinationPath = Path.Combine(_environment.Root, "backup.zip");
        var start = await client.PostAsJsonAsync($"/api/v1/local-host/workspaces/{snapshot.Definition.Workspace.Id}/backups", new OpenCode.Workspace.LocalClient.WorkspaceBackupRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            DestinationPath = destinationPath,
            OverwriteExisting = true,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(started);
        Assert.Equal("backup_workspace", started!.Data.OperationKind);

        var query = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>($"/api/v1/local-host/operations/{started.Data.OperationId}");
        Assert.NotNull(query);
        Assert.Equal(started.Data.OperationId, query!.Data.OperationId);
        Assert.Equal("backup_workspace", query.Data.OperationKind);

        gate.SetCanceled();
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Remove_Route_Returns_Durable_Removal_Operation_Record_And_Defaults_Do_Not_Delete_Files()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? removeOwnedRuntimeResources = null;
        bool? deleteWorkspaceFiles = null;

        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                RemoveWorkspaceHandler = async (workspaceId, removeRuntime, deleteFiles, _, cancellationToken) =>
                {
                    removeOwnedRuntimeResources = removeRuntime;
                    deleteWorkspaceFiles = deleteFiles;
                    await gate.Task.WaitAsync(cancellationToken);
                    return new WorkspaceRemovalOperationResultModel
                    {
                        Message = $"Removed '{workspaceId}' from the workspace list.",
                        Removal = new WorkspaceRemovalResultRecordModel
                        {
                            WorkspaceId = workspaceId,
                            WorkspaceName = "alpha",
                            WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                            RegistrationRemoved = true,
                            RuntimeResourcesRemoved = removeRuntime,
                            WorkspaceFilesDeleted = deleteFiles,
                            Warnings = [],
                            Succeeded = true,
                            FailureReason = string.Empty,
                        },
                    };
                },
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/remove", new OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(started);
        Assert.Equal("remove_workspace", started!.Data.OperationKind);

        var query = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>($"/api/v1/local-host/operations/{started.Data.OperationId}");
        Assert.NotNull(query);
        Assert.Equal(started.Data.OperationId, query!.Data.OperationId);
        Assert.Equal("remove_workspace", query.Data.OperationKind);
        Assert.False(removeOwnedRuntimeResources ?? true);
        Assert.False(deleteWorkspaceFiles ?? true);

        gate.SetCanceled();
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Validate_Route_Returns_Durable_Operation_With_Structured_Diagnostics()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ValidateSynchronizationHandler = (workspaceId, environmentName, deploymentProfileOverride, _) => Task.FromResult(new WorkspaceSynchronizationOperationResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.ValidationFailed,
                        Summary = "Validation failed.",
                    },
                    Message = $"Validation failed for environment '{environmentName ?? "dev"}'.",
                    Validation = new OracleApexValidationResult
                    {
                        IsSuccess = false,
                        Summary = "1 diagnostic",
                        Diagnostics =
                        [
                            new OracleApexCompilerDiagnostic
                            {
                                FilePath = "src/apex/application/pages/page_00001.sql",
                                Line = 12,
                                Column = 4,
                                Component = "page",
                                Property = "source",
                                Severity = "Error",
                                CompilerCode = "APEX-001",
                                Message = "Invalid page source.",
                                Category = "validation",
                            },
                        ],
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/validate", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("validate_synchronization", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("APEX-001", payload!.Validation!.Diagnostics[0].CompilerCode);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Diff_Route_Returns_Durable_Operation_With_Diff_Text()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                DiffSynchronizationHandler = (_, environmentName, _) => Task.FromResult(new WorkspaceSynchronizationDiffResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot { State = WorkspaceSynchronizationState.Diverged, Summary = "Diverged" },
                    Summary = "Differences were detected between workspace source and exported Oracle APEX source.",
                    DiffText = $"--- {environmentName ?? "dev"}/page_00001.sql{Environment.NewLine}+++ remote/page_00001.sql",
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/diff", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationDiffRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<WorkspaceSynchronizationDiffResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("diff_synchronization", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Contains("page_00001.sql", payload!.DiffText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Validate_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/validate", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Validate_Route_Rejects_Unknown_Profile()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                SynchronizationStatusHandler = (_, _) => Task.FromResult(new WorkspaceSynchronizationStatusResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = "dev",
                            AvailableDeploymentProfiles = ["default"],
                            ActiveDeploymentProfile = "default",
                        },
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/validate", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            DeploymentProfileOverride = "missing-profile",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
        Assert.Contains("missing-profile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Export_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ExportSynchronizationHandler = (_, environmentName, _) => Task.FromResult(new WorkspaceSynchronizationOperationResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.InSync,
                        Summary = "Export refreshed the workspace source tree.",
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = environmentName ?? "dev",
                            ActiveDeploymentProfile = "default",
                            AvailableDeploymentProfiles = ["default"],
                            State = WorkspaceSynchronizationState.InSync,
                            Summary = "In sync",
                        },
                    },
                    Message = $"Exported Oracle APEX application for environment '{environmentName ?? "dev"}'.",
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/export", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("export_synchronization", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Contains("Exported Oracle APEX application", payload!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Export_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/export", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Import_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ImportSynchronizationHandler = (_, environmentName, deploymentProfileOverride, _) => Task.FromResult(new WorkspaceSynchronizationOperationResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.InSync,
                        Summary = "Imported workspace source into Oracle APEX.",
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = environmentName ?? "dev",
                            ActiveDeploymentProfile = deploymentProfileOverride == string.Empty ? "default" : deploymentProfileOverride,
                            AvailableDeploymentProfiles = ["default"],
                            State = WorkspaceSynchronizationState.InSync,
                            Summary = "In sync",
                            LastDeploymentResult = "Succeeded",
                        },
                    },
                    Message = $"Imported workspace source into Oracle APEX for environment '{environmentName ?? "dev"}'.",
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/import", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("import_synchronization", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Contains("Imported workspace source into Oracle APEX", payload!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Import_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/import", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Import_Route_Rejects_Unknown_Profile()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                SynchronizationStatusHandler = (_, _) => Task.FromResult(new WorkspaceSynchronizationStatusResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = "dev",
                            AvailableDeploymentProfiles = ["default"],
                            ActiveDeploymentProfile = "default",
                        },
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/import", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            DeploymentProfileOverride = "missing-profile",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
        Assert.Contains("missing-profile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Synchronize_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                SynchronizeWorkspaceHandler = (_, environmentName, _, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionResult
                {
                    PreviousState = WorkspaceSynchronizationState.GitAhead,
                    ActionPerformed = OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionAction.PushChanges,
                    OperationResult = new WorkspaceSynchronizationOperationResult
                    {
                        Snapshot = new WorkspaceSynchronizationSnapshot
                        {
                            State = WorkspaceSynchronizationState.InSync,
                            Summary = "Imported workspace source into Oracle APEX.",
                            DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                            {
                                EnvironmentName = environmentName ?? "dev",
                                ActiveDeploymentProfile = "default",
                                AvailableDeploymentProfiles = ["default"],
                                State = WorkspaceSynchronizationState.InSync,
                                Summary = "In sync",
                            },
                        },
                        Message = "Validation started\nValidation succeeded\nImport completed\nSynchronization metadata updated\nFinal sync state: InSync",
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/synchronize", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizeRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("synchronize_workspace", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal(OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionAction.PushChanges, payload!.ActionPerformed);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Synchronize_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/synchronize", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizeRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Synchronize_Route_Rejects_Unknown_Profile()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                SynchronizationStatusHandler = (_, _) => Task.FromResult(new WorkspaceSynchronizationStatusResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = "dev",
                            AvailableDeploymentProfiles = ["default"],
                            ActiveDeploymentProfile = "default",
                            State = WorkspaceSynchronizationState.GitAhead,
                        },
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/synchronize", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizeRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            DeploymentProfileOverride = "missing-profile",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Pull_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                PullSynchronizationHandler = (_, environmentName, _) => Task.FromResult(new WorkspaceSynchronizationOperationResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.InSync,
                        Summary = "Pulled Oracle APEX changes into workspace source.",
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = environmentName ?? "dev",
                            ActiveDeploymentProfile = "default",
                            AvailableDeploymentProfiles = ["default"],
                            State = WorkspaceSynchronizationState.InSync,
                            Summary = "In sync",
                        },
                    },
                    Message = "Pulled Oracle APEX changes into workspace source.",
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/pull", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("pull_synchronization", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Contains("Pulled Oracle APEX changes", payload!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Pull_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/pull", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Push_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                PushSynchronizationHandler = (_, environmentName, deploymentProfileOverride, _) => Task.FromResult(new WorkspaceSynchronizationOperationResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        State = WorkspaceSynchronizationState.InSync,
                        Summary = "Imported workspace source into Oracle APEX.",
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = environmentName ?? "dev",
                            ActiveDeploymentProfile = string.IsNullOrWhiteSpace(deploymentProfileOverride) ? "default" : deploymentProfileOverride,
                            AvailableDeploymentProfiles = ["default"],
                            State = WorkspaceSynchronizationState.InSync,
                            Summary = "In sync",
                        },
                    },
                    Message = "Validation started\nValidation succeeded\nImport completed\nSynchronization metadata updated\nFinal sync state: InSync",
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/push", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<WorkspaceSynchronizationOperationResult>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("push_synchronization", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Contains("Validation started", payload!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Push_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/push", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Synchronization_Push_Route_Rejects_Unknown_Profile()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                SynchronizationStatusHandler = (_, _) => Task.FromResult(new WorkspaceSynchronizationStatusResult
                {
                    Snapshot = new WorkspaceSynchronizationSnapshot
                    {
                        DefaultEnvironment = new WorkspaceSynchronizationEnvironmentSnapshot
                        {
                            EnvironmentName = "dev",
                            AvailableDeploymentProfiles = ["default"],
                            ActiveDeploymentProfile = "default",
                            State = WorkspaceSynchronizationState.GitAhead,
                        },
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/synchronization/push", new OpenCode.Workspace.LocalClient.WorkspaceSynchronizationImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            DeploymentProfileOverride = "missing-profile",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Validate_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ValidateOracleAssistantHandler = (_, executionId, environmentName, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord
                {
                    ExecutionId = executionId ?? "exec-1",
                    Response = new WorkspaceSynchronizationOperationResult
                    {
                        Snapshot = new WorkspaceSynchronizationSnapshot { State = WorkspaceSynchronizationState.ValidationFailed, Summary = "Validation failed" },
                        Message = "Validation failed.",
                        Validation = new OracleApexValidationResult
                        {
                            IsSuccess = false,
                            Diagnostics = [new OracleApexCompilerDiagnostic { FilePath = "src/apex/page.sql", Line = 12, Column = 3, Severity = "Error", CompilerCode = "APEX-001", Message = "Invalid SQL.", Component = "page", Property = "source", Category = "validation" }],
                        },
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/validate", new OpenCode.Workspace.LocalClient.OracleAssistantValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            ExecutionId = "exec-1",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("validate_oracle_assistant", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("exec-1", payload!.ExecutionId);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Plan_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                PlanOracleAssistantHandler = (_, request, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord
                {
                    PlanId = "plan-1",
                    ContextRevision = "dev|nogit|",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Response = new OracleApexAssistantPlanResponse
                    {
                        Request = request,
                        Plan = new OracleApexEditPlan { ExpectedChangedFiles = ["src/apex/page.sql"] },
                        Review = "Create Reports page.",
                        Classification = OracleApexPlanClassification.Additive,
                        Warnings = ["Will update navigation."],
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/plan", new OpenCode.Workspace.LocalClient.OracleAssistantPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            Intent = "Create Reports page",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("plan_oracle_assistant", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("plan-1", payload!.PlanId);
        Assert.Equal("dev|nogit|", payload.ContextRevision);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Plan_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/plan", new OpenCode.Workspace.LocalClient.OracleAssistantPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            Intent = "Create Reports page",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Apply_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                PlanOracleAssistantHandler = (_, request, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord
                {
                    PlanId = "plan-1",
                    ContextRevision = "dev|nogit|",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Response = new OracleApexAssistantPlanResponse
                    {
                        Request = request,
                        Plan = new OracleApexEditPlan { ExpectedChangedFiles = ["src/apex/page.sql"] },
                        Review = "Create Reports page.",
                        Classification = OracleApexPlanClassification.Additive,
                    },
                }),
                ApplyOracleAssistantHandler = (_, _, plan, planId, contextRevision, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord
                {
                    PlanId = planId,
                    ContextRevision = contextRevision,
                    ExecutionId = "exec-1",
                    Response = new OracleApexAssistantExecutionResponse
                    {
                        IsSuccess = true,
                        Summary = "Applied semantic changes.",
                        ChangedFiles = plan.ExpectedChangedFiles,
                        WorkspaceIndex = new OracleApexWorkspaceIndex(),
                        RollbackManifest = new OracleApexAssistantRollbackManifest { ExecutionId = "exec-1", RollbackState = OracleApexAssistantRollbackState.Available },
                        Stage = OracleApexAssistantStage.Preview,
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var planStart = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/plan", new OpenCode.Workspace.LocalClient.OracleAssistantPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            Intent = "Create Reports page",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var planned = await planStart.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        _ = await WaitForLocalOperationAsync(client, planned!.Data.OperationId);

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/apply", new OpenCode.Workspace.LocalClient.OracleAssistantApplyRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            PlanId = "plan-1",
            ContextRevision = "dev|nogit|",
            PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("apply_oracle_assistant", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("plan-1", payload!.PlanId);
        Assert.Equal("exec-1", payload.ExecutionId);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Apply_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/apply", new OpenCode.Workspace.LocalClient.OracleAssistantApplyRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            PlanId = "plan-1",
            ContextRevision = "dev|abc123|sig",
            PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Repair_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                PlanOracleAssistantHandler = (_, request, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord
                {
                    PlanId = "plan-1",
                    ContextRevision = "dev|nogit|",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Response = new OracleApexAssistantPlanResponse
                    {
                        Request = request,
                        Plan = new OracleApexEditPlan { ExpectedChangedFiles = ["src/apex/page.sql"] },
                        Review = "Create Reports page.",
                        Classification = OracleApexPlanClassification.Additive,
                    },
                }),
                ApplyOracleAssistantHandler = (_, _, plan, planId, contextRevision, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord
                {
                    PlanId = planId,
                    ContextRevision = contextRevision,
                    ExecutionId = "exec-1",
                    Response = new OracleApexAssistantExecutionResponse
                    {
                        IsSuccess = true,
                        Summary = "Validation failed.",
                        ChangedFiles = plan.ExpectedChangedFiles,
                        WorkspaceIndex = new OracleApexWorkspaceIndex(),
                        CompilerValidation = new OracleApexValidationResult { Diagnostics = [new OracleApexCompilerDiagnostic { CompilerCode = "APEX-1001", Message = "Missing alias." }] },
                        RollbackManifest = new OracleApexAssistantRollbackManifest { ExecutionId = "exec-1", EnvironmentName = "dev", RollbackState = OracleApexAssistantRollbackState.Available },
                        Stage = OracleApexAssistantStage.SqlclValidation,
                    },
                }),
                PlanOracleAssistantRepairHandler = (_, _, sourcePlan, validation, planId, executionId, contextRevision, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantRepairPlanOperationRecord
                {
                    RepairPlanId = "repair-1",
                    PlanId = planId,
                    ExecutionId = executionId,
                    ContextRevision = contextRevision,
                    Response = new OracleApexAssistantRepairPlanResponse
                    {
                        Plan = new OracleApexEditPlan { Intent = "repair", ExpectedChangedFiles = sourcePlan.ExpectedChangedFiles },
                        Review = "Repair review",
                        CompilerValidation = validation,
                    },
                }),
                ExecuteOracleAssistantRepairHandler = (_, _, repairPlan, planId, _, repairPlanId, contextRevision, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantRepairOperationRecord
                {
                    RepairPlanId = repairPlanId,
                    PlanId = planId,
                    ExecutionId = "exec-2",
                    ContextRevision = contextRevision,
                    Response = new OracleApexAssistantExecutionResponse
                    {
                        IsSuccess = true,
                        Summary = "Applied semantic repair.",
                        ChangedFiles = repairPlan.ExpectedChangedFiles,
                        WorkspaceIndex = new OracleApexWorkspaceIndex(),
                        RollbackManifest = new OracleApexAssistantRollbackManifest { ExecutionId = "exec-2", RollbackState = OracleApexAssistantRollbackState.Available },
                        Stage = OracleApexAssistantStage.Preview,
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var planStart = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/plan", new OpenCode.Workspace.LocalClient.OracleAssistantPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            Intent = "Create Reports page",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var planned = await planStart.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        _ = await WaitForLocalOperationAsync(client, planned!.Data.OperationId);

        var applyStart = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/apply", new OpenCode.Workspace.LocalClient.OracleAssistantApplyRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            PlanId = "plan-1",
            ContextRevision = "dev|nogit|",
            PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var applied = await applyStart.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        _ = await WaitForLocalOperationAsync(client, applied!.Data.OperationId);

        var repairPlanStart = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/repair-plan", new OpenCode.Workspace.LocalClient.OracleAssistantRepairPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            PlanId = "plan-1",
            ExecutionId = "exec-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var repairedPlan = await repairPlanStart.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        _ = await WaitForLocalOperationAsync(client, repairedPlan!.Data.OperationId);

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/repair", new OpenCode.Workspace.LocalClient.OracleAssistantRepairExecutionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            PlanId = "plan-1",
            ExecutionId = "exec-1",
            RepairPlanId = "repair-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.OracleAssistantRepairOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("execute_oracle_assistant_repair", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("repair-1", payload!.RepairPlanId);
        Assert.Equal("exec-2", payload.ExecutionId);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Repair_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/repair", new OpenCode.Workspace.LocalClient.OracleAssistantRepairExecutionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            PlanId = "plan-1",
            ExecutionId = "exec-1",
            RepairPlanId = "repair-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Repair_Route_Rejects_Unknown_RepairPlanId()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/repair", new OpenCode.Workspace.LocalClient.OracleAssistantRepairExecutionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            PlanId = "plan-1",
            ExecutionId = "exec-1",
            RepairPlanId = "repair-missing",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Rollback_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                PlanOracleAssistantHandler = (_, request, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord
                {
                    PlanId = "plan-1",
                    ContextRevision = "dev|nogit|",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Response = new OracleApexAssistantPlanResponse
                    {
                        Request = request,
                        Plan = new OracleApexEditPlan { ExpectedChangedFiles = ["src/apex/page.sql"] },
                        Review = "Create Reports page.",
                        Classification = OracleApexPlanClassification.Additive,
                    },
                }),
                ApplyOracleAssistantHandler = (_, _, plan, planId, contextRevision, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord
                {
                    PlanId = planId,
                    ContextRevision = contextRevision,
                    ExecutionId = "exec-1",
                    Response = new OracleApexAssistantExecutionResponse
                    {
                        IsSuccess = true,
                        Summary = "Applied semantic changes.",
                        ChangedFiles = plan.ExpectedChangedFiles,
                        WorkspaceIndex = new OracleApexWorkspaceIndex(),
                        RollbackManifest = new OracleApexAssistantRollbackManifest { ExecutionId = "exec-1", EnvironmentName = "dev", RollbackState = OracleApexAssistantRollbackState.Available },
                        Stage = OracleApexAssistantStage.Preview,
                    },
                }),
                RollbackOracleAssistantHandler = (_, executionId, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantRollbackOperationRecord
                {
                    ExecutionId = executionId,
                    Response = new OracleApexAssistantRollbackResponse
                    {
                        IsSuccess = true,
                        Summary = "Rollback completed.",
                        RollbackManifest = new OracleApexAssistantRollbackManifest { ExecutionId = executionId, RollbackState = OracleApexAssistantRollbackState.Completed },
                        RollbackState = OracleApexAssistantRollbackState.Completed,
                        RestoredFiles = ["src/apex/page.sql"],
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var planStart = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/plan", new OpenCode.Workspace.LocalClient.OracleAssistantPlanRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            Intent = "Create Reports page",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var planned = await planStart.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        _ = await WaitForLocalOperationAsync(client, planned!.Data.OperationId);

        var applyStart = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/apply", new OpenCode.Workspace.LocalClient.OracleAssistantApplyRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            PlanId = "plan-1",
            ContextRevision = "dev|nogit|",
            PostEditBehavior = OracleApexAssistantPostEditBehavior.SourceOnly,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var applied = await applyStart.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        _ = await WaitForLocalOperationAsync(client, applied!.Data.OperationId);

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/rollback", new OpenCode.Workspace.LocalClient.OracleAssistantRollbackRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            ExecutionId = "exec-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.OracleAssistantRollbackOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("rollback_oracle_assistant", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("exec-1", payload!.ExecutionId);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Rollback_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/rollback", new OpenCode.Workspace.LocalClient.OracleAssistantRollbackRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            ExecutionId = "exec-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Rollback_Route_Rejects_Unknown_ExecutionId()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/rollback", new OpenCode.Workspace.LocalClient.OracleAssistantRollbackRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            ExecutionId = "exec-missing",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleApex_Discovery_Route_Returns_Structured_Candidates()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                DiscoverOracleApexApplicationsHandler = (_, environmentName, workspaceName, parsingSchema, sqlclProfile, sourcePath, _) => Task.FromResult(new OracleApexApplicationDiscoveryResult
                {
                    EnvironmentName = environmentName,
                    WorkspaceName = workspaceName,
                    ParsingSchema = parsingSchema,
                    SqlclProfile = sqlclProfile,
                    SourcePath = sourcePath,
                    Applications =
                    [
                        new OracleApexApplicationInfo { ApplicationId = 100, ApplicationName = "Reports", Alias = "reports" },
                        new OracleApexApplicationInfo { ApplicationId = 101, ApplicationName = "Admin", Alias = "admin" },
                    ],
                    Summary = "Found 2 Oracle APEX application(s) in workspace 'TEST'.",
                }),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-apex/discover-applications", new OpenCode.Workspace.LocalClient.OracleApexApplicationDiscoveryQuery
        {
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            WorkspaceName = "TEST",
            ParsingSchema = "TESTSCHEMA",
            SqlclProfile = "local-apex-dev",
            SourcePath = "src/apex",
        });
        var envelope = await response.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OracleApexApplicationDiscoveryResult>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.Equal(2, envelope!.Data.Applications.Count);
        Assert.Equal(100, envelope.Data.Applications[0].ApplicationId);
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleApex_Discovery_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-apex/discover-applications", new OpenCode.Workspace.LocalClient.OracleApexApplicationDiscoveryQuery
        {
            WorkspaceId = "beta",
            EnvironmentName = "dev",
            WorkspaceName = "TEST",
            ParsingSchema = "TESTSCHEMA",
            SqlclProfile = "local-apex-dev",
            SourcePath = "src/apex",
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleApex_Connect_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ConnectExistingOracleApexApplicationHandler = (_, environmentName, workspaceName, parsingSchema, applicationId, sqlclProfile, sourcePath, _) => Task.FromResult(new OracleApexConnectExistingApplicationResult
                {
                    Snapshot = CreateApiSnapshot("alpha", Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                    Message = $"Connected Oracle APEX application 'Reports' ({applicationId}) for environment '{environmentName}', exported it to '{sourcePath}', and validated the exported source.",
                    ProcessResults =
                    [
                        new ProcessResult { Command = "export", ExitCode = 0, StandardOutput = "exported", StandardError = string.Empty, StandardOutputLines = ["exported"], StandardErrorLines = [], Duration = TimeSpan.FromSeconds(1) },
                        new ProcessResult { Command = "validate", ExitCode = 0, StandardOutput = "validated", StandardError = string.Empty, StandardOutputLines = ["validated"], StandardErrorLines = [], Duration = TimeSpan.FromSeconds(1) },
                    ],
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-apex/connect-existing-application", new OpenCode.Workspace.LocalClient.ConnectExistingOracleApexApplicationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            EnvironmentName = "dev",
            WorkspaceName = "TEST",
            ParsingSchema = "TESTSCHEMA",
            SqlclProfile = "local-apex-dev",
            SourcePath = "src/apex",
            ApplicationId = 100,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.ConnectExistingOracleApexApplicationOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("connect_existing_oracle_apex_application", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal(100, payload!.ApplicationId);
        Assert.Equal("src/apex", payload.SourcePath);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleApex_Connect_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-apex/connect-existing-application", new OpenCode.Workspace.LocalClient.ConnectExistingOracleApexApplicationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            EnvironmentName = "dev",
            WorkspaceName = "TEST",
            ParsingSchema = "TESTSCHEMA",
            SqlclProfile = "local-apex-dev",
            SourcePath = "src/apex",
            ApplicationId = 100,
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_InteractiveSessions_Create_List_Filter_And_Idempotency_Work()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = workspaceId,
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, workspaceId),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, workspaceId), workspaceId),
                }),
                ListWorkspacesHandler = () => Task.FromResult<IReadOnlyList<WorkspaceRecordModel>>([
                    new WorkspaceRecordModel { WorkspaceId = "alpha", Name = "alpha", WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"), Snapshot = CreateApiSnapshot("alpha", Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha") },
                    new WorkspaceRecordModel { WorkspaceId = "beta", Name = "beta", WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "beta"), Snapshot = CreateApiSnapshot("beta", Path.Combine(_environment.WorkspaceParentRoot, "beta"), "beta") },
                ]),
            });
        });
        using var client = factory.CreateClient();
        var commandId = Guid.NewGuid().ToString("n");

        var create = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/interactive-sessions", new OpenCode.Workspace.LocalClient.CreateInteractiveAgentSessionRequest
        {
            CommandId = commandId,
            WorkspaceId = "alpha",
            Title = "OpenCode session - alpha",
        });
        var created = await create.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>();
        var get = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>($"/api/v1/interactive-agent-sessions/{created!.Data.InteractiveAgentSessionId}");
        var list = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<IReadOnlyList<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>>("/api/v1/interactive-agent-sessions");
        var filtered = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<IReadOnlyList<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>>("/api/v1/interactive-agent-sessions?workspaceId=alpha");
        var duplicate = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/interactive-sessions", new OpenCode.Workspace.LocalClient.CreateInteractiveAgentSessionRequest
        {
            CommandId = commandId,
            WorkspaceId = "alpha",
            Title = "OpenCode session - alpha",
        });
        var duplicateCreated = await duplicate.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>();

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.NotNull(created?.Data);
        Assert.Equal(created!.Data.InteractiveAgentSessionId, get!.Data.InteractiveAgentSessionId);
        Assert.Contains(list!.Data, item => item.InteractiveAgentSessionId == created.Data.InteractiveAgentSessionId);
        Assert.All(filtered!.Data, item => Assert.Equal("alpha", item.WorkspaceId));
        Assert.Equal(created.Data.InteractiveAgentSessionId, duplicateCreated!.Data.InteractiveAgentSessionId);
        Assert.DoesNotContain("token", await create.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_InteractiveSessions_Create_Rejects_Unknown_Workspace()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = _ => throw new OpenCodeWorkspaceMcpException("workspace_not_found", "Workspace 'missing' was not found.", "Refresh workspaces and retry."),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/missing/interactive-sessions", new OpenCode.Workspace.LocalClient.CreateInteractiveAgentSessionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "missing",
            Title = "OpenCode session - missing",
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("workspace_not_found", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_InteractiveSession_Attach_Heartbeat_Detach_And_Conflict_Work()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = workspaceId,
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, workspaceId),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, workspaceId), workspaceId),
                }),
            });
        });
        using var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/interactive-sessions", new OpenCode.Workspace.LocalClient.CreateInteractiveAgentSessionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            Title = "OpenCode session - alpha",
        });
        var created = await create.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>();
        var attach = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created!.Data.InteractiveAgentSessionId}/attachments", new OpenCode.Workspace.LocalClient.AttachInteractiveSessionRequest
        {
            SessionId = created.Data.InteractiveAgentSessionId,
            CommandId = Guid.NewGuid().ToString("n"),
            ClientInstanceId = "client-1",
        });
        var attached = await attach.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveSessionAttachResult>>();
        var attachmentTokenIndex = Array.IndexOf(attached!.Data.LaunchDescriptor.Arguments.ToArray(), "--attachment-token");
        var attachmentToken = attached.Data.LaunchDescriptor.Arguments[attachmentTokenIndex + 1];
        var activate = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created.Data.InteractiveAgentSessionId}/attachments/{attached.Data.Attachment.AttachmentId}/activate", new OpenCode.Workspace.LocalClient.ActivateInteractiveSessionAttachmentRequest
        {
            AttachmentToken = attachmentToken,
            HelperProcessId = 101,
        });
        _ = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created.Data.InteractiveAgentSessionId}/attachments/{attached.Data.Attachment.AttachmentId}/process-started", new OpenCode.Workspace.LocalClient.InteractiveSessionAttachmentProcessStartedRequest
        {
            AttachmentToken = attachmentToken,
            ChildProcessId = 202,
        });
        var conflict = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created.Data.InteractiveAgentSessionId}/attachments", new OpenCode.Workspace.LocalClient.AttachInteractiveSessionRequest
        {
            SessionId = created.Data.InteractiveAgentSessionId,
            CommandId = Guid.NewGuid().ToString("n"),
            ClientInstanceId = "client-2",
        });
        var heartbeat = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created.Data.InteractiveAgentSessionId}/attachments/{attached!.Data.Attachment.AttachmentId}/heartbeat", new OpenCode.Workspace.LocalClient.InteractiveSessionAttachmentHeartbeatRequest
        {
            AttachmentToken = attachmentToken,
        });
        var detached = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created.Data.InteractiveAgentSessionId}/attachments/{attached.Data.Attachment.AttachmentId}/detach", new OpenCode.Workspace.LocalClient.DetachInteractiveSessionAttachmentRequest
        {
            ClientInstanceId = "client-1",
            Reason = "test_detach",
        });
        var exited = await client.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{created.Data.InteractiveAgentSessionId}/attachments/{attached.Data.Attachment.AttachmentId}/process-exit", new OpenCode.Workspace.LocalClient.InteractiveSessionAttachmentProcessExitRequest
        {
            AttachmentToken = attachmentToken,
            ChildProcessId = 202,
            Outcome = "detach_requested",
        });
        var heartbeatEnvelope = await heartbeat.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveSessionAttachmentHeartbeatResult>>();
        var detachedEnvelope = await exited.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.InteractiveAgentSessionRecord>>();
        var conflictError = await conflict.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detached.StatusCode);
        Assert.Equal("wt.exe", attached.Data.LaunchDescriptor.FileName);
        Assert.Equal(OpenCode.Workspace.LocalClient.InteractiveAttachmentStatus.Active, heartbeatEnvelope!.Data.Attachment.Status);
        Assert.Equal(OpenCode.Workspace.LocalClient.InteractiveAgentSessionStatus.Detached, detachedEnvelope!.Data.Status);
        Assert.Equal("already_attached", conflictError!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_InteractiveSession_Attach_Rejects_Invalid_Session()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/interactive-agent-sessions/missing/attachments", new OpenCode.Workspace.LocalClient.AttachInteractiveSessionRequest
        {
            SessionId = "missing",
            CommandId = Guid.NewGuid().ToString("n"),
            ClientInstanceId = "client-1",
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("interactive_session_not_found", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Import_Route_Returns_Durable_Operation_Record()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ImportOracleAssistantHandler = (_, executionId, _, _, _) => Task.FromResult(new OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord
                {
                    ExecutionId = executionId ?? "exec-1",
                    Response = new WorkspaceSynchronizationOperationResult
                    {
                        Snapshot = new WorkspaceSynchronizationSnapshot { State = WorkspaceSynchronizationState.InSync, Summary = "Import completed" },
                        Message = "Imported validated APEXlang source.",
                    },
                }),
            });
        });
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/import", new OpenCode.Workspace.LocalClient.OracleAssistantImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            ExecutionId = "exec-1",
            EnvironmentName = "dev",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await start.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var query = await WaitForLocalOperationAsync(client, started!.Data.OperationId);
        var payload = query.Result?.Deserialize<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord>(OpenCode.Workspace.LocalClient.LocalHostContract.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("import_oracle_assistant", started.Data.OperationKind);
        Assert.NotNull(payload);
        Assert.Equal("exec-1", payload!.ExecutionId);
        Assert.DoesNotContain("password", query.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Validate_Route_Rejects_Workspace_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/validate", new OpenCode.Workspace.LocalClient.OracleAssistantValidationRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            ExecutionId = "exec-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_OracleAssistant_Import_Route_Rejects_Stale_Execution()
    {
        await using var factory = _environment.CreateFactory(services =>
        {
            services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService
            {
                GetWorkspaceHandler = workspaceId => Task.FromResult(new WorkspaceRecordModel
                {
                    WorkspaceId = workspaceId,
                    Name = "alpha",
                    WorkspaceRoot = Path.Combine(_environment.WorkspaceParentRoot, "alpha"),
                    Snapshot = CreateApiSnapshot(workspaceId, Path.Combine(_environment.WorkspaceParentRoot, "alpha"), "alpha"),
                }),
                ImportOracleAssistantHandler = (_, executionId, _, _, _) => throw new OpenCodeWorkspaceMcpException("invalid_request", $"Oracle Assistant execution '{executionId}' does not match the current workspace execution 'exec-2'.", "Refresh the Assistant state and retry."),
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/oracle-assistant/import", new OpenCode.Workspace.LocalClient.OracleAssistantImportRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "alpha",
            ExecutionId = "exec-1",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var started = await response.Content.ReadFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>();
        var operation = await WaitForLocalOperationAsync(client, started!.Data.OperationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OpenCode.Workspace.LocalClient.WorkspaceOperationStatus.Failed, operation.Status);
        Assert.Contains("exec-2", operation.OriginalFailure?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task LocalHost_Remove_Route_Rejects_Workspace_Id_Mismatch()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/local-host/workspaces/alpha/remove", new OpenCode.Workspace.LocalClient.WorkspaceRemovalRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = "beta",
            RequestedBy = new OpenCode.Workspace.LocalClient.OperationInitiator { Kind = "test" },
        });
        var error = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", error!.Code);
        Assert.Contains("does not match route workspace id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task Live_Api_Smoke_Run_Completes_And_Leaves_No_Smoke_Resources()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/smoke/runs", new StartSmokeRunRequest { TemplateId = "empty-workspace", Timeout = "00:05:00" });
        var operation = await start.Content.ReadFromJsonAsync<McpOperationModel>();
        Assert.NotNull(operation);

        var completed = await WaitForOperationAsync(client, operation!.OperationId, TimeSpan.FromMinutes(4));
        Assert.Equal(McpOperationStatus.Succeeded, completed.Status);

        var doctor = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/doctor?owner=smoke");
        Assert.Empty(doctor!.Data.Resources);
        Assert.Empty(doctor.Data.Orphans);
    }

    [Fact]
    [Trait("Category", "ApiIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task Live_Api_Smoke_Cancellation_Cleans_Up_And_Reports_Cancelled()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/v1/smoke/runs", new StartSmokeRunRequest { TemplateId = "web-testing", Timeout = "00:05:00" });
        var operation = await start.Content.ReadFromJsonAsync<McpOperationModel>();
        Assert.NotNull(operation);

        var active = await WaitForOperationStateAsync(client, operation!.OperationId, TimeSpan.FromMinutes(2), item => item.Status == McpOperationStatus.Running && item.StartedUtc is not null);
        Assert.False(active.CancellationRequested);

        var cancel = await client.PostAsync($"/api/v1/operations/{operation.OperationId}/cancel", null);
        var cancelled = await cancel.Content.ReadFromJsonAsync<ApiEnvelope<McpOperationModel>>();
        Assert.True(cancelled!.Data.CancellationRequested);

        var completed = await WaitForOperationAsync(client, operation.OperationId, TimeSpan.FromMinutes(3));
        Assert.Equal(McpOperationStatus.Cancelled, completed.Status);
        Assert.Equal("cancelled", completed.FailureClassification, ignoreCase: true);

        var doctor = await client.GetFromJsonAsync<ApiEnvelope<RuntimeResourceInventory>>("/api/v1/runtime/doctor?owner=smoke");
        Assert.Empty(doctor!.Data.Resources);
        Assert.Empty(doctor.Data.Orphans);
    }

    public void Dispose() => _environment.Dispose();

    private static WorkspaceSnapshot CreateApiSnapshot(string workspaceId, string rootPath, string workspaceName)
        => new()
        {
            Record = new WorkspaceRecord { Name = workspaceName, RootPath = rootPath, RepositoryPath = rootPath, ConfigurationPath = "workspace.yaml" },
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = workspaceId, Name = workspaceName, Image = "ubuntu:24.04" } },
            Paths = WorkspacePathBuilder.Build(rootPath, "workspace.yaml"),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.Protected, Headline = "Protected", Message = "Protected", LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot() },
            Session = new WorkspaceSessionSnapshot(),
            Health = new WorkspaceHealthSnapshot { OverallStatus = WorkspaceHealthStatus.Healthy, Summary = "Ready" },
            Readiness = new WorkspaceReadinessSnapshot { Summary = "Ready" },
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };

    private static async Task<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord> WaitForLocalOperationAsync(HttpClient client, string operationId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await client.GetFromJsonAsync<OpenCode.Workspace.LocalClient.LocalHostEnvelope<OpenCode.Workspace.LocalClient.WorkspaceOperationRecord>>($"/api/v1/local-host/operations/{operationId}");
            if (response?.Data.Result is not null || response?.Data.Status == OpenCode.Workspace.LocalClient.WorkspaceOperationStatus.Failed)
            {
                return response.Data;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException($"Operation '{operationId}' did not publish a result payload in time.");
    }

    private static async Task<McpOperationModel> WaitForOperationAsync(HttpClient client, string operationId, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var operation = await client.GetFromJsonAsync<ApiEnvelope<McpOperationModel>>($"/api/v1/operations/{operationId}");
            if (operation!.Data.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                return operation.Data;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Operation '{operationId}' did not complete in time.");
    }

    private static async Task<McpOperationModel> WaitForOperationStateAsync(HttpClient client, string operationId, TimeSpan timeout, Func<McpOperationModel, bool> predicate)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var operation = await client.GetFromJsonAsync<ApiEnvelope<McpOperationModel>>($"/api/v1/operations/{operationId}");
            if (predicate(operation!.Data))
            {
                return operation.Data;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Operation '{operationId}' did not reach the expected state in time.");
    }
}

internal sealed class FakeApiService : IOpenCodeWorkspaceMcpService
{
    public Func<string, Task<WorkspaceTemplateDetailModel>>? GetWorkspaceTemplateHandler { get; init; }
    public Func<string, Task<WorkspaceRecordModel>>? GetWorkspaceHandler { get; init; }
    public Func<Task<IReadOnlyList<WorkspaceRecordModel>>>? ListWorkspacesHandler { get; init; }
    public Func<string, string, bool, Action<CommandLogEntry>?, CancellationToken, Task<WorkspaceBackupOperationResultModel>>? BackupWorkspaceHandler { get; init; }
    public Func<string, Task<WorkspacePublishAssessmentModel>>? PublishAssessmentHandler { get; init; }
    public Func<string, Action<CommandLogEntry>?, CancellationToken, Task<WorkspacePublishOperationResultModel>>? PublishWorkspaceHandler { get; init; }
    public Func<string, bool, bool, Action<CommandLogEntry>?, CancellationToken, Task<WorkspaceRemovalOperationResultModel>>? RemoveWorkspaceHandler { get; init; }
    public Func<string, Task<WorkspaceRecoveryAssessmentModel>>? RecoveryAssessmentHandler { get; init; }
    public Func<string, string?, Task<WorkspaceSynchronizationStatusResult>>? SynchronizationStatusHandler { get; init; }
    public Func<string, string?, CancellationToken, Task<WorkspaceSynchronizationOperationResult>>? ExportSynchronizationHandler { get; init; }
    public Func<string, string?, string?, CancellationToken, Task<WorkspaceSynchronizationOperationResult>>? ImportSynchronizationHandler { get; init; }
    public Func<string, string?, CancellationToken, Task<WorkspaceSynchronizationOperationResult>>? PullSynchronizationHandler { get; init; }
    public Func<string, string?, string?, CancellationToken, Task<WorkspaceSynchronizationOperationResult>>? PushSynchronizationHandler { get; init; }
    public Func<string, string?, string?, CancellationToken, Task<WorkspaceSynchronizationOperationResult>>? ValidateSynchronizationHandler { get; init; }
    public Func<string, string?, CancellationToken, Task<WorkspaceSynchronizationDiffResult>>? DiffSynchronizationHandler { get; init; }
    public Func<string, string?, string?, CancellationToken, Task<OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionResult>>? SynchronizeWorkspaceHandler { get; init; }
    public Func<string, string?, string?, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord>>? ValidateOracleAssistantHandler { get; init; }
    public Func<string, string?, string?, bool, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord>>? ImportOracleAssistantHandler { get; init; }
    public Func<string, OracleApexAssistantRequest, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord>>? PlanOracleAssistantHandler { get; init; }
    public Func<string, OracleApexAssistantRequest, OracleApexEditPlan, string, string, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord>>? ApplyOracleAssistantHandler { get; init; }
    public Func<string, OracleApexAssistantRequest, OracleApexEditPlan, OracleApexValidationResult, string, string, string, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantRepairPlanOperationRecord>>? PlanOracleAssistantRepairHandler { get; init; }
    public Func<string, OracleApexAssistantRequest, OracleApexEditPlan, string, string, string, string, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantRepairOperationRecord>>? ExecuteOracleAssistantRepairHandler { get; init; }
    public Func<string, string, CancellationToken, Task<OpenCode.Workspace.LocalClient.OracleAssistantRollbackOperationRecord>>? RollbackOracleAssistantHandler { get; init; }
    public Func<string, string, string, string, string, string, CancellationToken, Task<OracleApexApplicationDiscoveryResult>>? DiscoverOracleApexApplicationsHandler { get; init; }
    public Func<string, string, string, string, int, string, string, CancellationToken, Task<OracleApexConnectExistingApplicationResult>>? ConnectExistingOracleApexApplicationHandler { get; init; }
    public Func<RuntimeOwnershipQuery, Task<RuntimeResourceInventory>>? ListRuntimeResourcesHandler { get; init; }
    public Func<RuntimeOwnershipQuery, Task<RuntimeResourceInventory>>? RunRuntimeDoctorHandler { get; init; }
    public Func<WorkspaceSmokeSingleRunRequest, CancellationToken, Task<WorkspaceSmokeResult>>? RunSmokeHandler { get; init; }

    public ServerHealthModel GetServerHealth() => new();
    public Task<IReadOnlyList<WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceTemplateSummaryModel>>([new WorkspaceTemplateSummaryModel { TemplateId = "empty-workspace" }]);
    public Task<WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default) => GetWorkspaceTemplateHandler?.Invoke(templateId) ?? Task.FromResult(new WorkspaceTemplateDetailModel { Summary = new WorkspaceTemplateSummaryModel { TemplateId = templateId } });
    public Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceSmokeDefinitionCatalogResult());
    public Task<IReadOnlyList<WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default) => ListWorkspacesHandler?.Invoke() ?? Task.FromResult<IReadOnlyList<WorkspaceRecordModel>>([]);
    public Task<WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => GetWorkspaceHandler?.Invoke(workspaceId) ?? throw new OpenCodeWorkspaceMcpException("workspace_not_found", $"Workspace '{workspaceId}' was not found.");
    public Task<WorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryPath, string workspaceName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitBranchValidationResult> ValidateExistingGitCheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(new GitBranchValidationResult(true, string.Empty, false));
    public Task<WorkspaceRecordModel> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<string> SuggestSavePointMessageAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult("Capture current workspace state");
    public Task<WorkspaceRecordModel> CreateSavePointAsync(string workspaceId, string message, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> CreateCheckpointAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceBackupOperationResultModel> BackupWorkspaceAsync(string workspaceId, string destinationPath, bool overwriteExisting, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => BackupWorkspaceHandler?.Invoke(workspaceId, destinationPath, overwriteExisting, progress, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspacePublishAssessmentModel> AssessWorkspacePublishAsync(string workspaceId, CancellationToken cancellationToken = default) => PublishAssessmentHandler?.Invoke(workspaceId) ?? throw new NotSupportedException();
    public Task<WorkspacePublishOperationResultModel> PublishWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => PublishWorkspaceHandler?.Invoke(workspaceId, progress, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceRemovalOperationResultModel> RemoveWorkspaceAsync(string workspaceId, bool removeOwnedRuntimeResources, bool deleteWorkspaceFiles, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => RemoveWorkspaceHandler?.Invoke(workspaceId, removeOwnedRuntimeResources, deleteWorkspaceFiles, progress, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceRecoveryAssessmentModel> AssessWorkspaceRecoveryAsync(string workspaceId, CancellationToken cancellationToken = default) => RecoveryAssessmentHandler?.Invoke(workspaceId) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationStatusResult> GetSynchronizationStatusAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => SynchronizationStatusHandler?.Invoke(workspaceId, environmentName) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> ExportSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => ExportSynchronizationHandler?.Invoke(workspaceId, environmentName, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> ImportSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => ImportSynchronizationHandler?.Invoke(workspaceId, environmentName, deploymentProfileOverride, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> PullSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => PullSynchronizationHandler?.Invoke(workspaceId, environmentName, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> PushSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => PushSynchronizationHandler?.Invoke(workspaceId, environmentName, deploymentProfileOverride, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationOperationResult> ValidateSynchronizationAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => ValidateSynchronizationHandler?.Invoke(workspaceId, environmentName, deploymentProfileOverride, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceSynchronizationDiffResult> DiffSynchronizationAsync(string workspaceId, string? environmentName = null, CancellationToken cancellationToken = default) => DiffSynchronizationHandler?.Invoke(workspaceId, environmentName, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.WorkspaceSynchronizationExecutionResult> SynchronizeWorkspaceAsync(string workspaceId, string? environmentName = null, string? deploymentProfileOverride = null, CancellationToken cancellationToken = default) => SynchronizeWorkspaceHandler?.Invoke(workspaceId, environmentName, deploymentProfileOverride, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantPlanOperationRecord> PlanOracleApexChangeAsync(string workspaceId, OracleApexAssistantRequest request, CancellationToken cancellationToken = default) => PlanOracleAssistantHandler?.Invoke(workspaceId, request, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantApplyOperationRecord> ExecuteOracleApexPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan plan, string planId, string contextRevision, CancellationToken cancellationToken = default) => ApplyOracleAssistantHandler?.Invoke(workspaceId, request, plan, planId, contextRevision, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantRepairPlanOperationRecord> CreateOracleApexRepairPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation, string planId, string executionId, string contextRevision, CancellationToken cancellationToken = default) => PlanOracleAssistantRepairHandler?.Invoke(workspaceId, request, sourcePlan, validation, planId, executionId, contextRevision, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantRepairOperationRecord> ExecuteOracleApexRepairPlanAsync(string workspaceId, OracleApexAssistantRequest request, OracleApexEditPlan repairPlan, string planId, string executionId, string repairPlanId, string contextRevision, CancellationToken cancellationToken = default) => ExecuteOracleAssistantRepairHandler?.Invoke(workspaceId, request, repairPlan, planId, executionId, repairPlanId, contextRevision, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantRollbackOperationRecord> RollBackOracleApexGeneratedChangeAsync(string workspaceId, string executionId, CancellationToken cancellationToken = default) => RollbackOracleAssistantHandler?.Invoke(workspaceId, executionId, cancellationToken) ?? throw new NotSupportedException();
    public Task<OracleApexApplicationDiscoveryResult> DiscoverOracleApexApplicationsAsync(string workspaceId, string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath, CancellationToken cancellationToken = default) => DiscoverOracleApexApplicationsHandler?.Invoke(workspaceId, environmentName, workspaceName, parsingSchema, sqlclProfile, sourcePath, cancellationToken) ?? throw new NotSupportedException();
    public Task<OracleApexConnectExistingApplicationResult> ConnectExistingOracleApexApplicationAsync(string workspaceId, string environmentName, string workspaceName, string parsingSchema, int applicationId, string sqlclProfile, string sourcePath, CancellationToken cancellationToken = default) => ConnectExistingOracleApexApplicationHandler?.Invoke(workspaceId, environmentName, workspaceName, parsingSchema, applicationId, sqlclProfile, sourcePath, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord> ValidateOracleAssistantGeneratedApplicationAsync(string workspaceId, string? executionId = null, string? environmentName = null, CancellationToken cancellationToken = default) => ValidateOracleAssistantHandler?.Invoke(workspaceId, executionId, environmentName, cancellationToken) ?? throw new NotSupportedException();
    public Task<OpenCode.Workspace.LocalClient.OracleAssistantSynchronizationOperationRecord> ImportOracleAssistantGeneratedApplicationAsync(string workspaceId, string? executionId = null, string? environmentName = null, bool allowNonDevelopmentDeployment = false, CancellationToken cancellationToken = default) => ImportOracleAssistantHandler?.Invoke(workspaceId, executionId, environmentName, allowNonDevelopmentDeployment, cancellationToken) ?? throw new NotSupportedException();
    public Task<WorkspaceTimeline> GetWorkspaceTimelineAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceTimeline());
    public Task<WorkspaceCheckpointIndex> GetWorkspaceCheckpointIndexAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceCheckpointIndex());
    public Task<WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> PrepareWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> StartWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> RecoverWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> ResetWorkspaceRuntimeAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> AttachWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<WorkspaceRecordModel> ReprovisionWorkspaceAsync(string workspaceId, Action<CommandLogEntry>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceRecordModel());
    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceSmokeDefinition>>([new WorkspaceSmokeDefinition { TemplateId = "empty-workspace", DisplayName = "Empty Workspace", Family = "lightweight", Supported = true }]);
    public Task<WorkspaceSmokeResult> RunSmokeAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default) => RunSmokeHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new WorkspaceSmokeResult { TemplateId = request.TemplateId, RunId = "run-1", Status = WorkspaceSmokeStatus.Passed, Phase = WorkspaceSmokePhase.Completed, FailureClassification = WorkspaceSmokeFailureClassification.None, CleanupVerificationSucceeded = true });
    public Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceSmokeMatrixResult { MatrixRunId = "matrix-1", SelectedTemplates = request.TemplateIds, Status = WorkspaceSmokeStatus.Passed });
    public Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => ListRuntimeResourcesHandler?.Invoke(query) ?? Task.FromResult(new RuntimeResourceInventory());
    public Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default) => RunRuntimeDoctorHandler?.Invoke(query) ?? Task.FromResult(new RuntimeResourceInventory());
    public Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new SmokeCleanupResult { Succeeded = true, DryRun = options.DryRun, VerificationSucceeded = true });
    public Task<IReadOnlyList<ArtifactListItem>> ListWorkspaceArtifactsAsync(string workspaceId, string? relativePath, bool recursive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArtifactListItem>>([]);
    public Task<ArtifactReadModel> GetWorkspaceArtifactAsync(string workspaceId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ArtifactListItem>> ListSmokeArtifactsAsync(string runId, string? relativePath, bool recursive, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArtifactListItem>>([]);
    public Task<ArtifactReadModel> GetSmokeArtifactAsync(string runId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactReadModel> ReadArtifactByResourceUriAsync(string resourceUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArtifactResourceReadModel> ReadArtifactResourceAsync(string resourceUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExcelProcessResultModel> ProcessExcelArtifactAsync(string sourcePath, string? destinationWorkspaceId, string? processingTemplateId, string? outputLogicalName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
