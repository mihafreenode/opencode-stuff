using Microsoft.Extensions.Logging.Abstractions;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Mcp;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class McpServiceIntegrationTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "opencode-mcp-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Service_RejectsArtifactTraversal_And_ProcessesExcelArtifacts()
    {
        Directory.CreateDirectory(_tempRoot);
        var workspaceStateRoot = Path.Combine(_tempRoot, "state");
        var smokeRoot = Path.Combine(_tempRoot, "smoke");
        var workspaceRoot = Path.Combine(_tempRoot, "workspaces");
        var service = new OpenCodeWorkspaceMcpService(
            new OpenCodeWorkspaceMcpOptions(),
            NullLogger<OpenCodeWorkspaceMcpService>.Instance,
            catalogRoot: Path.Combine(TestPaths.RepositoryRoot, "catalog"),
            workspaceStateRoot: workspaceStateRoot,
            smokeArtifactsRoot: smokeRoot);

        var created = await service.CreateWorkspaceAsync("empty-workspace", "mcp-demo", workspaceRoot);
        var sampleTextPath = Path.Combine(created.Snapshot.Paths.ArtifactsPath, "notes.txt");
        Directory.CreateDirectory(created.Snapshot.Paths.ArtifactsPath);
        await File.WriteAllTextAsync(sampleTextPath, "artifact text");
        var workbookPath = Path.Combine(created.Snapshot.Paths.ArtifactsPath, "input.xlsx");
        CreateWorkbook(workbookPath);

        await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => service.GetWorkspaceArtifactAsync(created.WorkspaceId, "../outside.txt"));

        var artifact = await service.GetWorkspaceArtifactAsync(created.WorkspaceId, "notes.txt");
        Assert.True(artifact.IsTextInline);
        Assert.Equal("artifact text", artifact.Text);

        var processed = await service.ProcessExcelArtifactAsync(workbookPath, created.WorkspaceId, null, "processed-workbook");
        Assert.EndsWith("processed-workbook.xlsx", processed.OutputPath, StringComparison.Ordinal);
        Assert.NotEmpty(processed.OutputChecksumSha256);
        Assert.NotEmpty(processed.SourceChecksumSha256);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void CreateWorkbook(string path)
    {
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
        var worksheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
        worksheetPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData());
        var sheets = workbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
        sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Sheet1",
        });
        workbookPart.Workbook.Save();
    }
}
