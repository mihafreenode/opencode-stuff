using System.Reflection;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Smoke;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class WorkspaceSmokeCatalogTests
{
    [Fact]
    public void BuildDefinitions_CoversEveryBuiltInTemplate()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var templates = provider.LoadTemplates();

        var definitions = WorkspaceSmokeCatalog.BuildDefinitions(templates);

        Assert.Equal(templates.Count, definitions.Count);
        Assert.All(definitions, definition => Assert.True(definition.Supported));
    }

    [Fact]
    public void BuildDefinition_DerivesPostgreSqlFamilyAndServices()
    {
        var provider = new BuiltInCatalogProvider(Path.Combine(TestPaths.RepositoryRoot, "catalog"));
        var template = provider.LoadTemplates().Single(item => item.Id == "data-processing");

        var definition = WorkspaceSmokeCatalog.BuildDefinition(template);

        Assert.Equal("postgresql", definition.Family);
        Assert.Equal(WorkspaceSmokeResourceClass.Database, definition.ResourceClass);
        Assert.Contains("postgres", definition.ExpectedServices);
        Assert.Contains("pgadmin", definition.ExpectedServices);
        Assert.Contains("postgresql-runtime", definition.ValidatorIds);
    }

    [Fact]
    public void ValidateTemplateSmokeMetadata_RejectsMissingCoverage()
    {
        var errors = WorkspaceSmokeCatalog.ValidateTemplateSmokeMetadata(
        [
            new TemplateManifest
            {
                Id = "sample",
                DisplayName = "Sample",
                Features = ["core"],
                Services = [],
            },
        ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Contains(errors, item => item.Contains("missing smoke coverage metadata", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateTemplateSmokeMetadata_RejectsApexValidatorOnPlSqlTemplate()
    {
        var errors = WorkspaceSmokeCatalog.ValidateTemplateSmokeMetadata(
        [
            new TemplateManifest
            {
                Id = "oracle-plsql-demo",
                DisplayName = "Oracle",
                Features = ["core", "oracle-demo"],
                Services = ["oracle-demo"],
                Smoke = new TemplateSmokeManifest
                {
                    Supported = true,
                    Family = "oracle-plsql",
                    Validators = ["oracle-apex-runtime"],
                },
            },
        ],
            new HashSet<string>(["oracle-demo"], StringComparer.OrdinalIgnoreCase));

        Assert.Contains(errors, item => item.Contains("cannot assign APEX validators", StringComparison.Ordinal));
    }

    [Fact]
    public void MatrixOrdering_PrioritizesLightweightBeforeOracle()
    {
        var method = typeof(WorkspaceSmokeMatrixRunner).GetMethod("OrderDefinitions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var ordered = (IReadOnlyList<WorkspaceSmokeDefinition>)method!.Invoke(null,
        [
            new[]
            {
                new WorkspaceSmokeDefinition { TemplateId = "oracle", DisplayName = "Oracle", Family = "oracle-plsql", Supported = true, ResourceClass = WorkspaceSmokeResourceClass.OracleExclusive, TimeoutClass = WorkspaceSmokeTimeoutClass.Extended },
                new WorkspaceSmokeDefinition { TemplateId = "light", DisplayName = "Light", Family = "lightweight", Supported = true, ResourceClass = WorkspaceSmokeResourceClass.Lightweight, TimeoutClass = WorkspaceSmokeTimeoutClass.Short },
                new WorkspaceSmokeDefinition { TemplateId = "db", DisplayName = "Db", Family = "postgresql", Supported = true, ResourceClass = WorkspaceSmokeResourceClass.Database, TimeoutClass = WorkspaceSmokeTimeoutClass.Long },
            },
        ])!;

        Assert.Equal(["light", "db", "oracle"], ordered.Select(item => item.TemplateId).ToArray());
    }
}
