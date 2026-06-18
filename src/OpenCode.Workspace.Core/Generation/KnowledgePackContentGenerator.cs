using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Generation;

internal static class KnowledgePackContentGenerator
{
    public static IReadOnlyDictionary<string, string> Generate(ResolvedWorkspace workspace, Func<string, string> withGeneratedHeader)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var knowledgeMapGroup in workspace.KnowledgePacks
            .Where(pack => !string.IsNullOrWhiteSpace(pack.WorkspacePaths.KnowledgeMap))
            .GroupBy(pack => pack.WorkspacePaths.KnowledgeMap!, StringComparer.OrdinalIgnoreCase))
        {
            files[knowledgeMapGroup.Key] = withGeneratedHeader(BuildKnowledgeMap(knowledgeMapGroup.ToList()));
        }

        foreach (var pack in workspace.KnowledgePacks)
        {
            string? generatedSourceIndex = null;

            if (!string.IsNullOrWhiteSpace(pack.WorkspacePaths.SourceIndex))
            {
                generatedSourceIndex = withGeneratedHeader(BuildSourceIndex(pack));
                files[pack.WorkspacePaths.SourceIndex] = generatedSourceIndex;
            }

            foreach (var alias in pack.OutputAliases)
            {
                if (string.Equals(alias.Source, "source-index", StringComparison.OrdinalIgnoreCase) && generatedSourceIndex is not null)
                {
                    files[alias.Destination] = generatedSourceIndex;
                }
            }
        }

        return files;
    }

    public static IReadOnlyList<string> GetOnboardingLinks(ResolvedWorkspace workspace)
        => workspace.KnowledgePacks
            .SelectMany(pack => pack.Onboarding)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildKnowledgeMap(IReadOnlyList<KnowledgePackManifest> packs)
    {
        var primaryPack = packs[0];
        var knowledgeMapId = packs.Select(pack => pack.WorkspacePaths.KnowledgeMapId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? primaryPack.Id;
        var knowledgeMapTitle = packs.Select(pack => pack.WorkspacePaths.KnowledgeMapTitle).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? primaryPack.Title;
        var builder = new StringBuilder();
        builder.AppendLine($"id: {EscapeYamlScalar(knowledgeMapId)}");
        builder.AppendLine($"title: {EscapeYamlScalar(knowledgeMapTitle)}");
        builder.AppendLine($"category: {EscapeYamlScalar(primaryPack.Category)}");
        builder.AppendLine($"lifecycle: {EscapeYamlScalar(string.IsNullOrWhiteSpace(primaryPack.Lifecycle) ? CatalogConventions.StableLifecycle : primaryPack.Lifecycle!)}");
        builder.AppendLine("packs:");
        foreach (var pack in packs.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  - id: {EscapeYamlScalar(pack.Id)}");
            builder.AppendLine($"    title: {EscapeYamlScalar(pack.Title)}");
        }
        builder.AppendLine("sources:");

        foreach (var source in packs.SelectMany(pack => pack.Sources).OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  - name: {EscapeYamlScalar(source.Name)}");
            builder.AppendLine($"    category: {EscapeYamlScalar(source.Category)}");
            builder.AppendLine($"    url: {EscapeYamlScalar(source.Url)}");
            if (!string.IsNullOrWhiteSpace(source.Description))
            {
                builder.AppendLine($"    description: {EscapeYamlScalar(source.Description)}");
            }
        }

        var onboarding = packs.SelectMany(pack => pack.Onboarding).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
        if (onboarding.Count > 0)
        {
            builder.AppendLine("onboarding:");
            foreach (var path in onboarding)
            {
                builder.AppendLine($"  - {EscapeYamlScalar(path)}");
            }
        }

        var skillRefs = packs.SelectMany(pack => pack.SkillRefs).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
        if (skillRefs.Count > 0)
        {
            builder.AppendLine("skillRefs:");
            foreach (var skill in skillRefs)
            {
                builder.AppendLine($"  - {EscapeYamlScalar(skill)}");
            }
        }

        var outputs = packs
            .SelectMany(pack => EnumerateGeneratedOutputs(pack))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (outputs.Count > 0)
        {
            builder.AppendLine("outputs:");
            foreach (var output in outputs)
            {
                builder.AppendLine($"  - {EscapeYamlScalar(output)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<string> EnumerateGeneratedOutputs(KnowledgePackManifest pack)
    {
        if (!string.IsNullOrWhiteSpace(pack.WorkspacePaths.SourceIndex))
        {
            yield return pack.WorkspacePaths.SourceIndex;
        }

        foreach (var alias in pack.OutputAliases)
        {
            if (!string.IsNullOrWhiteSpace(alias.Destination))
            {
                yield return alias.Destination;
            }
        }
    }

    private static string BuildSourceIndex(KnowledgePackManifest pack)
    {
        var lines = new List<string>
        {
            $"# {pack.Title}",
            string.Empty,
            string.IsNullOrWhiteSpace(pack.Description) ? "Curated reference links for this knowledge pack." : pack.Description,
            string.Empty,
            "These references are a lightweight navigation layer. They are not mirrored documentation copies.",
        };

        foreach (var grouping in pack.Sources.GroupBy(source => string.IsNullOrWhiteSpace(source.Category) ? "general" : source.Category, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(string.Empty);
            lines.Add($"## {ToTitle(grouping.Key)}");
            lines.Add(string.Empty);

            foreach (var source in grouping.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"- [{source.Name}]({source.Url})");
                if (!string.IsNullOrWhiteSpace(source.Description))
                {
                    lines.Add($"  {source.Description}");
                }
            }
        }

        if (pack.Onboarding.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Agent Onboarding");
            lines.Add(string.Empty);
            foreach (var onboarding in pack.Onboarding.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"- `{onboarding}`");
            }
        }

        if (pack.SkillRefs.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Skill References");
            lines.Add(string.Empty);
            foreach (var skill in pack.SkillRefs.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"- `{skill}`");
            }
        }

        return string.Join("\n", lines);
    }

    private static string EscapeYamlScalar(string value)
        => value.Contains(':', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static string ToTitle(string value)
        => string.Join(' ', value.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
