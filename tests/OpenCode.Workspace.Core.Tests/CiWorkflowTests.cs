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
        Assert.Contains("Validate complete extracted distribution on native runner", ci, StringComparison.Ordinal);
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
        Assert.Contains("gh api --paginate \"repos/${{ github.repository }}/releases?per_page=100\"", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("releases/tags/${{ github.ref_name }}", ci, StringComparison.Ordinal);
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
    public void ReleaseWorkflow_PassesDownloadedPackagePathsAsWorkspaceAbsolutePaths()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("OPENCODE_EXISTING_PACKAGE_ARCHIVE: ${{ github.workspace }}/artifacts/downloaded/", ci, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT: ${{ github.workspace }}/artifacts/native-package-validation/", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENCODE_EXISTING_PACKAGE_ARCHIVE: artifacts/downloaded/", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT: artifacts/native-package-validation/", ci, StringComparison.Ordinal);
    }

    [Fact]
    public void NativePackageLeg_RunsRequiredWindowsAndExtractedDistributionAcceptance()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));
        var conPtyTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Api.IntegrationTests", "WindowsConPtyTerminalRuntimeIntegrationTests.cs"));
        var oracleTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Core.Tests", "OraclePortConflictHandlingTests.cs"));
        var packageTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PackagedDistributionTests.cs"));
        var oracleMethods = new[]
        {
            "ValidateVolatileEnvironmentAsync_UsesWslDockerForPortPreflightWhenWindowsDockerPsTimesOut",
            "ValidateVolatileEnvironmentAsync_UsesShortTimeoutForDockerPsPreflight",
            "RunSimpleDockerCommandAsync_WhenWindowsDockerUnavailableButWslDockerAvailable_ThrowsPreciseMessage",
        };

        Assert.Contains("--filter \"Category=WindowsConPtyIntegration\"", ci, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Category\", \"WindowsConPtyIntegration\")]", conPtyTests, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(ci, "FullyQualifiedName=OpenCode\\.Workspace\\.Core\\.Tests\\.OraclePortConflictHandlingTests\\.").Count);
        foreach (var method in oracleMethods)
        {
            Assert.Contains($"FullyQualifiedName=OpenCode.Workspace.Core.Tests.OraclePortConflictHandlingTests.{method}", ci, StringComparison.Ordinal);
            Assert.Contains($" {method}()", oracleTests, StringComparison.Ordinal);
        }

        Assert.Contains("if: matrix.runtime == 'win-x64'", Step(ci, "Run Windows ConPTY integration tests"), StringComparison.Ordinal);
        Assert.Contains("if: matrix.runtime == 'win-x64'", Step(ci, "Run Windows Oracle port conflict handling tests"), StringComparison.Ordinal);
        Assert.Contains("--filter \"FullyQualifiedName~PackagedDistributionTests.ExtractedDistribution_\"", ci, StringComparison.Ordinal);
        Assert.Contains("runs_on: macos-15\n            runtime: osx-arm64", ci, StringComparison.Ordinal);
        Assert.Contains("PackagedLocalHost_ConPtyHelper_Detaches_Reattaches_And_Stops_Cleanly", packageTests, StringComparison.Ordinal);
        Assert.Contains("--filter \"Category=PackagedConPtyIntegration\"", ci, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(packageTests, "Trait\\(\"Category\", \"PackagedConPtyIntegration\"\\)"));
        Assert.DoesNotContain("api/test-assets/conpty-child", ci, StringComparison.Ordinal);
        Assert.Contains("OPENCODE_PACKAGE_CONPTY_TEST_ASSET_ROOT: ${{ github.workspace }}/tests/OpenCode.Workspace.ConPtyTestChild/bin/Release/net10.0", ci, StringComparison.Ordinal);
    }

    [Fact]
    public void NativePackageLeg_SeparatesSourceTestsFromDownloadedPackageEnvironment()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.DoesNotContain("OPENCODE_EXISTING_PACKAGE", Step(ci, "Run Windows ConPTY integration tests"), StringComparison.Ordinal);
        Assert.DoesNotContain("OPENCODE_EXISTING_PACKAGE", Step(ci, "Run Windows Oracle port conflict handling tests"), StringComparison.Ordinal);
        foreach (var packageStep in new[]
        {
            Step(ci, "Validate complete extracted distribution on native runner"),
            Step(ci, "Validate packaged ConPTY handoff from downloaded archive"),
        })
        {
            Assert.Contains("OPENCODE_EXISTING_PACKAGE_ARCHIVE: ${{ github.workspace }}/artifacts/downloaded/", packageStep, StringComparison.Ordinal);
            Assert.Contains("OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT: ${{ github.workspace }}/artifacts/native-package-validation/", packageStep, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENCODE_EXISTING_PACKAGE_ROOT", packageStep, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NativePackageLeg_RunsExactlyFocusedWindowsProcessRunnerCoverage()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));
        var processRunnerTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Core.Tests", "ProcessRunnerTests.cs"));
        var step = Step(ci, "Run focused Windows ProcessRunner tests");
        var methods = new[]
        {
            "RunAsync_CapturesStdoutAndStderrLines",
            "RunAsync_ImmediateExitStillCapturesBothStreams",
            "RunAsync_LargeOutputIsFullyDrained",
            "RunAsync_CancellationDoesNotDeadlockStreamCompletion",
            "RunAsync_CancellationTerminatesDescendantProcessTree",
        };

        Assert.Contains("if: matrix.runtime == 'win-x64'", step, StringComparison.Ordinal);
        Assert.Contains("OpenCode.Workspace.Core.Tests.csproj", step, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(step, "FullyQualifiedName=OpenCode\\.Workspace\\.Core\\.Tests\\.ProcessRunnerTests\\.").Count);
        Assert.All(methods, method =>
        {
            Assert.Contains($"FullyQualifiedName=OpenCode.Workspace.Core.Tests.ProcessRunnerTests.{method}", step, StringComparison.Ordinal);
            Assert.Contains($" {method}()", processRunnerTests, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("Category=", step, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedName~ProcessRunnerTests", step, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationWorkflow_RequiresLightweightDockerOnMainAndVersionTagsOnly()
    {
        var ci = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml"));
        var integration = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("run_lightweight_docker: ${{ github.event_name == 'push' && (github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/tags/v')) }}", ci, StringComparison.Ordinal);
        Assert.Contains("lightweight-docker-integration:", integration, StringComparison.Ordinal);
        Assert.Contains("if: inputs.run_lightweight_docker", integration, StringComparison.Ordinal);
        Assert.Contains("run_lightweight_docker:\n        type: boolean\n        default: false", integration, StringComparison.Ordinal);
        Assert.Contains("run_lightweight_docker:\n        description: Run lightweight Docker integration tests\n        type: boolean\n        default: false", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredIntegrationMethods_DoNotSilentlyReturnForEnvironmentalPrerequisites()
    {
        var oracleTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Core.Tests", "OraclePortConflictHandlingTests.cs"));
        var packageTests = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PackagedDistributionTests.cs"));
        var requiredBodies = new[]
        {
            MethodBody(oracleTests, "ValidateVolatileEnvironmentAsync_UsesWslDockerForPortPreflightWhenWindowsDockerPsTimesOut"),
            MethodBody(oracleTests, "ValidateVolatileEnvironmentAsync_UsesShortTimeoutForDockerPsPreflight"),
            MethodBody(oracleTests, "RunSimpleDockerCommandAsync_WhenWindowsDockerUnavailableButWslDockerAvailable_ThrowsPreciseMessage"),
            MethodBody(packageTests, "ExtractedDistribution_ResolvesPackagedContent_AndHostsExitGracefully"),
            MethodBody(packageTests, "ExtractedDistribution_McpConfigure_UsesPackagedPaths"),
            MethodBody(packageTests, "PackagedLocalHost_ConPtyHelper_Detaches_Reattaches_And_Stops_Cleanly"),
        };
        var silentPrerequisiteReturn = new Regex(@"if\s*\([^)]*(DockerIsAvailable|ShouldRun|GetEnvironmentVariable|File\.Exists|Directory\.Exists)[^)]*\)\s*\{?\s*return\s*;", RegexOptions.CultureInvariant);

        Assert.All(requiredBodies, body => Assert.DoesNotMatch(silentPrerequisiteReturn, body));
    }

    [Fact]
    public void IntegrationTests_DoNotSilentlyReturnForPrerequisiteOrPlatformGates()
    {
        var testsRoot = Path.Combine(TestPaths.RepositoryRoot, "tests");
        var prerequisiteReturn = new Regex(
            @"if\s*\([\s\S]{0,300}?(CanRunGit|OperatingSystem\.Is|DockerIsAvailable|TryGetConfiguration|ShouldRunPackagedOracleValidation|GetEnvironmentVariable)[\s\S]{0,300}?\)\s*(\{\s*)?return\s*;",
            RegexOptions.CultureInvariant);
        var violations = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(nameof(CiWorkflowTests) + ".cs", StringComparison.Ordinal))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(item => prerequisiteReturn.IsMatch(item.Source))
            .Select(item => Path.GetRelativePath(TestPaths.RepositoryRoot, item.Path))
            .ToArray();

        Assert.True(violations.Length == 0, $"Prerequisite/platform gates must fail or explicitly skip, not return successfully: {string.Join(", ", violations)}");
    }

    [Fact]
    public void PackageCategories_KeepDeterministicAndEnvironmentalContractsSeparate()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.Mcp.Tests", "PackagedDistributionTests.cs"));

        Assert.DoesNotContain("[Trait(\"Category\", \"PackageIntegration\")]\npublic sealed class", source, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(source, "Trait\\(\"Category\", \"PackageIntegration\"\\)").Count);
        Assert.Contains("[Trait(\"Category\", \"PackageEnvironmentIntegration\")]", MethodDeclaration(source, "ExtractedDistribution_RuntimeReadinessAndMcpDoctor_UsePackagedPaths"), StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Category\", \"PackageEnvironmentIntegration\")]", MethodDeclaration(source, "PackagedDoctor_ReusesExternalLocalHostWithoutStoppingIt"), StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Category\", \"LiveDockerIntegration\")]", MethodDeclaration(source, "ExtractedDistribution_RuntimeReadinessAndMcpDoctor_UsePackagedPaths"), StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Category\", \"LiveDockerIntegration\")]", MethodDeclaration(source, "PackagedDoctor_ReusesExternalLocalHostWithoutStoppingIt"), StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Category\", \"LiveDockerIntegration\")]", MethodDeclaration(source, "PackagedMcp_RunSmoke_ReportsIncrementalProgress_AndShutsDownCleanly"), StringComparison.Ordinal);
        Assert.Contains("[Trait(\"Category\", \"PackagedOracleMcpIntegration\")]", MethodDeclaration(source, "PackagedMcp_OracleApexlangProvisioning_ReportsProgress_AndCleansUp"), StringComparison.Ordinal);
        Assert.DoesNotContain("PackageIntegration", MethodDeclaration(source, "ExtractedDistribution_RuntimeReadinessAndMcpDoctor_UsePackagedPaths"), StringComparison.Ordinal);
        Assert.DoesNotContain("PackageIntegration", MethodDeclaration(source, "PackagedDoctor_ReusesExternalLocalHostWithoutStoppingIt"), StringComparison.Ordinal);
        Assert.DoesNotContain("PackageIntegration", MethodDeclaration(source, "PackagedMcp_RunSmoke_ReportsIncrementalProgress_AndShutsDownCleanly"), StringComparison.Ordinal);
        Assert.DoesNotContain("PackageIntegration", MethodDeclaration(source, "PackagedMcp_OracleApexlangProvisioning_ReportsProgress_AndCleansUp"), StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredWorkflowCategoryFilters_HaveMatchingTestTraits()
    {
        var workflows = string.Join('\n',
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml")),
            File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml")));
        var testSources = string.Join('\n', Directory.EnumerateFiles(Path.Combine(TestPaths.RepositoryRoot, "tests"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        var requiredCategories = Regex.Matches(workflows, @"(?<!!)Category=(?<category>[A-Za-z0-9]+)")
            .Select(match => match.Groups["category"].Value)
            .Distinct(StringComparer.Ordinal);

        Assert.All(requiredCategories, category => Assert.Contains($"[Trait(\"Category\", \"{category}\")]", testSources, StringComparison.Ordinal));
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
    public void IntegrationWorkflow_KeepsOracleOptionalSequentialAndOnDedicatedRunner()
    {
        var workflow = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "integration.yml"));

        Assert.Contains("oracle-smoke-sequential:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Oracle Smoke (Sequential)", workflow, StringComparison.Ordinal);
        Assert.Contains("if: inputs.run_oracle", workflow, StringComparison.Ordinal);
        Assert.Contains("run_oracle:\n        type: boolean\n        default: false", workflow, StringComparison.Ordinal);
        Assert.Contains("run_oracle: false", File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, ".github", "workflows", "ci.yml")), StringComparison.Ordinal);
        Assert.Contains("self-hosted", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle-smoke-sequential:\n    strategy:", workflow, StringComparison.Ordinal);
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

    private static string Step(string workflow, string name)
    {
        var start = workflow.IndexOf($"- name: {name}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Workflow step '{name}' was not found.");
        var end = workflow.IndexOf("\n      - name:", start + 1, StringComparison.Ordinal);
        return workflow[start..(end < 0 ? workflow.Length : end)];
    }

    private static string MethodBody(string source, string methodName)
    {
        var signature = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(signature >= 0, $"Method '{methodName}' was not found.");
        var start = source.IndexOf('{', signature);
        Assert.True(start >= 0, $"Method body for '{methodName}' was not found.");
        var depth = 0;
        for (var index = start; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Method body for '{methodName}' was incomplete.");
    }

    private static string MethodDeclaration(string source, string methodName)
    {
        var signature = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(signature >= 0, $"Method '{methodName}' was not found.");
        var previousMethodEnd = source.LastIndexOf('}', signature);
        return source[(previousMethodEnd + 1)..source.IndexOf('{', signature)];
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
