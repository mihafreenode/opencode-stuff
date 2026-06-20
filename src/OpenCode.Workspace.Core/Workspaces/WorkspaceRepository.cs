using System.Text.Json;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

/// <summary>
/// Stores a lightweight Windows-local index of known workspaces. The index helps
/// the app reopen workspaces quickly, but the authoritative workspace behavior is
/// still described by workspace.yaml inside each workspace folder.
/// </summary>
public sealed class WorkspaceRepository
{
    private readonly string _indexFilePath;
    private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    public WorkspaceRepository(string applicationDataRoot)
    {
        Directory.CreateDirectory(applicationDataRoot);
        _indexFilePath = Path.Combine(applicationDataRoot, "workspaces.json");
    }

    public string IndexFilePath => _indexFilePath;

    public IReadOnlyList<WorkspaceRecord> LoadAll()
    {
        if (!File.Exists(_indexFilePath))
        {
            return Array.Empty<WorkspaceRecord>();
        }

        var json = File.ReadAllText(_indexFilePath);
        return JsonSerializer.Deserialize<List<WorkspaceRecord>>(json, _serializerOptions) ?? new List<WorkspaceRecord>();
    }

    public void Save(WorkspaceRecord record)
    {
        var records = LoadAll().ToList();
        var existingIndex = records.FindIndex(existing => string.Equals(existing.RootPath, record.RootPath, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            records[existingIndex] = record;
        }
        else
        {
            records.Add(record);
        }

        Persist(records.OrderByDescending(item => item.LastOpenedUtc).ToList());
    }

    public Task SaveAsync(WorkspaceRecord record, CancellationToken cancellationToken = default)
        => Task.Run(() => Save(record), cancellationToken);

    public void Delete(string rootPath)
    {
        var remaining = LoadAll()
            .Where(record => !string.Equals(record.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Persist(remaining);
    }

    private void Persist(IReadOnlyList<WorkspaceRecord> records)
    {
        var json = JsonSerializer.Serialize(records, _serializerOptions);
        File.WriteAllText(_indexFilePath, json);
    }
}
