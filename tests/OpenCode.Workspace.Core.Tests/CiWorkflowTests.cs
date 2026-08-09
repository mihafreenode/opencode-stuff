using Xunit;
using YamlDotNet.RepresentationModel;
using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Tests;

public sealed class CiWorkflowTests
{
    [Fact]
    public void ReleaseWorkflows_Parse_And_TagPublicationNeedsPackagesAndIntegrationValidation()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));
        var integration = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        AssertParsesAsYaml(ci);
        AssertParsesAsYaml(integration);
        Assert.Contains("uses: ./.github/workflows/integration.yml", ci, StringComparison.Ordinal);
        Assert.Contains("- integration-validation", ci, StringComparison.Ordinal);
        Assert.Contains("- package", ci, StringComparison.Ordinal);
        Assert.Contains("--archive-kind ${{ matrix.archive_kind }}", ci, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("tar -C", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress-Archive", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-Content", ci, StringComparison.Ordinal);
        Assert.Contains("--filter \"Category=PackageIntegration\"", integration, StringComparison.Ordinal);
        Assert.Contains("Validate downloaded package on native runner", ci, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_EXISTING_PACKAGE_ARCHIVE", ci, StringComparison.Ordinal);
        Assert.Contains("name: opencode-workspace-${{ env.RELEASE_VERSION }}-${{ matrix.runtime }}", ci, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v0.1.0", "0.1.0", false)]
    [InlineData("v0.1.0-rc.1", "0.1.0-rc.1", true)]
    [InlineData("v12.34.56-rc.7", "12.34.56-rc.7", true)]
    public void ReleaseTagPolicy_AcceptsStableAndRcTags(string tag, string version, bool prerelease)
    {
        var match = ReleaseTagPattern().Match(tag);
        Assert.True(match.Success);
        Assert.Equal(version, match.Groups["version"].Value);
        Assert.Equal(prerelease, version.Contains("-rc.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v1.2")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3-rc")]
    [InlineData("v1.2.3-beta.1")]
    [InlineData("version1.2.3")]
    public void ReleaseTagPolicy_RejectsUnsupportedTags(string tag)
        => Assert.DoesNotMatch(ReleaseTagPattern(), tag);

    [Fact]
    public void ReleaseWorkflow_StagesExactAssetsBeforePublishingRcOrStable()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("docs/releases/$version.md", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/history/release-notes.md", ci, StringComparison.Ordinal);
        Assert.Contains("is_prerelease:", ci, StringComparison.Ordinal);
        Assert.Contains("make_latest:", ci, StringComparison.Ordinal);
        Assert.Contains("$makeLatest = (-not $isPrerelease).ToString().ToLowerInvariant()", ci, StringComparison.Ordinal);
        Assert.Contains("draft: true", ci, StringComparison.Ordinal);
        Assert.Contains("prerelease: ${{ needs.release-metadata.outputs.is_prerelease }}", ci, StringComparison.Ordinal);
        Assert.Contains("make_latest: false", ci, StringComparison.Ordinal);
        Assert.Contains("fail_on_unmatched_files: true", ci, StringComparison.Ordinal);
        Assert.Contains("preserve_order: true", ci, StringComparison.Ordinal);
        Assert.Contains("tag_name: ${{ github.ref_name }}", ci, StringComparison.Ordinal);
        Assert.Contains("name: OpenCode Stuff ${{ needs.release-metadata.outputs.version }}", ci, StringComparison.Ordinal);
        Assert.Contains("pattern: opencode-workspace-${{ needs.release-metadata.outputs.version }}-*", ci, StringComparison.Ordinal);
        Assert.Equal(6, Regex.Matches(ci, "^\\s+release-artifacts/opencode-workspace-\\$\\{\\{ needs\\.release-metadata\\.outputs\\.version \\}\\}", RegexOptions.Multiline).Count);
        Assert.Contains("Refuse to mutate an already published release", ci, StringComparison.Ordinal);
        Assert.Contains("Refusing to replace assets on a public release", ci, StringComparison.Ordinal);
        Assert.Contains("Verify staged GitHub Release assets", ci, StringComparison.Ordinal);
        Assert.Contains("Publish verified release", ci, StringComparison.Ordinal);
        Assert.Contains("-F \"draft=false\"", ci, StringComparison.Ordinal);
        Assert.Contains("-F \"prerelease=${{ needs.release-metadata.outputs.is_prerelease }}\"", ci, StringComparison.Ordinal);
        Assert.Contains("-f \"make_latest=${{ needs.release-metadata.outputs.make_latest }}\"", ci, StringComparison.Ordinal);
        Assert.True(ci.IndexOf("Verify exact release asset inventory and checksums", StringComparison.Ordinal) < ci.IndexOf("Stage draft release and assets", StringComparison.Ordinal));
        Assert.True(ci.IndexOf("Verify staged GitHub Release assets", StringComparison.Ordinal) < ci.IndexOf("Publish verified release", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseWorkflow_UsesExactNativeMatrixAndMandatoryGates()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));
        var integration = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("runs_on: windows-latest\n            runtime: win-x64", ci, StringComparison.Ordinal);
        Assert.Contains("runs_on: ubuntu-latest\n            runtime: linux-x64", ci, StringComparison.Ordinal);
        Assert.Contains("runs_on: macos-15\n            runtime: osx-arm64", ci, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(ci, "runtime: (win-x64|linux-x64|osx-arm64)").Count);
        Assert.Contains("needs:\n      - release-metadata\n      - integration-validation\n      - package", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", ci, StringComparison.Ordinal);
        foreach (var requiredGate in new[]
        {
            "OpenCode.Workspace.Core.Tests.csproj",
            "OpenCode.Workspace.Cli.Tests.csproj",
            "OpenCode.Workspace.Avalonia.Tests.csproj",
            "OpenCode.Workspace.RemoteBridge.Tests.csproj",
            "OpenCode.Workspace.Mcp.Tests.csproj",
            "Category=FastIntegration",
            "Category=PackageIntegration",
        })
        {
            Assert.Contains(requiredGate, integration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseAssetContract_RejectsMissingDuplicateOrUnexpectedAssets()
    {
        var expected = ExpectedReleaseAssets("0.1.0-rc.1");
        Assert.True(IsExactAssetSet(expected, expected));
        Assert.False(IsExactAssetSet(expected, expected.Skip(1)));
        Assert.False(IsExactAssetSet(expected, expected.Concat([expected[0]])));
        Assert.False(IsExactAssetSet(expected, expected.Append("opencode-workspace-0.1.0-rc.1-osx-x64.tar.gz")));
        Assert.False(IsExactAssetSet(expected, ExpectedReleaseAssets("0.1.0")));
    }

    [Fact]
    public void IntegrationWorkflow_UsesMandatoryFinalCleanup_AndAvoidsBroadDockerPrune()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke cleanup --all --format json", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke cleanup --dry-run --all --format json", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("docker system prune", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker volume prune", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntegrationWorkflow_RunsOracleSequentially_OnDedicatedRunner()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("oracle-smoke-matrix:", workflow, StringComparison.Ordinal);
        Assert.Contains("self-hosted", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle-smoke-matrix:\n    strategy:", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run oracle-plsql-demo", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run oracle-apex-demo", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run oracle-apexlang-demo", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationWorkflow_UploadsArtifacts_AndUsesCanonicalSmokeCommands()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("uses: actions/upload-artifact@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family lightweight", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family postgresql", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family analytics", workflow, StringComparison.Ordinal);
        Assert.Contains("smoke run --family document-processing", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationBoundaryTests_UseRealHttpAndProtocolHarnesses()
    {
        var apiTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Api.IntegrationTests", "ApiTestFactory.cs"));
        var mcpTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "McpProtocolIntegrationTests.cs"));

        Assert.Contains("WebApplicationFactory<Program>", apiTests, StringComparison.Ordinal);
        Assert.Contains("McpClient.CreateAsync", mcpTests, StringComparison.Ordinal);
        Assert.Contains("StdioClientTransport", mcpTests, StringComparison.Ordinal);
    }

    private static void AssertParsesAsYaml(string workflow)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(workflow));
        Assert.Single(stream.Documents);
    }

    private static Regex ReleaseTagPattern()
        => new("^v(?<version>(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-rc\\.(0|[1-9][0-9]*))?)$", RegexOptions.CultureInvariant);

    private static string[] ExpectedReleaseAssets(string version)
        =>
        [
            $"opencode-workspace-{version}-win-x64.zip",
            $"opencode-workspace-{version}-win-x64.zip.sha256",
            $"opencode-workspace-{version}-linux-x64.tar.gz",
            $"opencode-workspace-{version}-linux-x64.tar.gz.sha256",
            $"opencode-workspace-{version}-osx-arm64.tar.gz",
            $"opencode-workspace-{version}-osx-arm64.tar.gz.sha256",
        ];

    private static bool IsExactAssetSet(IReadOnlyCollection<string> expected, IEnumerable<string> actual)
    {
        var actualArray = actual.ToArray();
        return actualArray.Length == actualArray.Distinct(StringComparer.Ordinal).Count()
            && expected.Order(StringComparer.Ordinal).SequenceEqual(actualArray.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
