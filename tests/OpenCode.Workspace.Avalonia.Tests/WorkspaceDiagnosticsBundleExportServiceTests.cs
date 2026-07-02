using System.IO.Compression;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;
using Xunit;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class WorkspaceDiagnosticsBundleExportServiceTests
{
    [Fact]
    public async Task ExportAsync_WritesExpectedBundleEntries()
    {
        var service = new WorkspaceDiagnosticsBundleExportService();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"diagnostics-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var archivePath = Path.Combine(tempRoot, "diagnostics.zip");

        try
        {
            await service.ExportAsync(CreateSession(), archivePath);

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.NotNull(archive.GetEntry("diagnostics-summary.txt"));
            Assert.NotNull(archive.GetEntry("diagnostics-full-log.txt"));
            Assert.NotNull(archive.GetEntry("readiness.json"));
            Assert.NotNull(archive.GetEntry("provisioning-health.json"));

            var summary = await ReadEntryAsync(archive, "diagnostics-summary.txt");
            var fullLog = await ReadEntryAsync(archive, "diagnostics-full-log.txt");
            var readiness = await ReadEntryAsync(archive, "readiness.json");
            var health = await ReadEntryAsync(archive, "provisioning-health.json");

            Assert.Contains("Workspace Diagnostics", summary, StringComparison.Ordinal);
            Assert.Contains("Timeline / Entries", fullLog, StringComparison.Ordinal);
            Assert.Contains("\"PrimaryAction\":", readiness, StringComparison.Ordinal);
            Assert.Contains("\"Reason\": \"XDB status = INVALID\"", health, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_OmitsOptionalJsonFilesWhenUnavailable()
    {
        var service = new WorkspaceDiagnosticsBundleExportService();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"diagnostics-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var archivePath = Path.Combine(tempRoot, "diagnostics.zip");
        var session = CreateSession();

        try
        {
            await service.ExportAsync(new WorkspaceDiagnosticsSession
            {
                WorkspaceName = session.WorkspaceName,
                WorkspaceRootPath = session.WorkspaceRootPath,
                OperationName = session.OperationName,
                Mode = session.Mode,
                Status = session.Status,
                Summary = session.Summary,
                StartedUtc = session.StartedUtc,
                CompletedUtc = session.CompletedUtc,
                Recommendation = session.Recommendation,
                FailureSummary = session.FailureSummary,
                AttemptedSteps = session.AttemptedSteps,
                Entries = session.Entries,
                BundleInfo = session.BundleInfo,
            }, archivePath);

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.NotNull(archive.GetEntry("diagnostics-summary.txt"));
            Assert.NotNull(archive.GetEntry("diagnostics-full-log.txt"));
            Assert.Null(archive.GetEntry("readiness.json"));
            Assert.Null(archive.GetEntry("provisioning-health.json"));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static WorkspaceDiagnosticsSession CreateSession()
        => new()
        {
            WorkspaceName = "alpha",
            WorkspaceRootPath = "/workspace/alpha",
            OperationName = "Open Workspace",
            Mode = WorkspaceDiagnosticsMode.Diagnostics,
            Status = WorkspaceDiagnosticsStatus.Failed,
            Summary = "Workspace provisioning stopped.",
            StartedUtc = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            CompletedUtc = new DateTimeOffset(2026, 7, 2, 12, 5, 0, TimeSpan.Zero),
            Recommendation = WorkspaceNextActionRecommendation.RebuildRuntime,
            Readiness = new WorkspaceReadinessSnapshot
            {
                Summary = "Workspace unavailable.",
                PrimaryAction = WorkspacePrimaryAction.OpenWorkspace,
            },
            ProvisioningHealth = new WorkspaceProvisioningHealthRecord
            {
                Succeeded = false,
                Summary = "Workspace provisioning stopped.",
                Reason = "XDB status = INVALID",
                Evidence = "Oracle validation failed.",
                Timestamp = DateTimeOffset.UtcNow,
            },
            FailureSummary = new WorkspaceFailureSummary
            {
                Summary = "Provisioning failed.",
                Reason = "XDB status = INVALID",
                Evidence = "Oracle validation failed.",
            },
            AttemptedSteps =
            [
                new WorkspaceAttemptResult
                {
                    Step = WorkspaceAttemptStep.SafeRepair,
                    Succeeded = false,
                    Summary = "safe repair",
                    Timestamp = new DateTimeOffset(2026, 7, 2, 12, 2, 0, TimeSpan.Zero),
                },
            ],
            Entries =
            [
                new WorkspaceDiagnosticsEntry
                {
                    Timestamp = new DateTimeOffset(2026, 7, 2, 12, 3, 0, TimeSpan.Zero),
                    Kind = WorkspaceDiagnosticsEntryKind.Error,
                    Message = "XDB status = INVALID",
                    Source = "transcript",
                    IsFailureEvidence = true,
                },
            ],
            BundleInfo = new WorkspaceDiagnosticsBundleInfo
            {
                SuggestedFileName = "alpha-open-workspace-diagnostics.zip",
                CanExportToFile = true,
            },
        };

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Entry '{name}' was not found.");
        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync();
    }
}
