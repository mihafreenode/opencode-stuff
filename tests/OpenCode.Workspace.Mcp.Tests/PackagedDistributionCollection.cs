using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

[CollectionDefinition("Packaged distribution", DisableParallelization = true)]
public sealed class PackagedDistributionCollection : ICollectionFixture<PackagedDistributionFixture>
{
}
