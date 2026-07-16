using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Mcp;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class McpProtocolIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencode-mcp-protocol", Guid.NewGuid().ToString("n"));
    private string _excelRoot = string.Empty;
    private string _workspaceStateRoot = string.Empty;
    private string _smokeArtifactsRoot = string.Empty;

    public Task InitializeAsync()
    {
        _workspaceStateRoot = Path.Combine(_root, "state");
        _smokeArtifactsRoot = Path.Combine(_root, "artifacts", "template-smoke");
        _excelRoot = Path.Combine(_smokeArtifactsRoot, "excel");
        Directory.CreateDirectory(_excelRoot);
        Directory.CreateDirectory(_workspaceStateRoot);
        Directory.CreateDirectory(_smokeArtifactsRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task ProtocolDiscovery_ExposesStableToolsAndSchemas()
    {
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);
        var tools = await harness.Client.ListToolsAsync();
        var names = tools.Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[]
        {
            "cancel_operation",
            "cleanup_smoke_resources",
            "create_workspace",
            "get_operation",
            "get_smoke_artifact",
            "get_workspace",
            "get_workspace_artifact",
            "get_workspace_template",
            "list_operations",
            "list_runtime_resources",
            "list_smoke_artifacts",
            "list_smoke_definitions",
            "list_smoke_resources",
            "list_workspace_artifacts",
            "list_workspace_templates",
            "list_workspaces",
            "process_excel_artifact",
            "provision_workspace",
            "remove_workspace_runtime",
            "run_runtime_doctor",
            "run_smoke",
            "run_smoke_matrix",
            "stop_workspace",
            "validate_workspace",
        }, names);

        var smokeTool = tools.Single(item => item.Name == "run_smoke");
        var required = smokeTool.JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Where(item => item is not null).Cast<string>().ToArray();
        Assert.Contains("templateId", required);
        Assert.DoesNotContain("timeout", required);
        Assert.DoesNotContain("artifactsRoot", required);
        Assert.Contains("smoke run", smokeTool.Description, StringComparison.OrdinalIgnoreCase);

        var resourceTemplates = await harness.Client.ListResourceTemplatesAsync();
        Assert.Contains(resourceTemplates, item => item.UriTemplate == "opencode://templates/{templateId}");
        Assert.Contains(resourceTemplates, item => item.UriTemplate == "opencode://operations/{operationId}");
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task ProtocolSmokeRun_CompletesAndExposesResourcesAndArtifacts()
    {
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);

        var start = await harness.Client.CallToolAsync("run_smoke", new Dictionary<string, object?>
        {
            ["templateId"] = "empty-workspace",
            ["timeout"] = "00:05:00",
            ["artifactsRoot"] = string.Empty,
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(start.StructuredContent!.Value.GetRawText())!;
        Assert.Equal("queued", operation.CurrentPhase);
        Assert.NotEmpty(operation.OperationResourceUri);

        var completed = await harness.WaitForOperationAsync(operation.OperationId, TimeSpan.FromMinutes(4));
        Assert.Equal(McpOperationStatus.Succeeded, completed.Status);
        Assert.NotEmpty(completed.SmokeRunId);
        Assert.NotEmpty(completed.ArtifactDirectory);
        Assert.True(completed.Result.HasValue);
        Assert.Contains("preflightCleanup", completed.PhaseHistory);
        Assert.Contains("creatingWorkspace", completed.PhaseHistory);
        Assert.Contains("provisioning", completed.PhaseHistory);
        Assert.Contains("validating", completed.PhaseHistory);
        Assert.Contains("completed", completed.PhaseHistory);

        var smokeResult = completed.Result!.Value.Deserialize<OpenCode.Workspace.Core.Smoke.WorkspaceSmokeResult>();
        Assert.NotNull(smokeResult);
        Assert.Equal("empty-workspace", smokeResult!.TemplateId);
        Assert.Equal(OpenCode.Workspace.Core.Smoke.WorkspaceSmokeStatus.Passed, smokeResult.Status);
        Assert.True(smokeResult.CleanupVerificationSucceeded);
        Assert.NotEmpty(smokeResult.SummaryJsonPath);
        Assert.NotEmpty(smokeResult.SummaryTextPath);
        Assert.NotEmpty(smokeResult.Validators);

        var operationResource = await harness.Client.ReadResourceAsync($"opencode://operations/{operation.OperationId}");
        Assert.Contains(operation.OperationId, GetTextResource(operationResource), StringComparison.Ordinal);

        var smokeSummary = await harness.Client.ReadResourceAsync($"opencode://smoke/{smokeResult.RunId}/summary");
        Assert.Contains("\"templateId\": \"empty-workspace\"", GetTextResource(smokeSummary), StringComparison.Ordinal);

        var runtimeInventory = await harness.Client.ReadResourceAsync("opencode://runtime/inventory");
        Assert.Contains("\"resources\": []", GetTextResource(runtimeInventory), StringComparison.Ordinal);

        var smokeArtifacts = await harness.Client.CallToolAsync("list_smoke_artifacts", new Dictionary<string, object?>
        {
            ["runId"] = smokeResult.RunId,
            ["relativePath"] = ".",
            ["recursive"] = false,
        });
        var artifactPayload = ReadEnvelope<IReadOnlyList<ArtifactListItem>>(smokeArtifacts);
        Assert.Contains(artifactPayload.Data, item => item.RelativePath == "summary.json");
        Assert.Contains(artifactPayload.Data, item => item.RelativePath == "summary.txt");
        Assert.NotEmpty(completed.RecentEvents);
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task ProtocolGetOperation_SupportsIncrementalAfterSequencePolling()
    {
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);

        var start = await harness.Client.CallToolAsync("run_smoke", new Dictionary<string, object?>
        {
            ["templateId"] = "empty-workspace",
            ["timeout"] = "00:05:00",
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(start.StructuredContent!.Value.GetRawText())!;

        var first = await harness.WaitForOperationConditionAsync(operation.OperationId, TimeSpan.FromMinutes(2), current => current.LastEventSequence > 1);
        Assert.NotEmpty(first.RecentEvents);

        var second = await harness.GetOperationAsync(operation.OperationId, first.LastEventSequence);
        Assert.DoesNotContain(second.RecentEvents, item => item.Sequence <= first.LastEventSequence);

        var completed = await harness.WaitForOperationAsync(operation.OperationId, TimeSpan.FromMinutes(4));
        Assert.Equal(McpOperationStatus.Succeeded, completed.Status);
        Assert.True(completed.LastEventSequence >= first.LastEventSequence);
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "LiveDockerIntegration")]
    public async Task ProtocolSmokeCancellation_CleansUpAndLeavesNoSmokeResources()
    {
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);

        var start = await harness.Client.CallToolAsync("run_smoke", new Dictionary<string, object?>
        {
            ["templateId"] = "web-testing",
            ["timeout"] = "00:05:00",
            ["artifactsRoot"] = string.Empty,
        });
        var operation = JsonSerializer.Deserialize<McpOperationModel>(start.StructuredContent!.Value.GetRawText())!;

        var active = await harness.WaitForOperationConditionAsync(operation.OperationId, TimeSpan.FromMinutes(2), current => current.Status == McpOperationStatus.Running && current.StartedUtc is not null);
        Assert.True(active.CancellationRequested is false);

        var cancel = await harness.Client.CallToolAsync("cancel_operation", new Dictionary<string, object?> { ["operationId"] = operation.OperationId });
        var cancelPayload = ReadEnvelope<McpOperationModel>(cancel);
        Assert.True(cancelPayload.Data.CancellationRequested);

        var completed = await harness.WaitForOperationAsync(operation.OperationId, TimeSpan.FromMinutes(3));
        Assert.Equal(McpOperationStatus.Cancelled, completed.Status);
        Assert.True(completed.CancellationRequested);
        Assert.Equal("cancelled", completed.FailureClassification, ignoreCase: true);

        var smokeResult = completed.Result!.Value.Deserialize<OpenCode.Workspace.Core.Smoke.WorkspaceSmokeResult>();
        Assert.NotNull(smokeResult);
        Assert.Equal(OpenCode.Workspace.Core.Smoke.WorkspaceSmokeStatus.Cancelled, smokeResult!.Status);
        Assert.True(smokeResult.CleanupVerificationSucceeded);
        Assert.NotNull(smokeResult.CleanupResult);
        Assert.True(smokeResult.CleanupResult!.VerificationSucceeded);

        var listSmoke = await harness.Client.CallToolAsync("list_smoke_resources");
        var smokeInventory = ReadEnvelope<RuntimeResourceInventory>(listSmoke);
        Assert.Empty(smokeInventory.Data.Resources);

        var doctor = await harness.Client.CallToolAsync("run_runtime_doctor", new Dictionary<string, object?> { ["owner"] = "smoke" });
        var doctorInventory = ReadEnvelope<RuntimeResourceInventory>(doctor);
        Assert.Empty(doctorInventory.Data.Resources);
        Assert.Empty(doctorInventory.Data.Orphans);
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task ProtocolExcelRoundTrip_ProcessesWorkbookAndReturnsArtifactResource()
    {
        var sourcePath = Path.Combine(_excelRoot, "source.xlsx");
        CreateWorkbook(sourcePath);
        var sourceChecksumBefore = ComputeSha256(sourcePath);

        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);
        CallToolResult result;
        try
        {
            result = await harness.Client.CallToolAsync("process_excel_artifact", new Dictionary<string, object?>
            {
                ["sourcePath"] = sourcePath,
                ["destinationWorkspaceId"] = null,
                ["processingTemplateId"] = "empty-workspace",
                ["outputLogicalName"] = "excel-round-trip",
            });
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, harness.StandardErrorLines), exception);
        }
        var payload = ReadEnvelope<ExcelProcessResultModel>(result);

        Assert.NotEmpty(payload.Data.OutputPath);
        Assert.NotEmpty(payload.Data.OutputChecksumSha256);
        Assert.NotEmpty(payload.Data.SourceChecksumSha256);
        Assert.Equal(sourceChecksumBefore, payload.Data.SourceChecksumSha256);
        Assert.Equal(sourceChecksumBefore, ComputeSha256(sourcePath));

        var outputResource = await harness.Client.ReadResourceAsync(new Uri(payload.Data.ResourceUri).ToString());
        var blob = outputResource.Contents.OfType<BlobResourceContents>().Single();
        Assert.True(blob.Blob.Length > 0);

        using var outputDocument = SpreadsheetDocument.Open(payload.Data.OutputPath, false);
        var sheetNames = outputDocument.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(item => item.Name!.Value).ToArray();
        Assert.Contains("OpenCode Result", sheetNames);
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task ProtocolErrors_AreStable_And_DoNotCrashServer()
    {
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);

        var unknownTemplate = await harness.Client.CallToolAsync("get_workspace_template", new Dictionary<string, object?> { ["templateId"] = "does-not-exist" });
        var unknownTemplateError = ReadError(unknownTemplate);
        Assert.Equal("unknown_template", unknownTemplateError.Code);
        Assert.DoesNotContain(" at ", unknownTemplateError.Message, StringComparison.Ordinal);

        var missingOperation = await harness.Client.CallToolAsync("get_operation", new Dictionary<string, object?> { ["operationId"] = "missing-operation" });
        var missingOperationError = ReadError(missingOperation);
        Assert.Equal("operation_not_found", missingOperationError.Code);

        var invalidArtifactResource = await harness.Client.CallToolAsync("get_smoke_artifact", new Dictionary<string, object?>
        {
            ["runId"] = "missing-run",
            ["relativePath"] = "summary.json",
        });
        var invalidArtifactError = ReadError(invalidArtifactResource);
        Assert.Equal("artifact_not_found", invalidArtifactError.Code);
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task ProtocolWorkspaceAndTemplateResources_AreReadable()
    {
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);
        var templateResource = await harness.Client.ReadResourceAsync("opencode://templates/empty-workspace");
        Assert.Contains("empty-workspace", GetTextResource(templateResource), StringComparison.Ordinal);

        var workspaces = await harness.Client.CallToolAsync("list_workspaces");
        var workspacePayload = ReadEnvelope<IReadOnlyList<WorkspaceRecordModel>>(workspaces);
        if (workspacePayload.Data.Count > 0)
        {
            var workspace = workspacePayload.Data[0];
            var workspaceResource = await harness.Client.ReadResourceAsync($"opencode://workspaces/{workspace.WorkspaceId}");
            Assert.Contains(workspace.WorkspaceId, GetTextResource(workspaceResource), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "McpProtocolIntegration")]
    [Trait("Category", "FastIntegration")]
    public async Task ProtocolWorkspaceLifecycle_Creates_Validates_Stops_And_Removes_Runtime()
    {
        var workspaceParent = Path.Combine(_root, "workspaces");
        Directory.CreateDirectory(workspaceParent);
        await using var harness = await McpProtocolHarness.StartAsync(_workspaceStateRoot, _smokeArtifactsRoot);

        var create = await harness.Client.CallToolAsync("create_workspace", new Dictionary<string, object?>
        {
            ["templateId"] = "empty-workspace",
            ["workspaceName"] = "mcp-api-parity",
            ["destinationRoot"] = workspaceParent,
        });
        var created = ReadEnvelope<WorkspaceRecordModel>(create).Data;
        Assert.True(Directory.Exists(created.WorkspaceRoot));

        var validate = await harness.Client.CallToolAsync("validate_workspace", new Dictionary<string, object?> { ["workspaceId"] = created.WorkspaceId });
        Assert.Equal(created.WorkspaceId, ReadEnvelope<WorkspaceRecordModel>(validate).Data.WorkspaceId);

        var stop = await harness.Client.CallToolAsync("stop_workspace", new Dictionary<string, object?> { ["workspaceId"] = created.WorkspaceId });
        Assert.Equal(created.WorkspaceId, ReadEnvelope<WorkspaceRecordModel>(stop).Data.WorkspaceId);

        var remove = await harness.Client.CallToolAsync("remove_workspace_runtime", new Dictionary<string, object?> { ["workspaceId"] = created.WorkspaceId });
        Assert.Equal(created.WorkspaceId, ReadEnvelope<WorkspaceRecordModel>(remove).Data.WorkspaceId);
    }

    private static McpToolEnvelope<T> ReadEnvelope<T>(CallToolResult result)
        => JsonSerializer.Deserialize<McpToolEnvelope<T>>(result.StructuredContent!.Value.GetRawText())!;

    private static McpErrorEnvelope ReadError(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured)
        {
            return JsonSerializer.Deserialize<McpErrorEnvelope>(structured.GetRawText())!;
        }

        return JsonSerializer.Deserialize<McpErrorEnvelope>(result.Content.OfType<TextContentBlock>().First().Text)!;
    }

    private static string GetTextResource(ReadResourceResult result)
        => result.Contents.OfType<TextResourceContents>().Single().Text;

    private static string ComputeSha256(string path)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void CreateWorkbook(string path)
    {
        using var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData());
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Sheet1" });
        workbookPart.Workbook.Save();
    }
}

internal sealed class McpProtocolHarness : IAsyncDisposable
{
    private readonly List<string> _stderr;

    private McpProtocolHarness(StdioClientTransport transport, McpClient client, List<string> stderr)
    {
        Transport = transport;
        Client = client;
        _stderr = stderr;
    }

    public StdioClientTransport Transport { get; }
    public McpClient Client { get; }
    public IReadOnlyList<string> StandardErrorLines => _stderr;

    public static async Task<McpProtocolHarness> StartAsync(string workspaceStateRoot, string smokeArtifactsRoot)
    {
        var stderr = new List<string>();
        var launch = McpHostLaunch.Resolve();
        var transport = McpHostLaunch.CreateTransport(
            line =>
            {
                lock (stderr)
                {
                    stderr.Add(line);
                }
            },
            new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = Path.Combine(TestPaths.RepositoryRoot, "catalog"),
                ["mcp__workspaceStateRoot"] = workspaceStateRoot,
                ["mcp__smokeArtifactsRoot"] = smokeArtifactsRoot,
            });

        McpClient client;
        try
        {
            client = await McpClient.CreateAsync(transport);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(McpHostLaunch.BuildStartupFailureMessage(launch, stderr, exception), exception);
        }

        return new McpProtocolHarness(transport, client, stderr);
    }

    public async Task<McpOperationModel> WaitForOperationAsync(string operationId, TimeSpan timeout, Action<McpOperationModel>? progress = null)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var operation = await GetOperationAsync(operationId);
            progress?.Invoke(operation);
            if (operation.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                return operation;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Operation '{operationId}' did not complete within {timeout}.");
    }

    public async Task<McpOperationModel> WaitForOperationConditionAsync(string operationId, TimeSpan timeout, Func<McpOperationModel, bool> predicate)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            var operation = await GetOperationAsync(operationId);
            if (predicate(operation))
            {
                return operation;
            }

            if (operation.Status is McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                return operation;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Operation '{operationId}' did not reach the expected condition.");
    }

    public async Task<McpOperationModel> GetOperationAsync(string operationId, long? afterSequence = null)
    {
        var request = new Dictionary<string, object?> { ["operationId"] = operationId };
        if (afterSequence.HasValue)
        {
            request["afterSequence"] = afterSequence.Value;
        }

        var result = await Client.CallToolAsync("get_operation", request);
        return JsonSerializer.Deserialize<McpToolEnvelope<McpOperationModel>>(result.StructuredContent!.Value.GetRawText())!.Data;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await TransportDisposal.TryDisposeAsync(Transport);
    }
}
