using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceRepairabilityAnalyzerTests
{
    [Fact]
    public void Analyze_XdbFailureWithResetRecommendation_ReturnsCleanupRepair()
    {
        var assessment = WorkspaceRepairabilityAnalyzer.Analyze(CreateSnapshot(), CreateHealth("Reset Runtime."));

        Assert.Equal(WorkspaceRepairability.CleanupRepair, assessment.Classification);
        Assert.Equal("Reset Runtime.", assessment.RecommendedNextAction);
    }

    [Fact]
    public void Analyze_XdbFailureWithManualRecommendation_ReturnsManualRepair()
    {
        var recommendation = "Investigate the Oracle XDB compilation errors or restore a known-good backup.";

        var assessment = WorkspaceRepairabilityAnalyzer.Analyze(CreateSnapshot(), CreateHealth(recommendation));

        Assert.Equal(WorkspaceRepairability.ManualRepair, assessment.Classification);
        Assert.Equal(recommendation, assessment.RecommendedNextAction);
    }

    private static WorkspaceSnapshot CreateSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repairability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "oracle-apexlang-demo",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
                Services = ["oracle-demo", "oracle-ords"],
            },
            Paths = WorkspacePathBuilder.Build(root),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Protected",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "oracle-apexlang-demo", State = WorkspaceSessionState.Unknown },
        };
    }

    private static WorkspaceProvisioningHealthRecord CreateHealth(string recommendation)
        => new()
        {
            Reason = "Oracle XML Database (XDB) is invalid.",
            Evidence = "XDB status = INVALID",
            RecommendedAction = recommendation,
        };
}
