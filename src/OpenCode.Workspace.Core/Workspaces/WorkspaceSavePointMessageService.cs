using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceSavePointMessageService
{
    private readonly ProcessRunner _processRunner;

    public WorkspaceSavePointMessageService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<string> SuggestAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var statusResult = await _processRunner.RunAsync("git", ["status", "--porcelain"], workspaceRootPath, cancellationToken: cancellationToken);
            var lines = statusResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count == 0)
            {
                return "Capture current workspace state";
            }

            var changedFiles = new List<string>();
            var addedFiles = new List<string>();
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (line.Length < 4)
                {
                    continue;
                }

                var path = line[3..].Trim();
                if (line.StartsWith("??", StringComparison.Ordinal))
                {
                    addedFiles.Add(path);
                }
                else
                {
                    changedFiles.Add(path);
                }
            }

            var primaryPath = (changedFiles.Concat(addedFiles).FirstOrDefault() ?? "workspace files").Replace('\\', '/');
            var title = BuildTitle(primaryPath, changedFiles.Count, addedFiles.Count);
            var bodyLines = new List<string>();

            if (changedFiles.Count > 0)
            {
                bodyLines.Add($"- Update {changedFiles.Count} tracked file(s).");
            }

            if (addedFiles.Count > 0)
            {
                bodyLines.Add($"- Add {addedFiles.Count} new file(s).");
            }

            bodyLines.Add("- Capture the current workspace state for local recovery.");

            return string.Join(Environment.NewLine, new[] { title, string.Empty }.Concat(bodyLines));
        }
        catch
        {
            return "Capture current workspace state";
        }
    }

    private static string BuildTitle(string primaryPath, int changedCount, int addedCount)
    {
        var fileName = Path.GetFileNameWithoutExtension(primaryPath);
        if (fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileName(primaryPath);
        }

        if (addedCount > 0 && changedCount == 0)
        {
            return $"Add {ToTitleFragment(fileName)}";
        }

        if (changedCount > 0 && addedCount == 0)
        {
            return $"Update {ToTitleFragment(fileName)}";
        }

        return $"Save changes for {ToTitleFragment(fileName)}";
    }

    private static string ToTitleFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "workspace";
        }

        var cleaned = value.Replace('-', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "workspace" : cleaned;
    }
}
