namespace OpenCode.Workspace.Manager.Services;

/// <summary>
/// Generates a small, deterministic terminal profile snippet for contributors who
/// want the attach experience to be discoverable and reproducible.
/// </summary>
public sealed class TerminalProfileGenerator
{
    public string Generate(string workspaceName, string colorScheme = "Campbell", string fontFace = "JetBrainsMono Nerd Font")
    {
        return string.Join(Environment.NewLine, new[]
        {
            "{",
            $"  \"name\": \"{workspaceName}\",",
            $"  \"colorScheme\": \"{colorScheme}\",",
            $"  \"font\": {{ \"face\": \"{fontFace}\" }},",
            "  \"suppressApplicationTitle\": false",
            "}",
        });
    }
}
