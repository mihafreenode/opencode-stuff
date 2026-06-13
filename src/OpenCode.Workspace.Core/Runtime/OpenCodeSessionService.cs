using System.Text.Json;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class OpenCodeSessionService
{
    public IReadOnlyList<OpenCodeSessionListItem> ParseSessionList(string output)
    {
        var items = new List<OpenCodeSessionListItem>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return items;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("Session ID", StringComparison.Ordinal)
                || line.StartsWith("─", StringComparison.Ordinal)
                || line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var firstGap = FindGap(line, 0);
            if (firstGap <= 0)
            {
                continue;
            }

            var id = line[..firstGap].Trim();
            var remainder = line[firstGap..].TrimStart();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(remainder))
            {
                continue;
            }

            var secondGap = FindGap(remainder, 0);
            var title = secondGap > 0 ? remainder[..secondGap].Trim() : remainder.Trim();
            items.Add(new OpenCodeSessionListItem(id, title));
        }

        return items;
    }

    public string? TryGetSessionDirectory(string exportJson)
    {
        if (string.IsNullOrWhiteSpace(exportJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(exportJson);
            if (document.RootElement.TryGetProperty("info", out var info)
                && info.TryGetProperty("directory", out var directory))
            {
                return directory.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public string? SelectLatestSessionForWorkspace(string sessionListOutput, Func<string, string?> exportDirectoryResolver, string workspaceDirectory)
    {
        foreach (var session in ParseSessionList(sessionListOutput))
        {
            var directory = exportDirectoryResolver(session.Id);
            if (string.Equals(directory, workspaceDirectory, StringComparison.Ordinal))
            {
                return session.Id;
            }
        }

        return null;
    }

    public async Task<string?> SelectLatestSessionForWorkspaceAsync(string sessionListOutput, Func<string, Task<string?>> exportDirectoryResolver, string workspaceDirectory)
    {
        foreach (var session in ParseSessionList(sessionListOutput))
        {
            var directory = await exportDirectoryResolver(session.Id);
            if (string.Equals(directory, workspaceDirectory, StringComparison.Ordinal))
            {
                return session.Id;
            }
        }

        return null;
    }

    private static int FindGap(string text, int startIndex)
    {
        var seenWhitespace = false;
        for (var index = startIndex; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                seenWhitespace = true;
                continue;
            }

            if (seenWhitespace)
            {
                return index - 1;
            }
        }

        return -1;
    }
}

public sealed record OpenCodeSessionListItem(string Id, string Title);
