using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class CanonicalOperationCrossAdapterIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("Category", "CrossAdapterIntegration")]
    public async Task McpStartedOperation_IsVisibleAndControllableThroughAvaloniaLocalHostPath()
    {
        await using var scope = new LocalHostTeardownScope();
        TeardownAssert.AssertNoLiveState(scope.Identity);
        await using var mcp = await scope.StartMcpAsync("mcp-to-avalonia", useTestOperation: true);
        var host = await scope.WaitForIdentityAsync(mcp.StandardErrorLines);
        var controller = await scope.WaitForControllerAsync(host, mcp.Report.ProcessId, mcp.StandardErrorLines);
        await using var avalonia = scope.CreateAvaloniaLocalHostService();
        await avalonia.ConnectAsync();

        Assert.Equal(host.InstanceId, avalonia.LocalHostInstanceId);
        var started = await CallMcpOperationAsync(mcp, 2, "run_smoke", new { templateId = "empty-workspace" });
        AssertMcpStartRecord(started, controller.ControllerSessionId);

        var desktopObserved = await avalonia.GetOperationAsync(started.OperationId);
        AssertImmutableParity(started, desktopObserved);
        AssertEvolvingParity(started, desktopObserved);
        Assert.Equal(controller.ClientInstanceId, desktopObserved.InitiatedBy.ClientInstanceId);

        var cancelled = await avalonia.CancelOperationAsync(started.OperationId);
        Assert.Equal(controller.ControllerSessionId, cancelled.InitiatedBy.ControllerSessionId);
        var mcpTerminal = await WaitForMcpTerminalAsync(mcp, started.OperationId, 3);
        var desktopTerminal = await WaitForDesktopTerminalAsync(avalonia, started.OperationId);

        Assert.Equal(McpOperationStatus.Cancelled, mcpTerminal.Status);
        AssertTerminalParity(mcpTerminal, desktopTerminal);
        Assert.Equal(controller.ControllerSessionId, desktopTerminal.InitiatedBy.ControllerSessionId);
        Assert.Equal(controller.ClientInstanceId, desktopTerminal.InitiatedBy.ClientInstanceId);

        await mcp.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
        await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controller.ControllerSessionId, scope.Identity, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertProcessExitedAsync(host, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertDescriptorNotLiveAsync(scope.Identity, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(scope.Identity, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "CrossAdapterIntegration")]
    public async Task AvaloniaStartedOperation_IsVisibleAndControllableThroughMcp()
    {
        await using var scope = new LocalHostTeardownScope();
        TeardownAssert.AssertNoLiveState(scope.Identity);
        await using var externalHost = await scope.StartExternalHostAsync(useTestOperation: true);
        var host = await scope.WaitForIdentityAsync(externalHost.StandardErrorLines);
        await using var avalonia = scope.CreateAvaloniaLocalHostService();
        await avalonia.ConnectAsync();
        Assert.Equal(host.InstanceId, avalonia.LocalHostInstanceId);

        var started = await avalonia.StartSmokeRunAsync("empty-workspace");
        Assert.Equal("avalonia", started.InitiatedBy.Kind);
        await using var mcp = await scope.StartMcpAsync("avalonia-to-mcp", useTestOperation: true);
        var controller = await scope.WaitForControllerAsync(host, mcp.Report.ProcessId, mcp.StandardErrorLines);
        var mcpObserved = await GetMcpOperationAsync(mcp, started.OperationId, 2);
        var desktopObserved = await avalonia.GetOperationAsync(started.OperationId);

        AssertImmutableParity(mcpObserved, desktopObserved);
        AssertEvolvingParity(mcpObserved, desktopObserved);
        Assert.Equal(desktopObserved.InitiatedBy.ControllerSessionId, mcpObserved.ControllerSessionId);
        Assert.NotEqual(controller.ControllerSessionId, mcpObserved.ControllerSessionId);

        await CallMcpOperationAsync(mcp, 3, "cancel_operation", new { operationId = started.OperationId });
        var desktopTerminal = await WaitForDesktopTerminalAsync(avalonia, started.OperationId);
        var mcpTerminal = await WaitForMcpTerminalAsync(mcp, started.OperationId, 4);

        Assert.Equal(WorkspaceOperationStatus.Cancelled, desktopTerminal.Status);
        AssertTerminalParity(mcpTerminal, desktopTerminal);
        Assert.Equal(started.InitiatedBy.ControllerSessionId, desktopTerminal.InitiatedBy.ControllerSessionId);

        await mcp.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
        await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controller.ControllerSessionId, scope.Identity, mcp.StandardErrorLines, externalHost.StandardErrorLines, Timeout, CancellationToken.None);
        await externalHost.ForceKillAsync(Timeout);
        await TeardownAssert.AssertProcessExitedAsync(host, mcp.StandardErrorLines, externalHost.StandardErrorLines, Timeout, CancellationToken.None);
        await TeardownAssert.AssertDescriptorNotLiveAsync(scope.Identity, mcp.StandardErrorLines, externalHost.StandardErrorLines, Timeout, CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(scope.Identity, mcp.StandardErrorLines, externalHost.StandardErrorLines, Timeout, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "CrossAdapterIntegration")]
    public async Task McpStartedExcelOperation_HasCanonicalResultAndArtifactParityThroughLocalClient()
    {
        await using var scope = new LocalHostTeardownScope();
        TeardownAssert.AssertNoLiveState(scope.Identity);
        var sourcePath = Path.Combine(scope.ArtifactsRoot, "excel", "input.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        CreateWorkbook(sourcePath);

        await using var mcp = await scope.StartMcpAsync("mcp-excel-parity");
        var host = await scope.WaitForIdentityAsync(mcp.StandardErrorLines);
        var controller = await scope.WaitForControllerAsync(host, mcp.Report.ProcessId, mcp.StandardErrorLines);
        await using var localClient = scope.Client(host);

        var started = await CallMcpOperationAsync(mcp, 2, "process_excel_artifact", new
        {
            sourcePath,
            processingTemplateId = "shared",
            outputLogicalName = "processed",
        });
        Assert.Equal("process_excel_artifact", started.Kind);
        Assert.Equal(controller.ControllerSessionId, started.ControllerSessionId);

        var canonical = await WaitForLocalClientTerminalAsync(localClient, started.OperationId);
        var throughMcp = await WaitForMcpTerminalAsync(mcp, started.OperationId, 3);
        AssertTerminalParity(throughMcp, canonical);
        Assert.Equal(controller.ControllerSessionId, canonical.InitiatedBy.ControllerSessionId);
        Assert.Equal(controller.ClientInstanceId, canonical.InitiatedBy.ClientInstanceId);

        var excel = canonical.Result!.Value.Deserialize<OpenCode.Workspace.LocalClient.ExcelProcessResultModel>(LocalHostContract.JsonOptions)!;
        Assert.True(File.Exists(excel.OutputPath));
        Assert.Contains(canonical.ArtifactReferences, item => item.SafeLocalReference == excel.OutputPath && item.Kind == "excel-workbook");
        var artifact = await localClient.ReadArtifactByResourceUriAsync(excel.ResourceUri);
        Assert.Equal(excel.OutputChecksumSha256, artifact.ChecksumSha256);
        Assert.Equal(canonical.ArtifactReferences.Select(item => item.SafeLocalReference), throughMcp.ArtifactReferences);

        await mcp.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
        await TeardownAssert.AssertControllerDisconnectedAsync(localClient, controller.ControllerSessionId, scope.Identity, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertProcessExitedAsync(host, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertDescriptorNotLiveAsync(scope.Identity, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(scope.Identity, mcp.StandardErrorLines, [], Timeout, CancellationToken.None);
    }

    private static async Task<McpOperationModel> WaitForMcpTerminalAsync(PackagedProcessHarness mcp, string operationId, int requestId)
    {
        var stopwatch = Stopwatch.StartNew();
        McpOperationModel? last = null;
        while (stopwatch.Elapsed < Timeout)
        {
            var operation = await GetMcpOperationAsync(mcp, operationId, requestId++);
            last = operation;
            if (operation.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled)
            {
                return operation;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Operation '{operationId}' did not reach a terminal state through MCP stdio. Last status={last?.Status} phase={last?.CurrentPhase} sequence={last?.LastEventSequence} stdout={string.Join(" | ", mcp.StandardOutputLines.TakeLast(20))}");
    }

    private static Task<McpOperationModel> GetMcpOperationAsync(PackagedProcessHarness mcp, string operationId, int requestId)
        => CallMcpOperationAsync(mcp, requestId, "get_operation", new { operationId });

    private static async Task<McpOperationModel> CallMcpOperationAsync(PackagedProcessHarness mcp, int requestId, string toolName, object arguments)
    {
        await mcp.WriteStandardInputAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = "tools/call",
            @params = new { name = toolName, arguments },
        }));

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            foreach (var line in mcp.StandardOutputLines)
            {
                if (!TryReadMcpOperation(line, requestId, out var operation))
                {
                    continue;
                }
                return operation;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"MCP tool '{toolName}' did not return request {requestId}. stdout={string.Join(" | ", mcp.StandardOutputLines.TakeLast(20))} stderr={string.Join(" | ", mcp.StandardErrorLines.TakeLast(20))}");
    }

    private static bool TryReadMcpOperation(string line, int requestId, out McpOperationModel operation)
    {
        operation = new McpOperationModel();
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt32() != requestId || !document.RootElement.TryGetProperty("result", out var result))
            {
                return false;
            }

            var payload = result.TryGetProperty("structuredContent", out var structured)
                ? structured
                : result.GetProperty("content")[0].GetProperty("text");
            var parsed = payload.TryGetProperty("Data", out var data)
                ? JsonSerializer.Deserialize<McpOperationModel>(data.GetRawText())
                : JsonSerializer.Deserialize<McpOperationModel>(payload.GetRawText());
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.OperationId))
            {
                throw new InvalidOperationException("MCP did not return a typed operation record.");
            }
            operation = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AssertMcpStartRecord(McpOperationModel operation, string controllerSessionId)
    {
        Assert.False(string.IsNullOrWhiteSpace(operation.OperationId));
        Assert.NotNull(operation.WorkspaceId);
        Assert.Equal("run_smoke", operation.Kind);
        Assert.NotEqual(default, operation.CreatedUtc);
        Assert.NotEqual(default, operation.UpdatedUtc);
        Assert.Equal(controllerSessionId, operation.ControllerSessionId);
        Assert.True(operation.LastEventSequence >= 0);
        Assert.NotNull(operation.CurrentPhase);
        Assert.NotNull(operation.ProgressMessage);
    }

    private static void AssertImmutableParity(McpOperationModel mcp, WorkspaceOperationRecord desktop)
    {
        Assert.Equal(mcp.OperationId, desktop.OperationId);
        Assert.Equal(mcp.WorkspaceId, desktop.WorkspaceId);
        Assert.Equal(mcp.Kind, desktop.OperationKind);
        Assert.Equal(mcp.CreatedUtc, desktop.CreatedUtc);
        Assert.Equal(mcp.ControllerSessionId, desktop.InitiatedBy.ControllerSessionId);
    }

    private static void AssertEvolvingParity(McpOperationModel earlier, WorkspaceOperationRecord later)
    {
        Assert.True(StatusRank(later.Status) >= StatusRank(earlier.Status));
        Assert.True(later.LastUpdatedUtc >= earlier.UpdatedUtc);
        Assert.True(later.LastEventSequence >= earlier.LastEventSequence);
        Assert.NotNull(later.CurrentPhase);
        Assert.NotNull(later.ProgressMessage);
        Assert.All(later.RecentEvents, item => Assert.True(item.Sequence <= later.LastEventSequence));
    }

    private static void AssertTerminalParity(McpOperationModel mcp, WorkspaceOperationRecord desktop)
    {
        Assert.Equal(ToMcpStatus(desktop.Status), mcp.Status);
        Assert.Equal(mcp.FailureClassification, desktop.OriginalFailure?.Classification ?? (desktop.Status == WorkspaceOperationStatus.Cancelled ? "cancelled" : string.Empty));
        Assert.Equal(mcp.FailureMessage, desktop.OriginalFailure?.Message ?? string.Empty);
        Assert.Equal(mcp.CleanupFailureClassification, desktop.CleanupFailure?.Classification ?? string.Empty);
        Assert.Equal(mcp.CleanupFailureMessage, desktop.CleanupFailure?.Message ?? string.Empty);
        Assert.Equal(mcp.ArtifactReferences, desktop.ArtifactReferences.Select(item => item.SafeLocalReference).Where(item => !string.IsNullOrWhiteSpace(item)));
        Assert.Equal(desktop.Result.HasValue, mcp.Result.HasValue);
        if (desktop.Result.HasValue)
        {
            Assert.True(JsonElement.DeepEquals(desktop.Result.Value, mcp.Result!.Value));
        }
        Assert.Equal(desktop.LastEventSequence, mcp.LastEventSequence);
        Assert.All(mcp.RecentEvents, item => Assert.True(item.Sequence <= mcp.LastEventSequence));
    }

    private static async Task<WorkspaceOperationRecord> WaitForDesktopTerminalAsync(WorkspaceLocalHostApplicationService avalonia, string operationId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            var operation = await avalonia.GetOperationAsync(operationId);
            if (operation.Status is WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled)
            {
                return operation;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Operation '{operationId}' did not reach a terminal state through the Avalonia LocalHost service.");
    }

    private static async Task<WorkspaceOperationRecord> WaitForLocalClientTerminalAsync(LocalHostClient client, string operationId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            var operation = await client.GetOperationAsync(operationId);
            if (operation.Status is WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled)
            {
                return operation;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Operation '{operationId}' did not reach a terminal state through LocalClient.");
    }

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

    private static int StatusRank(WorkspaceOperationStatus status)
        => status switch
        {
            WorkspaceOperationStatus.Pending => 0,
            WorkspaceOperationStatus.Running => 1,
            WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled or WorkspaceOperationStatus.Interrupted => 2,
            _ => 0,
        };

    private static int StatusRank(McpOperationStatus status)
        => status switch
        {
            McpOperationStatus.Pending => 0,
            McpOperationStatus.Running => 1,
            _ => 2,
        };

    private static McpOperationStatus ToMcpStatus(WorkspaceOperationStatus status)
        => status switch
        {
            WorkspaceOperationStatus.Running => McpOperationStatus.Running,
            WorkspaceOperationStatus.Succeeded => McpOperationStatus.Succeeded,
            WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Interrupted => McpOperationStatus.Failed,
            WorkspaceOperationStatus.Cancelled => McpOperationStatus.Cancelled,
            _ => McpOperationStatus.Pending,
        };
}
