using System.Text.Json;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Knowledge;

public sealed class KnowledgePackProvisioner
{
    private static readonly JsonSerializerOptions StateJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IReadOnlyDictionary<string, IKnowledgePackProvider> _providers;
    private readonly KnowledgePackManagedFileWriter _managedFileWriter;

    public KnowledgePackProvisioner(IEnumerable<IKnowledgePackProvider> providers)
        : this(providers, new KnowledgePackManagedFileWriter())
    {
    }

    internal KnowledgePackProvisioner(IEnumerable<IKnowledgePackProvider> providers, KnowledgePackManagedFileWriter managedFileWriter)
    {
        _providers = providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
        _managedFileWriter = managedFileWriter;
    }

    public async Task<KnowledgePackResult> ProvisionAsync(WorkspaceDefinition definition, WorkspacePaths paths, bool explicitRegenerationRequested = false, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var runs = new List<ProvisionedKnowledgePackRunResult>();
        var warnings = new List<string>();
        var errors = new List<string>();

        foreach (var configuration in definition.KnowledgePacks.Where(pack => pack.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isRequired = string.Equals(configuration.Mode, WorkspaceKnowledgePackModes.Required, StringComparison.OrdinalIgnoreCase);

            if (!_providers.TryGetValue(configuration.Provider, out var provider))
            {
                var message = $"Knowledge Pack provider '{configuration.Provider}' is not registered.";
                RecordOutcome(configuration.Provider, "unavailable", isRequired, message, runs, warnings, errors, log);
                continue;
            }

            if (!provider.IsApplicable(definition, configuration))
            {
                var message = $"Knowledge Pack provider '{provider.ProviderId}' is not applicable for workspace '{definition.Workspace.Name}'.";
                RecordOutcome(provider.ProviderId, provider.Version, isRequired, message, runs, warnings, errors, log);
                continue;
            }

            try
            {
                var context = CreateContext(definition, paths, configuration, explicitRegenerationRequested);
                var providerContent = await provider.GenerateAsync(context, cancellationToken);
                var statePath = Path.Combine(context.ProviderRootPath, "state.json");
                var previousState = ReadState(statePath);
                var writeResult = _managedFileWriter.WriteFiles(context.ProviderRootPath, providerContent.GeneratedFiles, previousState, explicitRegenerationRequested);
                var runWarnings = providerContent.Warnings.Concat(writeResult.Warnings).ToList();
                var state = new ProvisionedKnowledgePackState
                {
                    ProviderVersion = provider.Version,
                    Metadata = new Dictionary<string, string>(providerContent.Metadata, StringComparer.OrdinalIgnoreCase),
                    SourceHashes = new Dictionary<string, string>(providerContent.SourceHashes, StringComparer.OrdinalIgnoreCase),
                    SourceLocations = new Dictionary<string, string>(providerContent.SourceLocations, StringComparer.OrdinalIgnoreCase),
                    ImportTimestamp = DateTimeOffset.UtcNow,
                    GeneratedFileHashes = writeResult.GeneratedFileHashes,
                    Warnings = runWarnings,
                    SkippedFiles = writeResult.SkippedFiles,
                };
                WriteState(statePath, state);
                runs.Add(new ProvisionedKnowledgePackRunResult
                {
                    Provider = provider.ProviderId,
                    Version = provider.Version,
                    IsRequired = isRequired,
                    Succeeded = true,
                    Warnings = runWarnings,
                    SkippedFiles = writeResult.SkippedFiles,
                });

                foreach (var warning in runWarnings)
                {
                    warnings.Add($"{provider.ProviderId}: {warning}");
                    log?.Invoke(new CommandLogEntry { Source = "app", Message = $"Knowledge Pack warning [{provider.ProviderId}]: {warning}" });
                }
            }
            catch (Exception exception)
            {
                RecordOutcome(provider.ProviderId, provider.Version, isRequired, exception.Message, runs, warnings, errors, log);
            }
        }

        return new KnowledgePackResult
        {
            Packs = runs,
            Warnings = warnings,
            Errors = errors,
        };
    }

    private static KnowledgePackContext CreateContext(WorkspaceDefinition definition, WorkspacePaths paths, WorkspaceKnowledgePackDefinition configuration, bool explicitRegenerationRequested)
    {
        var providerRootPath = Path.Combine(paths.OpencodePath, "knowledge", configuration.Provider);
        return new KnowledgePackContext
        {
            Definition = definition,
            Paths = paths,
            Configuration = configuration,
            ProviderRootPath = providerRootPath,
            GeneratedRootPath = Path.Combine(providerRootPath, "generated"),
            DocsRootPath = Path.Combine(providerRootPath, "docs"),
            IndexesRootPath = Path.Combine(providerRootPath, "indexes"),
            PromptsRootPath = Path.Combine(providerRootPath, "prompts"),
            SharedCacheRootPath = Path.Combine(paths.OpencodePath, "cache", "knowledge", configuration.Provider),
            ExplicitRegenerationRequested = explicitRegenerationRequested,
        };
    }

    private static ProvisionedKnowledgePackState? ReadState(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProvisionedKnowledgePackState>(File.ReadAllText(statePath), StateJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(string statePath, ProvisionedKnowledgePackState state)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(statePath, JsonSerializer.Serialize(state, StateJsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void RecordOutcome(string providerId, string version, bool isRequired, string message, List<ProvisionedKnowledgePackRunResult> runs, List<string> warnings, List<string> errors, Action<CommandLogEntry>? log)
    {
        runs.Add(new ProvisionedKnowledgePackRunResult
        {
            Provider = providerId,
            Version = version,
            IsRequired = isRequired,
            Succeeded = false,
            Errors = [message],
        });

        if (isRequired)
        {
            errors.Add($"{providerId}: {message}");
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"Knowledge Pack failed [{providerId}]: {message}" });
        }
        else
        {
            warnings.Add($"{providerId}: {message}");
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"Knowledge Pack warning [{providerId}]: {message}" });
        }
    }
}
