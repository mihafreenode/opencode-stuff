namespace OpenCode.Workspace.Core.Knowledge;

public sealed class KnowledgePackResult
{
    public IReadOnlyList<ProvisionedKnowledgePackRunResult> Packs { get; init; } = Array.Empty<ProvisionedKnowledgePackRunResult>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool HasFailures => Errors.Count > 0 || Packs.Any(pack => !pack.Succeeded);

    public bool HasRequiredFailures => Packs.Any(pack => pack.IsRequired && !pack.Succeeded);
}

public sealed class ProvisionedKnowledgePackRunResult
{
    public required string Provider { get; init; }

    public required string Version { get; init; }

    public bool IsRequired { get; init; }

    public bool Succeeded { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SkippedFiles { get; init; } = Array.Empty<string>();
}
