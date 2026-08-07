using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.ComponentModel;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

[McpServerToolType]
public sealed class OpenCodeWorkspaceMcpTools
{
    [McpServerTool(Name = "list_workspace_templates"), Description("List concise local workspace template metadata.")]
    public static async Task<CallToolResult> ListWorkspaceTemplates(IOpenCodeWorkspaceMcpService service)
        => McpResults.Success(await service.ListWorkspaceTemplatesAsync(), "Listed workspace templates.");

    [McpServerTool(Name = "get_workspace_template"), Description("Get a detailed local workspace template definition.")]
    public static async Task<CallToolResult> GetWorkspaceTemplate([Description("Stable template id")] string templateId, IOpenCodeWorkspaceMcpService service)
        => await ExecuteAsync(() => service.GetWorkspaceTemplateAsync(templateId));

    [McpServerTool(Name = "list_smoke_definitions"), Description("List local smoke definitions.")]
    public static async Task<CallToolResult> ListSmokeDefinitions(IOpenCodeWorkspaceMcpService service)
        => McpResults.Success(await service.ListSmokeDefinitionsAsync(), "Listed smoke definitions.");

    [McpServerTool(Name = "list_workspaces"), Description("List registered local workspaces.")]
    public static async Task<CallToolResult> ListWorkspaces(IOpenCodeWorkspaceMcpService service)
        => McpResults.Success(await service.ListWorkspacesAsync(), "Listed workspaces.");

    [McpServerTool(Name = "get_workspace"), Description("Get local workspace details.")]
    public static async Task<CallToolResult> GetWorkspace([Description("Workspace id or root path")] string workspaceId, IOpenCodeWorkspaceMcpService service)
        => await ExecuteAsync(() => service.GetWorkspaceAsync(workspaceId));

    [McpServerTool(Name = "create_workspace"), Description("Create a local workspace from a built-in template.")]
    public static async Task<CallToolResult> CreateWorkspace(
        [Description("Stable template id")] string templateId,
        [Description("Workspace name")] string workspaceName,
        [Description("Destination parent directory")] string destinationRoot,
        IOpenCodeWorkspaceMcpService service)
        => await ExecuteAsync(() => service.CreateWorkspaceAsync(templateId, workspaceName, destinationRoot));

    [McpServerTool(Name = "provision_workspace"), Description("Start a long-running local workspace provisioning operation. The result is an operation id, not completion. Poll get_operation with afterSequence until the terminal status is completed, failed, or cancelled. Do not run Docker manually or start duplicate provisioning while an operation is active.")]
    public static async Task<CallToolResult> ProvisionWorkspace([Description("Workspace id")] string workspaceId, IOpenCodeWorkspaceMcpService service, LocalHostOperationStore operations)
        => await ExecuteAsync(async () => await operations.StartProvisionWorkspaceAsync(workspaceId), McpResults.OperationStarted, "Provision operation started.");

    public static CallToolResult ProvisionWorkspaceCompatibility([Description("Workspace id")] string workspaceId, IOpenCodeWorkspaceMcpService service, McpOperationStore operations)
        => McpResults.OperationStarted(operations.Start("provision_workspace", workspaceId, "Provisioning workspace.", async (reporter, cancellationToken) =>
        {
            reporter.MarkStarted("preparing", "Preparing workspace provisioning.");
            return await service.ProvisionWorkspaceAsync(workspaceId, reporter.ReportProgress, cancellationToken);
        }), "Provision operation started.");

    [McpServerTool(Name = "prepare_workspace"), Description("Start durable workspace preparation. Poll get_operation until it reaches a terminal status.")]
    public static Task<CallToolResult> PrepareWorkspace(string workspaceId, LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.StartWorkspaceLifecycleAsync(workspaceId, "prepare"), McpResults.OperationStarted, "Prepare operation started.");

    [McpServerTool(Name = "start_workspace"), Description("Start durable workspace runtime preparation and startup. Poll get_operation until it reaches a terminal status.")]
    public static Task<CallToolResult> StartWorkspace(string workspaceId, LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.StartWorkspaceLifecycleAsync(workspaceId, "start"), McpResults.OperationStarted, "Start operation started.");

    [McpServerTool(Name = "recover_workspace"), Description("Start durable workspace recovery. Poll get_operation until it reaches a terminal status.")]
    public static Task<CallToolResult> RecoverWorkspace(string workspaceId, LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.StartWorkspaceLifecycleAsync(workspaceId, "recover"), McpResults.OperationStarted, "Recovery operation started.");

    [McpServerTool(Name = "validate_workspace"), Description("Validate a local workspace and return readiness and health details.")]
    public static async Task<CallToolResult> ValidateWorkspace([Description("Workspace id")] string workspaceId, IOpenCodeWorkspaceMcpService service)
        => await ExecuteAsync(() => service.ValidateWorkspaceAsync(workspaceId));

    [McpServerTool(Name = "stop_workspace"), Description("Stop local workspace runtime services.")]
    public static Task<CallToolResult> StopWorkspace([Description("Workspace id")] string workspaceId, LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.StartWorkspaceLifecycleAsync(workspaceId, "stop"), McpResults.OperationStarted, "Stop operation started.");

    [McpServerTool(Name = "remove_workspace_runtime"), Description("Remove local workspace runtime resources while preserving durable files.")]
    public static Task<CallToolResult> RemoveWorkspaceRuntime([Description("Workspace id")] string workspaceId, LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.StartWorkspaceLifecycleAsync(workspaceId, "reset-runtime"), McpResults.OperationStarted, "Runtime reset operation started.");

    [McpServerTool(Name = "run_smoke"), Description("Start a long-running local smoke run. The tool returns an operation immediately. Poll get_operation with afterSequence for incremental progress until the terminal status is completed, failed, or cancelled. Do not assume success from the initial response.")]
    public static async Task<CallToolResult> RunSmoke(
        [Description("Stable template id")] string templateId,
        [Description("Optional timeout as hh:mm:ss")] string? timeout = null,
        [Description("Optional artifact root")] string? artifactsRoot = null,
        IOpenCodeWorkspaceMcpService service = null!,
        LocalHostOperationStore operations = null!)
    {
        return await ExecuteAsync(async () =>
        {
            await service.GetWorkspaceTemplateAsync(templateId);
            return await operations.StartSmokeRunAsync(templateId, timeout, artifactsRoot);
        }, McpResults.OperationStarted, "Smoke operation started.");
    }

    public static async Task<CallToolResult> RunSmokeCompatibility(
        [Description("Stable template id")] string templateId,
        [Description("Optional timeout as hh:mm:ss")] string? timeout = null,
        [Description("Optional artifact root")] string? artifactsRoot = null,
        IOpenCodeWorkspaceMcpService service = null!,
        McpOperationStore operations = null!)
    {
        await service.GetWorkspaceTemplateAsync(templateId);
        var request = new WorkspaceSmokeSingleRunRequest { TemplateId = templateId, Timeout = string.IsNullOrWhiteSpace(timeout) ? null : TimeSpan.Parse(timeout), ArtifactsRoot = artifactsRoot ?? string.Empty };
        var operation = operations.Start("run_smoke", string.Empty, $"Running smoke for {templateId}.", async (reporter, token) =>
        {
            reporter.MarkStarted("queued", $"Queued smoke run for '{templateId}'.");
            request = new WorkspaceSmokeSingleRunRequest
            {
                TemplateId = request.TemplateId,
                ArtifactsRoot = request.ArtifactsRoot,
                Timeout = request.Timeout,
                Progress = new Progress<WorkspaceSmokeProgressUpdate>(reporter.ReportProgress),
            };
            return await service.RunSmokeAsync(request, token);
        });
        return McpResults.OperationStarted(operation, "Smoke operation started.");
    }

    [McpServerTool(Name = "run_smoke_matrix"), Description("Start a long-running local smoke matrix run. The tool returns an operation immediately. Poll get_operation with afterSequence for incremental progress until the terminal status is completed, failed, or cancelled.")]
    public static async Task<CallToolResult> RunSmokeMatrix(
        [Description("Optional template ids")] string[]? templateIds = null,
        [Description("Optional smoke family")] string? family = null,
        [Description("Select all smoke definitions")] bool all = false,
        [Description("Optional timeout as hh:mm:ss")] string? timeout = null,
        IOpenCodeWorkspaceMcpService service = null!,
        LocalHostOperationStore operations = null!)
    {
        return await ExecuteAsync(
            async () => await operations.StartSmokeMatrixAsync(templateIds, family, all, timeout),
            McpResults.OperationStarted,
            "Smoke matrix operation started.");
    }

    public static async Task<CallToolResult> RunSmokeMatrixCompatibility(
        [Description("Optional template ids")] string[]? templateIds = null,
        [Description("Optional smoke family")] string? family = null,
        [Description("Select all smoke definitions")] bool all = false,
        [Description("Optional timeout as hh:mm:ss")] string? timeout = null,
        IOpenCodeWorkspaceMcpService service = null!,
        McpOperationStore operations = null!)
    {
        var selected = await service.SelectSmokeDefinitionsAsync(new WorkspaceSmokeDefinitionSelectionRequest
        {
            TemplateIds = templateIds ?? Array.Empty<string>(),
            Family = family,
            All = all,
        });
        var request = new WorkspaceSmokeMatrixRunRequest { TemplateIds = selected.Select(item => item.TemplateId).ToArray(), MatrixTimeout = string.IsNullOrWhiteSpace(timeout) ? null : TimeSpan.Parse(timeout) };
        var operation = operations.Start("run_smoke_matrix", string.Empty, "Running smoke matrix.", async (reporter, token) =>
        {
            reporter.MarkStarted("queued", "Queued smoke matrix.");
            request = new WorkspaceSmokeMatrixRunRequest
            {
                TemplateIds = request.TemplateIds,
                MatrixTimeout = request.MatrixTimeout,
                Progress = new Progress<WorkspaceSmokeProgressUpdate>(reporter.ReportProgress),
            };
            return await service.RunSmokeMatrixAsync(request, token);
        });
        return McpResults.OperationStarted(operation, "Smoke matrix operation started.");
    }

    [McpServerTool(Name = "list_smoke_resources"), Description("List owned smoke runtime resources.")]
    public static async Task<CallToolResult> ListSmokeResources(IOpenCodeWorkspaceMcpService service)
        => McpResults.Success(await service.ListRuntimeResourcesAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }), "Listed smoke runtime resources.");

    [McpServerTool(Name = "cleanup_smoke_resources"), Description("Clean only labeled smoke-owned runtime resources.")]
    public static async Task<CallToolResult> CleanupSmokeResources(
        [Description("Run in dry-run mode")] bool dryRun,
        [Description("Include all smoke resources")] bool includeAll,
        [Description("Optional smoke run id filter")] string? runId = null,
        IOpenCodeWorkspaceMcpService service = null!)
        => await ExecuteAsync(() => service.CleanupSmokeResourcesAsync(new SmokeCleanupOptions(dryRun, includeAll, runId, "json")));

    [McpServerTool(Name = "list_runtime_resources"), Description("List local runtime resources using OpenCode ownership labels.")]
    public static async Task<CallToolResult> ListRuntimeResources(
        string? owner = null,
        string? runId = null,
        string? project = null,
        string? workspaceRoot = null,
        IOpenCodeWorkspaceMcpService service = null!)
        => McpResults.Success(await service.ListRuntimeResourcesAsync(new RuntimeOwnershipQuery { OwnerKind = owner, RunId = runId, Project = project, WorkspaceRoot = workspaceRoot }), "Listed runtime resources.");

    [McpServerTool(Name = "run_runtime_doctor"), Description("Run the local runtime ownership doctor view.")]
    public static async Task<CallToolResult> RunRuntimeDoctor(
        string? owner = null,
        string? runId = null,
        string? project = null,
        string? workspaceRoot = null,
        IOpenCodeWorkspaceMcpService service = null!)
        => McpResults.Success(await service.RunRuntimeDoctorAsync(new RuntimeOwnershipQuery { OwnerKind = owner, RunId = runId, Project = project, WorkspaceRoot = workspaceRoot }), "Ran runtime doctor.");

    [McpServerTool(Name = "list_workspace_artifacts"), Description("List files under a workspace artifacts root.")]
    public static async Task<CallToolResult> ListWorkspaceArtifacts(string workspaceId, string? relativePath = null, bool recursive = false, IOpenCodeWorkspaceMcpService service = null!)
        => McpResults.Success(await service.ListWorkspaceArtifactsAsync(workspaceId, relativePath, recursive), "Listed workspace artifacts.");

    [McpServerTool(Name = "get_workspace_artifact"), Description("Read a workspace artifact when it is text-sized, or return metadata for larger artifacts.")]
    public static async Task<CallToolResult> GetWorkspaceArtifact(string workspaceId, string relativePath, IOpenCodeWorkspaceMcpService service)
        => await ExecuteAsync(() => service.GetWorkspaceArtifactAsync(workspaceId, relativePath));

    [McpServerTool(Name = "list_smoke_artifacts"), Description("List files under a smoke artifact directory.")]
    public static async Task<CallToolResult> ListSmokeArtifacts(string runId, string? relativePath = null, bool recursive = false, IOpenCodeWorkspaceMcpService service = null!)
        => McpResults.Success(await service.ListSmokeArtifactsAsync(runId, relativePath, recursive), "Listed smoke artifacts.");

    [McpServerTool(Name = "get_smoke_artifact"), Description("Read a smoke artifact when it is text-sized, or return metadata for larger artifacts.")]
    public static async Task<CallToolResult> GetSmokeArtifact(string runId, string relativePath, IOpenCodeWorkspaceMcpService service)
        => await ExecuteAsync(() => service.GetSmokeArtifactAsync(runId, relativePath));

    [McpServerTool(Name = "process_excel_artifact"), Description("Process a local XLSX artifact and emit an output workbook with an OpenCode Result worksheet.")]
    public static async Task<CallToolResult> ProcessExcelArtifact(string sourcePath, string? destinationWorkspaceId = null, string? processingTemplateId = null, string? outputLogicalName = null, IOpenCodeWorkspaceMcpService service = null!)
        => await ExecuteAsync(() => service.ProcessExcelArtifactAsync(sourcePath, destinationWorkspaceId, processingTemplateId, outputLogicalName));

    [McpServerTool(Name = "get_operation"), Description("Get a long-running local operation. Use afterSequence to receive only new incremental progress events. Terminal statuses are completed, failed, and cancelled. Cleanup may continue briefly after cancellation and detailed logs are available through artifact references.")]
    public static Task<CallToolResult> GetOperation(string operationId, long? afterSequence = null, int? maxEvents = null, LocalHostOperationStore operations = null!)
        => ExecuteAsync(() => operations.GetAsync(operationId, afterSequence, maxEvents));

    public static Task<CallToolResult> GetOperationLegacy(string operationId, long? afterSequence = null, int? maxEvents = null, McpOperationStore operations = null!)
        => ExecuteAsync(() => Task.FromResult(operations.Get(operationId, afterSequence, maxEvents)));

    [McpServerTool(Name = "list_operations"), Description("List canonical LocalHost operations shared by all MCP and desktop clients.")]
    public static Task<CallToolResult> ListOperations(LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.ListAsync());

    public static Task<CallToolResult> ListOperationsLegacy(McpOperationStore operations)
        => ExecuteAsync(() => Task.FromResult(operations.List()));

    [McpServerTool(Name = "cancel_operation"), Description("Request cancellation for a canonical LocalHost operation. Cancellation never deletes workspace or interactive sessions.")]
    public static Task<CallToolResult> CancelOperation(string operationId, LocalHostOperationStore operations)
        => ExecuteAsync(() => operations.CancelAsync(operationId));

    public static Task<CallToolResult> CancelOperationLegacy(string operationId, McpOperationStore operations)
        => ExecuteAsync(() => Task.FromResult(operations.Cancel(operationId)));

    private static async Task<CallToolResult> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return McpResults.Success(await operation(), "Completed successfully.");
        }
        catch (Exception exception)
        {
            return McpResults.Error(exception);
        }
    }

    private static async Task<CallToolResult> ExecuteAsync<T>(Func<Task<T>> operation, Func<T, string, CallToolResult> successFactory, string message)
    {
        try
        {
            return successFactory(await operation(), message);
        }
        catch (Exception exception)
        {
            return McpResults.Error(exception);
        }
    }

}

internal static class McpResults
{
    public static CallToolResult OperationStarted(McpOperationModel operation, string message)
    {
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(operation),
            Content = [new TextContentBlock { Text = message }],
        };
    }

    public static CallToolResult Success<T>(T data, string message)
    {
        var correlationId = Guid.NewGuid().ToString("n");
        var envelope = new McpToolEnvelope<T> { CorrelationId = correlationId, Data = data };
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(envelope),
            Content = [new TextContentBlock { Text = message }],
        };
    }

    public static CallToolResult Error(Exception exception)
    {
        var correlationId = Guid.NewGuid().ToString("n");
        var envelope = exception is OpenCodeWorkspaceMcpException mcpException
            ? new McpErrorEnvelope
            {
                CorrelationId = correlationId,
                Code = mcpException.Code,
                Message = mcpException.Message,
                Recommendation = mcpException.Recommendation,
                FailureClassification = mcpException.FailureClassification,
            }
            : exception is OpenCode.Workspace.LocalClient.LocalHostClientException localHostException
            ? new McpErrorEnvelope
            {
                CorrelationId = correlationId,
                Code = localHostException.Code,
                Message = localHostException.Message,
                Recommendation = localHostException.Recommendation,
                FailureClassification = localHostException.GetType().Name,
            }
            : new McpErrorEnvelope
        {
            CorrelationId = correlationId,
            Code = MapErrorCode(exception),
            Message = exception.Message,
            Recommendation = BuildRecommendation(exception),
            FailureClassification = MapFailureClassification(exception),
        };
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(envelope),
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(envelope, OpenCodeWorkspaceMcpContract.JsonOptions) }],
        };
    }

    private static string MapErrorCode(Exception exception)
        => exception.Message switch
        {
            var message when message.Contains("Unknown template", StringComparison.OrdinalIgnoreCase) => "unknown_template",
            var message when message.Contains("Unknown smoke family", StringComparison.OrdinalIgnoreCase) => "invalid_smoke_selection",
            var message when message.Contains("Unsupported", StringComparison.OrdinalIgnoreCase) => "unsupported_template",
            var message when message.Contains("Workspace '", StringComparison.OrdinalIgnoreCase) && message.Contains("was not found", StringComparison.OrdinalIgnoreCase) => "workspace_not_found",
            var message when message.Contains("Operation '", StringComparison.OrdinalIgnoreCase) && message.Contains("was not found", StringComparison.OrdinalIgnoreCase) => "operation_not_found",
            var message when message.Contains("already complete", StringComparison.OrdinalIgnoreCase) => "operation_not_cancellable",
            var message when message.Contains("Artifact was not found", StringComparison.OrdinalIgnoreCase) => "artifact_not_found",
            var message when message.Contains("Artifact resource was not found", StringComparison.OrdinalIgnoreCase) => "invalid_artifact_resource_id",
            var message when message.Contains("outside the allowed root", StringComparison.OrdinalIgnoreCase) => "artifact_outside_allowed_root",
            var message when message.Contains("Invalid workbook", StringComparison.OrdinalIgnoreCase) => "invalid_workbook",
            _ => "invalid_request",
        };

    private static string MapFailureClassification(Exception exception)
        => exception switch
        {
            OperationCanceledException => "cancelled",
            _ => exception.GetType().Name,
        };

    private static string BuildRecommendation(Exception exception)
        => exception.Message.Contains("outside the allowed root", StringComparison.OrdinalIgnoreCase)
            ? "Use workspace or smoke artifact paths only."
            : exception.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase)
                ? "Refresh the local workspace or operation list and retry."
                : string.Empty;
}
