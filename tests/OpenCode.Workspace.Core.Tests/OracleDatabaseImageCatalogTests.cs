using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleDatabaseImageCatalogTests
{
    [Fact]
    public void ResolveDatabaseImage_UsesKnownGoodDefault()
    {
        var definition = new WorkspaceDefinition();

        Assert.Equal("gvenzl/oracle-free:23", OracleDatabaseImageCatalog.ResolveDatabaseImage(definition));
    }

    [Fact]
    public void IsKnownApexIncompatibleImage_RecognizesFaststartImage()
    {
        Assert.True(OracleDatabaseImageCatalog.IsKnownApexIncompatibleImage("gvenzl/oracle-free:23-slim-faststart"));
        Assert.False(OracleDatabaseImageCatalog.IsKnownApexIncompatibleImage("gvenzl/oracle-free:23"));
    }

    [Fact]
    public void ResolveDatabaseImage_RejectsExplicitEmptyValue()
    {
        var definition = new WorkspaceDefinition
        {
            Oracle = new OracleWorkspacePreferences { DatabaseImage = "   " },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => OracleDatabaseImageCatalog.ResolveDatabaseImage(definition));
        Assert.Contains("oracle.databaseImage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceResolver_OverridesOracleDemoImageFromWorkspaceDefinition()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var resolved = resolver.Resolve(new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata { Name = "oracle-image-override" },
            Features = ["core", "oracle-demo"],
            Services = ["oracle-demo"],
            Oracle = new OracleWorkspacePreferences { DatabaseImage = "gvenzl/oracle-free:23-slim-faststart" },
        });

        var oracleService = Assert.Single(resolved.Services, service => string.Equals(service.Id, "oracle-demo", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("gvenzl/oracle-free:23-slim-faststart", oracleService.Image);
    }
}
