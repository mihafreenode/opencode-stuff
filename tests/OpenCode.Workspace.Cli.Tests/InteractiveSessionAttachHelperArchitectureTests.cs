namespace OpenCode.Workspace.Cli.Tests;

public sealed class InteractiveSessionAttachHelperArchitectureTests
{
    [Fact]
    public void Helper_Is_A_Byte_Stream_Bridge_And_Does_Not_Launch_A_Provider()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "src", "OpenCode.Workspace.Cli", "InteractiveSessionAttachHelper.cs"));

        Assert.Contains("Console.OpenStandardInput", source, StringComparison.Ordinal);
        Assert.Contains("Console.OpenStandardOutput", source, StringComparison.Ordinal);
        Assert.Contains("SendInteractiveTerminalInputAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetInteractiveTerminalOutputAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("attach-workspace.ps1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.ReadLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_output.WriteLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("opencode --session", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker exec", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProviderSessionId =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("session list", source, StringComparison.OrdinalIgnoreCase);
    }
}
