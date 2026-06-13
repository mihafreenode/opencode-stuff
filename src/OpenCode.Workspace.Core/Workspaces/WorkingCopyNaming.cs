using System.Text;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkingCopyNaming
{
    public static string Create(string user, string title, DateTimeOffset timestamp)
    {
        var safeUser = SanitizeSegment(user, "user");
        var safeTitle = SanitizeSegment(title, "workspace");
        return $"users/{safeUser}/{safeTitle}-{timestamp:yyyyMMdd-HHmm}";
    }

    public static string CreateReview(string user, string title, DateTimeOffset timestamp)
    {
        var safeUser = SanitizeSegment(user, "user");
        var safeTitle = SanitizeSegment(title, "workspace-review");
        return $"reviews/{safeUser}/{safeTitle}-{timestamp:yyyyMMdd-HHmm}";
    }

    public static string CreateImportedWorkspace(string title, DateTimeOffset timestamp)
    {
        var safeTitle = SanitizeSegment(title, "workspace");
        return $"workspace/{safeTitle}-{timestamp:yyyyMMdd-HHmm}";
    }

    public static string SanitizeSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder();
        var normalized = value.Trim().ToLowerInvariant();
        var lastWasSeparator = false;

        foreach (var character in normalized)
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (char.IsWhiteSpace(character) || character is '-' or '_' or '.')
            {
                if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    lastWasSeparator = true;
                }
            }
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    public static bool IsProtectedBranch(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return false;
        }

        return string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase)
            || string.Equals(branchName, "master", StringComparison.OrdinalIgnoreCase)
            || string.Equals(branchName, "staging", StringComparison.OrdinalIgnoreCase)
            || string.Equals(branchName, "production", StringComparison.OrdinalIgnoreCase)
            || branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase)
            || branchName.StartsWith("protected/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeWorkingCopy(string branchName)
    {
        return IsWorkspaceBranch(branchName);
    }

    public static bool IsWorkspaceBranch(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return false;
        }

        return branchName.StartsWith("users/", StringComparison.OrdinalIgnoreCase)
            || branchName.StartsWith("workspace/", StringComparison.OrdinalIgnoreCase);
    }
}
