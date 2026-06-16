using OpenCode.Workspace.Core.Catalog;

namespace OpenCode.Workspace.Core.Tests;

public sealed class CatalogIntegrationTests
{
    [Fact]
    public void BuiltInCatalog_LoadsAndValidatesBuiltInManifests()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var validator = new CatalogValidator();

        var features = provider.LoadFeatures();
        var services = provider.LoadServices();
        var templates = provider.LoadTemplates();

        Assert.NotEmpty(features);
        Assert.NotEmpty(services);
        Assert.NotEmpty(templates);
        Assert.Empty(validator.ValidateFeatures(features));
        Assert.Empty(validator.ValidateServices(services));
        Assert.Empty(validator.ValidateTemplates(templates, features, services));
    }

    [Fact]
    public void TemplateExpander_CreatesPortableWorkspaceDefinition()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var template = provider.LoadTemplates().Single(item => item.Id == "data-processing");
        var expander = new TemplateExpander();

        var definition = expander.Expand("portable-data", template);

        Assert.Equal("portable-data", definition.Workspace.Name);
        Assert.Equal("ubuntu:24.04", definition.Workspace.Image);
        Assert.Equal(22, definition.Runtime.Node);
        Assert.Contains("core", definition.Features);
        Assert.Contains("postgres", definition.Services);
        Assert.Contains("pgadmin", definition.Services);
    }

    [Fact]
    public void TemplateExpander_CarriesTemplateSkillsAndMcpSelections()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var template = provider.LoadTemplates().Single(item => item.Id == "oracle-apexlang-demo");
        var expander = new TemplateExpander();

        var definition = expander.Expand("oracle-demo", template);

        Assert.Contains("oracle-explain-procedure", definition.Skills);
        Assert.Contains("oracle-sqlcl", definition.Mcp);
        Assert.Contains("oracle-demo", definition.Services);
        Assert.Contains("oracle-ords", definition.Services);
    }

    [Fact]
    public void WorkspaceResolver_DeduplicatesDependenciesAndAlwaysEnablesCore()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());

        var resolved = resolver.Resolve(new OpenCode.Workspace.Core.Models.WorkspaceDefinition
        {
            Workspace = new OpenCode.Workspace.Core.Models.WorkspaceMetadata { Name = "docs" },
            Features = ["document-processing", "document-processing"],
            Services = ["postgres"],
        });

        Assert.Contains(resolved.Features, feature => feature.Id == "core");
        Assert.Contains(resolved.AptPackages, packageName => packageName == "pandoc");
        Assert.Contains(resolved.AptPackages, packageName => packageName == "fonts-crosextra-carlito");
        Assert.Contains(resolved.NpmPackages, packageName => packageName == "playwright");
        Assert.Contains(resolved.NpmPackages, packageName => packageName == "@mermaid-js/mermaid-cli");
        Assert.Contains(resolved.PipPackages, packageName => packageName == "weasyprint");
        Assert.Contains(resolved.AptPackages, packageName => packageName == "python-is-python3");
        Assert.Contains(resolved.PostInstallCommands, command => command.Contains("command -v typst", StringComparison.Ordinal));
        Assert.Contains(resolved.PostInstallCommands, command => command.Contains("playwright install chromium", StringComparison.Ordinal));
        Assert.Contains(resolved.PostInstallCommands, command => command.Contains("fc-cache -fv", StringComparison.Ordinal));
        Assert.Equal(resolved.AptPackages.Count, resolved.AptPackages.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OracleFeature_DoesNotHardcodeLibaioInCatalogPackageList()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var oracleFeature = provider.LoadFeatures().Single(feature => feature.Id == "oracle-demo");

        Assert.DoesNotContain("libaio1", oracleFeature.Dependencies.Apt, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("libaio1t64", oracleFeature.Dependencies.Apt, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(oracleFeature.PostInstall, command => command.Contains("apt-get install -y libaio1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OracleCatalog_ContainsPlSqlApexAndApexLangTemplates()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var templates = provider.LoadTemplates();

        Assert.Contains(templates, template => template.Id == "oracle-plsql-demo");
        Assert.Contains(templates, template => template.Id == "oracle-apex-demo");
        Assert.Contains(templates, template => template.Id == "oracle-apexlang-demo");
    }
}
