namespace OpenCode.Workspace.Core.Workspaces;

public static class OracleXdbReadinessEvidenceClassifier
{
    public static bool ShouldTreatAsFailure(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return true;
        }

        var values = ParseEvidence(evidence);
        var rootRegistry = GetRegistryStatus(values, "root_registry");
        var pdbRegistry = GetRegistryStatus(values, "pdb_registry");
        if (string.Equals(rootRegistry, "VALID", StringComparison.OrdinalIgnoreCase)
            && string.Equals(pdbRegistry, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryGetInt(values, "invalid_object_count", out var invalidObjectCount) && invalidObjectCount > 0)
        {
            return true;
        }

        if (HasMeaningfulValue(values, "dba_errors") || HasMeaningfulValue(values, "pdb_plug_in_violations"))
        {
            return true;
        }

        var rootFunctionalProbe = values.GetValueOrDefault("root_functional_probe", string.Empty);
        var pdbFunctionalProbe = values.GetValueOrDefault("pdb_functional_probe", string.Empty);
        if (IsSuccessfulFunctionalProbe(rootFunctionalProbe) && IsSuccessfulFunctionalProbe(pdbFunctionalProbe))
        {
            return false;
        }

        return true;
    }

    private static Dictionary<string, string> ParseEvidence(string evidence)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in evidence.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == part.Length - 1)
            {
                continue;
            }

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string GetRegistryStatus(IReadOnlyDictionary<string, string> values, string key)
    {
        var registry = values.GetValueOrDefault(key, string.Empty);
        if (string.IsNullOrWhiteSpace(registry))
        {
            return string.Empty;
        }

        var parts = registry.Split('|', StringSplitOptions.TrimEntries);
        return parts.Length >= 4 ? parts[3] : registry;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int value)
    {
        if (values.TryGetValue(key, out var raw) && int.TryParse(raw, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool HasMeaningfulValue(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = values.GetValueOrDefault(key, string.Empty);
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulFunctionalProbe(string probe)
        => !string.IsNullOrWhiteSpace(probe)
           && !string.Equals(probe, "failed", StringComparison.OrdinalIgnoreCase)
           && !probe.Contains("ORA-", StringComparison.OrdinalIgnoreCase)
           && !probe.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
           && probe.Contains("XMLTYPE=ok", StringComparison.OrdinalIgnoreCase)
           && probe.Contains("HTTPPORT=", StringComparison.OrdinalIgnoreCase);
}
