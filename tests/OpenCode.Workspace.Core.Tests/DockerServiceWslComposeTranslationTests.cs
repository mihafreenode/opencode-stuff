using System.Reflection;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Tests;

public sealed class DockerServiceWslComposeTranslationTests
{
    [Theory]
    [InlineData("    - oracle-demo-data:/opt/oracle/oradata", "    - oracle-demo-data:/opt/oracle/oradata")]
    [InlineData("    - C:/workspaces/demo/mounts/user:/workspace/user", "    - /mnt/c/workspaces/demo/mounts/user:/workspace/user")]
    [InlineData("    - /opt/oracle/oradata", "    - /opt/oracle/oradata")]
    [InlineData("    - ./mounts/config:/opt/opencode-workspace/config", "    - ./mounts/config:/opt/opencode-workspace/config")]
    [InlineData("    source: D:/data/demo", "    source: /mnt/d/data/demo")]
    public void TranslateWindowsBindMountSourcesInComposeText_TranslatesOnlyWindowsBindMountSources(string inputLine, string expectedLine)
    {
        var result = InvokeComposeTranslation(inputLine + "\n");

        Assert.Equal(expectedLine + "\n", result);
    }

    [Fact]
    public void TranslateWindowsBindMountSourcesInComposeText_PreservesOracleNamedVolumeSyntax()
    {
        const string compose = "services:\n  oracle-demo:\n    volumes:\n      - oracle-demo-data:/opt/oracle/oradata\n";

        var result = InvokeComposeTranslation(compose);

        Assert.Contains("oracle-demo-data:/opt/oracle/oradata", result, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle-demo-dat/mnt/a/opt/oracle/oradata", result, StringComparison.Ordinal);
    }

    private static string InvokeComposeTranslation(string compose)
    {
        var method = typeof(DockerService).GetMethod("TranslateWindowsBindMountSourcesInComposeText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [compose]));
    }
}
