using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class OracleLicensingNoticeTests
{
    [Fact]
    public async Task OracleNotice_CancelBlocksAcknowledgementAndKeepsRecordUnchanged()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-oracle-notice-cancel-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-oracle-notice-cancel-workspace-{Guid.NewGuid():N}");
        var viewModel = await CreateOracleWorkspaceAsync(appDataRoot, workspaceRoot, "oracle-notice-cancel");
        var snapshot = viewModel.Workspaces.Single().Snapshot;

        viewModel.OracleNoticePromptOverrideForTests = _ => AppDialogResult.No;

        var allowed = viewModel.EnsureOracleSoftwareNoticeReviewedForTests(snapshot);

        Assert.False(allowed);
        var indexJson = File.ReadAllText(Path.Combine(appDataRoot, "workspaces.json"));
        Assert.DoesNotContain("\"OracleSoftwareNoticeShown\": true", indexJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OracleNotice_ContinueStoresAcknowledgementAndDoesNotRepeatForSameWorkspace()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-oracle-notice-continue-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-oracle-notice-continue-workspace-{Guid.NewGuid():N}");
        var viewModel = await CreateOracleWorkspaceAsync(appDataRoot, workspaceRoot, "oracle-notice-continue");
        var snapshot = viewModel.Workspaces.Single().Snapshot;
        var promptCount = 0;

        viewModel.OracleNoticePromptOverrideForTests = message =>
        {
            promptCount++;
            Assert.Contains("Oracle software is subject to Oracle licensing terms.", message);
            Assert.DoesNotContain("accept on your behalf", message, StringComparison.OrdinalIgnoreCase);
            return AppDialogResult.Yes;
        };

        var firstAllowed = viewModel.EnsureOracleSoftwareNoticeReviewedForTests(snapshot);
        var secondAllowed = viewModel.EnsureOracleSoftwareNoticeReviewedForTests(snapshot);

        Assert.True(firstAllowed);
        Assert.True(secondAllowed);
        Assert.Equal(1, promptCount);
        Assert.Contains("Oracle software is subject to Oracle licensing terms.", viewModel.LastOracleNoticeMessageForTests);
        var indexJson = File.ReadAllText(Path.Combine(appDataRoot, "workspaces.json"));
        Assert.Contains("\"OracleSoftwareNoticeShown\": true", indexJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OracleNotice_AlreadyAcknowledgedWorkspaceRecordSkipsPrompt()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-oracle-notice-skips-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-oracle-notice-skips-workspace-{Guid.NewGuid():N}");
        var viewModel = await CreateOracleWorkspaceAsync(appDataRoot, workspaceRoot, "oracle-notice-skips");
        var snapshot = viewModel.Workspaces.Single().Snapshot;
        var updatedSnapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = snapshot.Record.Name,
                RootPath = snapshot.Record.RootPath,
                RepositoryPath = snapshot.Record.RepositoryPath,
                ConfigurationPath = snapshot.Record.ConfigurationPath,
                SourceType = snapshot.Record.SourceType,
                ImportedFromExistingCheckout = snapshot.Record.ImportedFromExistingCheckout,
                OriginalDefaultBranch = snapshot.Record.OriginalDefaultBranch,
                SelectedWorkspaceBranch = snapshot.Record.SelectedWorkspaceBranch,
                RemoteOriginUrl = snapshot.Record.RemoteOriginUrl,
                CreatedUtc = snapshot.Record.CreatedUtc,
                LastOpenedUtc = snapshot.Record.LastOpenedUtc,
                LastPreparedUtc = snapshot.Record.LastPreparedUtc,
                OracleSoftwareNoticeShown = true,
                LastOperationName = snapshot.Record.LastOperationName,
                LastOperationResult = snapshot.Record.LastOperationResult,
                LastOperationSucceeded = snapshot.Record.LastOperationSucceeded,
                LastOperationUtc = snapshot.Record.LastOperationUtc,
            },
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            UpdateRequired = snapshot.UpdateRequired,
        };

        var promptCount = 0;
        viewModel.OracleNoticePromptOverrideForTests = _ =>
        {
            promptCount++;
            return AppDialogResult.Yes;
        };

        var allowed = viewModel.EnsureOracleSoftwareNoticeReviewedForTests(updatedSnapshot);

        Assert.True(allowed);
        Assert.Equal(0, promptCount);
    }

    private static async Task<MainWindowViewModel> CreateOracleWorkspaceAsync(string appDataRoot, string workspaceRoot, string workspaceName)
    {
        var bootstrapper = new AppBootstrapper();
        var viewModel = bootstrapper.CreateMainWindowViewModel(TestPaths.RepositoryRoot, appDataRoot, "en");
        await viewModel.InitializeAsync();
        viewModel.PrepareCreateWorkspaceDialog();
        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");
        viewModel.NewWorkspaceName = workspaceName;
        viewModel.NewWorkspacePath = workspaceRoot;
        var completed = await viewModel.CreateWorkspaceFromDialogAsync("HeaderCreateButton");
        Assert.True(completed);
        return viewModel;
    }
}
