using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class CreateWorkspaceCompletionTests
{
    [Fact]
    public async Task CreateWorkspaceFromDialogAsync_SavesWorkspaceRecordAndCompletes()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-complete-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-workspace-{Guid.NewGuid():N}");
        var viewModel = CreateViewModel(appDataRoot);

        viewModel.PrepareCreateWorkspaceDialog();
        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");
        viewModel.NewWorkspaceName = "demo-oracle-complete";
        viewModel.NewWorkspacePath = workspaceRoot;

        var completed = await viewModel.CreateWorkspaceFromDialogAsync("HeaderCreateButton");

        Assert.True(completed);
        var indexPath = Path.Combine(appDataRoot, "workspaces.json");
        Assert.True(File.Exists(indexPath));
        var indexJson = File.ReadAllText(indexPath);
        Assert.Contains("demo-oracle-complete", indexJson);
        Assert.Contains("oracle-demo", File.ReadAllText(Path.Combine(workspaceRoot, "workspace.yaml")));
    }

    [Fact]
    public async Task CreateWorkspaceFromDialogAsync_HeaderAndTemplateCardPathsCreateIdenticalWorkspaces()
    {
        var headerAppDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-header-{Guid.NewGuid():N}");
        var templateAppDataRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-template-{Guid.NewGuid():N}");
        var headerWorkspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-header-workspace-{Guid.NewGuid():N}");
        var templateWorkspaceRoot = Path.Combine(Path.GetTempPath(), $"ocwm-create-template-workspace-{Guid.NewGuid():N}");

        var headerViewModel = CreateViewModel(headerAppDataRoot);
        headerViewModel.PrepareCreateWorkspaceDialog();
        headerViewModel.SelectedTemplate = headerViewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");
        headerViewModel.NewWorkspaceName = "demo-oracle-header";
        headerViewModel.NewWorkspacePath = headerWorkspaceRoot;

        var templateViewModel = CreateViewModel(templateAppDataRoot);
        templateViewModel.PrepareCreateWorkspaceDialog();
        templateViewModel.SelectedTemplate = templateViewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");
        templateViewModel.NewWorkspaceName = "demo-oracle-template";
        templateViewModel.NewWorkspacePath = templateWorkspaceRoot;

        var headerCompleted = await headerViewModel.CreateWorkspaceFromDialogAsync("HeaderCreateButton");
        var templateCompleted = await templateViewModel.CreateWorkspaceFromDialogAsync("TemplateCardCreateButton");

        Assert.True(headerCompleted);
        Assert.True(templateCompleted);

        var headerWorkspaceYaml = File.ReadAllText(Path.Combine(headerWorkspaceRoot, "workspace.yaml"));
        var templateWorkspaceYaml = File.ReadAllText(Path.Combine(templateWorkspaceRoot, "workspace.yaml"));
        Assert.Equal(headerWorkspaceYaml.Replace("demo-oracle-header", "demo-oracle-template", StringComparison.Ordinal), templateWorkspaceYaml);

        var headerIndexJson = File.ReadAllText(Path.Combine(headerAppDataRoot, "workspaces.json"));
        var templateIndexJson = File.ReadAllText(Path.Combine(templateAppDataRoot, "workspaces.json"));
        Assert.Contains("demo-oracle-header", headerIndexJson);
        Assert.Contains("demo-oracle-template", templateIndexJson);
    }

    private static MainWindowViewModel CreateViewModel(string appDataRoot)
    {
        var bootstrapper = new AppBootstrapper();
        return bootstrapper.CreateMainWindowViewModel(TestPaths.RepositoryRoot, appDataRoot, "en");
    }
}
