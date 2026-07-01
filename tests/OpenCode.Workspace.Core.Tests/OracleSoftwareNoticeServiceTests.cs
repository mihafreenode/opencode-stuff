using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleSoftwareNoticeServiceTests
{
    [Fact]
    public void OracleTemplates_RequireAcknowledgement()
    {
        var service = CreateService();

        Assert.True(service.RequiresAcknowledgement(new TemplateManifest { Id = OracleWorkspaceFamily.OraclePlSqlTemplateId, DisplayName = "Oracle PL/SQL Demo" }));
        Assert.True(service.RequiresAcknowledgement(new TemplateManifest { Id = OracleWorkspaceFamily.OracleApexTemplateId, DisplayName = "Oracle APEX Demo" }));
        Assert.True(service.RequiresAcknowledgement(new TemplateManifest { Id = OracleWorkspaceFamily.OracleApexLangTemplateId, DisplayName = "Oracle APEXlang Demo" }));
        Assert.True(service.RequiresAcknowledgement(new TemplateManifest { Id = "inherited-oracle", DisplayName = "Inherited Oracle", Features = [OracleWorkspaceFamily.OracleBaseFeatureId] }));
        Assert.True(service.RequiresAcknowledgement(new TemplateManifest { Id = "inherited-oracle-apex", DisplayName = "Inherited Oracle APEX", Features = [OracleWorkspaceFamily.OracleApexFeatureId], Services = [OracleWorkspaceFamily.OracleOrdsServiceId] }));
    }

    [Fact]
    public void NonOracleTemplates_DoNotRequireAcknowledgement()
    {
        var service = CreateService();

        Assert.False(service.RequiresAcknowledgement(new TemplateManifest { Id = "general-development", DisplayName = "General Development" }));
    }

    [Fact]
    public void BuildPrompt_IncludesExplicitAcknowledgementLabel()
    {
        var service = CreateService();

        var prompt = service.BuildPrompt(new TemplateManifest { Id = OracleWorkspaceFamily.OraclePlSqlTemplateId, DisplayName = "Oracle PL/SQL Demo" }, "oracle-demo");

        Assert.Contains("Oracle-provided software", prompt.AcknowledgementLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("licensing", prompt.AcknowledgementLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApexLangWorkspace_InheritsOracleNoticeRequirement()
    {
        var service = CreateService();
        var snapshot = CreateSnapshot(oracleNoticeShown: false, OracleWorkspaceFamily.OracleApexLangFeatureId, OracleWorkspaceFamily.OracleOrdsServiceId);

        Assert.True(service.RequiresAcknowledgement(snapshot));
        var prompt = service.BuildPrompt(snapshot);
        Assert.Contains("APEX", string.Join(" ", prompt.Facts), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("licensing", string.Join(" ", prompt.Facts), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acknowledge_PersistsRecordFlag()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oracle-notice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);
        try
        {
            var repository = new WorkspaceRepository(appDataRoot);
            var record = new WorkspaceRecord
            {
                Name = "oracle-demo",
                RootPath = "C:\\temp\\oracle-demo",
                RepositoryPath = "C:\\temp\\oracle-demo",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            };
            repository.Save(record);
            var service = new OracleSoftwareNoticeService(repository);

            var updated = service.Acknowledge(record);

            Assert.True(updated.OracleSoftwareNoticeShown);
            Assert.True(repository.LoadAll().Single().OracleSoftwareNoticeShown);
        }
        finally
        {
            if (Directory.Exists(appDataRoot)) Directory.Delete(appDataRoot, true);
        }
    }

    private static OracleSoftwareNoticeService CreateService()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"oracle-notice-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataRoot);
        return new OracleSoftwareNoticeService(new WorkspaceRepository(appDataRoot));
    }

    private static WorkspaceSnapshot CreateSnapshot(bool oracleNoticeShown, params string[] featuresAndServices)
    {
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "oracle-demo",
                RootPath = "C:\\temp\\oracle-demo",
                RepositoryPath = "C:\\temp\\oracle-demo",
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
                OracleSoftwareNoticeShown = oracleNoticeShown,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-demo" },
                Features = featuresAndServices.Where(item => item.Contains("feature", StringComparison.OrdinalIgnoreCase) == false && item != OracleWorkspaceFamily.OracleOrdsServiceId).ToList(),
                Services = featuresAndServices.Where(item => item == OracleWorkspaceFamily.OracleOrdsServiceId).ToList(),
            },
            Paths = WorkspacePathBuilder.Build(Path.Combine(Path.GetTempPath(), $"oracle-demo-{Guid.NewGuid():N}")),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.PartiallyProtected,
                Headline = "Ready",
                Message = "Ready",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "oracle", State = WorkspaceSessionState.Unknown },
            UpdateRequired = true,
            Readiness = new WorkspaceReadinessSnapshot(),
        };
    }
}
