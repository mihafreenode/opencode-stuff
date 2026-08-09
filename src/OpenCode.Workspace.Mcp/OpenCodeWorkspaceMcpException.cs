namespace OpenCode.Workspace.Mcp;

public sealed class OpenCodeWorkspaceMcpException : InvalidOperationException
{
    public OpenCodeWorkspaceMcpException(string code, string message, string recommendation = "", string failureClassification = "")
        : base(message)
    {
        Code = code;
        Recommendation = recommendation;
        FailureClassification = string.IsNullOrWhiteSpace(failureClassification) ? code : failureClassification;
    }

    public string Code { get; }
    public string Recommendation { get; }
    public string FailureClassification { get; }
}
