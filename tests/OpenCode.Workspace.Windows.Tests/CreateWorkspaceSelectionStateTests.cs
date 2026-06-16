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

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-apex-demo");

        var definition = viewModel.BuildWorkspaceDefinitionFromSelections("oracle-demo");

        Assert.Equal(3, definition.Features.Count);
        Assert.Contains("core", definition.Features);
        Assert.Contains("oracle-demo", definition.Features);
        Assert.Contains("oracle-apex-demo", definition.Features);
        Assert.Equal(["oracle-demo", "oracle-ords"], definition.Services);
    }

    [Fact]
    public void OracleTemplateSummary_UpdatesPerSelectedTemplate()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-plsql-demo");
        Assert.Equal("Oracle PL/SQL Demo", viewModel.OracleTemplateSummaryTitle);
        Assert.Contains("✓ PL/SQL Tutorial", viewModel.OracleTemplateIncludesSummary);
        Assert.Contains("✓ Sample Procedures", viewModel.OracleTemplateIncludesSummary);

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-apex-demo");
        Assert.Equal("Oracle APEX Demo", viewModel.OracleTemplateSummaryTitle);
        Assert.Contains("✓ Oracle APEX", viewModel.OracleTemplateIncludesSummary);
        Assert.Contains("✓ Oracle REST Data Services (ORDS)", viewModel.OracleTemplateIncludesSummary);
        Assert.Contains("✓ Browser-Based Development", viewModel.OracleTemplateIncludesSummary);

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-apexlang-demo");
        Assert.Equal("Oracle APEXlang Demo", viewModel.OracleTemplateSummaryTitle);
        Assert.Contains("✓ APEXlang Export/Import", viewModel.OracleTemplateIncludesSummary);
        Assert.Contains("✓ Source-Control Workflow", viewModel.OracleTemplateIncludesSummary);
        Assert.Contains("✓ Team Onboarding Assets", viewModel.OracleTemplateIncludesSummary);
    }

    [Fact]
    public void OracleTemplateDependencies_LockInheritedAssetsForApex()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-apex-demo");

        var plsqlFeature = viewModel.AvailableFeatures.Single(feature => feature.Id == "oracle-demo");
        var apexFeature = viewModel.AvailableFeatures.Single(feature => feature.Id == "oracle-apex-demo");
        var databaseService = viewModel.AvailableServices.Single(service => service.Id == "oracle-demo");
        var ordsService = viewModel.AvailableServices.Single(service => service.Id == "oracle-ords");

        Assert.True(plsqlFeature.IsSelected);
        Assert.True(apexFeature.IsSelected);
        Assert.True(databaseService.IsSelected);
        Assert.True(ordsService.IsSelected);
        Assert.False(plsqlFeature.CanChangeSelection);
        Assert.False(apexFeature.CanChangeSelection);
        Assert.False(databaseService.CanChangeSelection);
        Assert.False(ordsService.CanChangeSelection);
        Assert.Contains("Inherited PL/SQL Foundation", plsqlFeature.DisplayName);
    }

    [Fact]
    public void OracleTemplateDependencies_LockInheritedAssetsForApexLang()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTemplate = viewModel.Templates.Single(template => template.Id == "oracle-apexlang-demo");

        var plsqlFeature = viewModel.AvailableFeatures.Single(feature => feature.Id == "oracle-demo");
        var apexFeature = viewModel.AvailableFeatures.Single(feature => feature.Id == "oracle-apex-demo");
        var apexLangFeature = viewModel.AvailableFeatures.Single(feature => feature.Id == "oracle-apexlang-demo");
        var ordsService = viewModel.AvailableServices.Single(service => service.Id == "oracle-ords");

        Assert.True(plsqlFeature.IsSelected);
        Assert.True(apexFeature.IsSelected);
        Assert.True(apexLangFeature.IsSelected);
        Assert.True(ordsService.IsSelected);
        Assert.False(plsqlFeature.CanChangeSelection);
        Assert.False(apexFeature.CanChangeSelection);
        Assert.False(apexLangFeature.CanChangeSelection);
        Assert.False(ordsService.CanChangeSelection);

        apexFeature.IsSelected = false;
        ordsService.IsSelected = false;

        Assert.True(apexFeature.IsSelected);
        Assert.True(ordsService.IsSelected);
        Assert.Contains("Inherited APEX Requirement", apexFeature.DisplayName);
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
