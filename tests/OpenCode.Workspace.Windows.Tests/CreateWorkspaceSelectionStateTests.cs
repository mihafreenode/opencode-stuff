using System.IO;
using System.Linq;
using OpenCode.Workspace.Manager.ViewModels;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class CreateWorkspaceSelectionStateTests
{
    [Fact]
    public void OracleTemplate_BuildsWorkspaceDefinitionWithOracleService()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");

        var definition = viewModel.BuildWorkspaceDefinitionFromSelections("oracle-demo");

        Assert.Equal(["core", "oracle-demo"], definition.Features);
        Assert.Equal(["oracle-demo"], definition.Services);
    }

    [Fact]
    public void SwitchingFromDocumentationTemplateToOracle_ClearsPreviousFeatureSelections()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "documentation-analysis");
        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");

        var definition = viewModel.BuildWorkspaceDefinitionFromSelections("oracle-demo");

        Assert.DoesNotContain("document-processing", definition.Features);
        Assert.DoesNotContain("ocr-processing", definition.Features);
        Assert.Equal(["core", "oracle-demo"], definition.Features);
        Assert.Equal(["oracle-demo"], definition.Services);
    }

    [Fact]
    public void ReopeningCreateWorkspaceDialog_ReappliesCurrentTemplateSelectionsFromCleanState()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");
        viewModel.AvailableFeatures.Single(feature => feature.Id == "document-processing").IsSelected = true;
        viewModel.AvailableServices.Single(service => service.Id == "oracle-demo").IsSelected = false;

        viewModel.PrepareCreateWorkspaceDialog();

        var definition = viewModel.BuildWorkspaceDefinitionFromSelections("oracle-demo");

        Assert.Equal(["core", "oracle-demo"], definition.Features);
        Assert.Equal(["oracle-demo"], definition.Services);
    }

    [Fact]
    public void ReopeningCreateWorkspaceDialog_DoesNotCarryForwardPreviousTemplateState()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "documentation-analysis");
        viewModel.PrepareCreateWorkspaceDialog();
        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");

        var definition = viewModel.BuildWorkspaceDefinitionFromSelections("oracle-demo");

        Assert.Equal(["core", "oracle-demo"], definition.Features);
        Assert.Equal(["oracle-demo"], definition.Services);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var bootstrapper = new AppBootstrapper();
        return bootstrapper.CreateMainWindowViewModel(
            TestPaths.RepositoryRoot,
            Path.Combine(Path.GetTempPath(), $"ocwm-selection-state-{System.Guid.NewGuid():N}"),
            "en");
    }
}
