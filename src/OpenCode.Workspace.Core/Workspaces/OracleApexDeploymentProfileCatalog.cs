using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

internal sealed class OracleApexDeploymentProfileCatalog
{
    private static readonly Regex PropertyPattern = new(@"^(?<key>[A-Za-z][A-Za-z0-9_\-]*)\s*:\s*(?<value>.*)$", RegexOptions.Compiled);

    public OracleApexDeploymentProfileDiscovery Discover(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, string? overrideProfileName = null)
    {
        var sourcePath = Path.Combine(rootPath, (environment.SourcePath ?? "src/apex").Replace('/', Path.DirectorySeparatorChar));
        var deploymentsRoot = Path.Combine(sourcePath, "deployments");
        var profiles = new List<OracleApexDeploymentProfile>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(deploymentsRoot))
        {
            foreach (var filePath in Directory.GetFiles(deploymentsRoot, "*.apx", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var profile = ParseProfile(rootPath, sourcePath, filePath);
                if (!names.Add(profile.Name))
                {
                    errors.Add($"Duplicate Oracle APEX deployment profile '{profile.Name}' was discovered under '{Path.GetRelativePath(rootPath, deploymentsRoot).Replace('\\', '/')}'.");
                    continue;
                }

                if (!profile.IsValid)
                {
                    errors.Add($"Deployment profile '{profile.Name}' is invalid. {profile.ValidationMessage}");
                }

                profiles.Add(profile);
            }
        }

        var configuredProfile = environment.DeploymentProfile?.Trim() ?? string.Empty;
        var activeProfile = !string.IsNullOrWhiteSpace(overrideProfileName)
            ? overrideProfileName.Trim()
            : !string.IsNullOrWhiteSpace(configuredProfile)
                ? configuredProfile
                : profiles.Any(profile => string.Equals(profile.Name, environmentName, StringComparison.OrdinalIgnoreCase))
                    ? environmentName
                    : profiles.Count == 1
                        ? profiles[0].Name
                        : string.Empty;

        var selectedProfile = string.IsNullOrWhiteSpace(activeProfile)
            ? null
            : profiles.FirstOrDefault(profile => string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(configuredProfile) && selectedProfile is null && string.IsNullOrWhiteSpace(overrideProfileName))
        {
            errors.Add($"Configured deployment profile '{configuredProfile}' was not found under '{Path.GetRelativePath(rootPath, deploymentsRoot).Replace('\\', '/')}'.");
        }

        if (!string.IsNullOrWhiteSpace(overrideProfileName) && selectedProfile is null)
        {
            errors.Add($"Deployment profile override '{overrideProfileName}' was not found under '{Path.GetRelativePath(rootPath, deploymentsRoot).Replace('\\', '/')}'.");
        }

        if (selectedProfile is not null)
        {
            if (selectedProfile.Properties.TryGetValue("workspace", out var workspace) && !string.IsNullOrWhiteSpace(environment.Workspace) && !string.Equals(workspace, environment.Workspace, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Deployment profile '{selectedProfile.Name}' targets workspace '{workspace}', but environment '{environmentName}' targets '{environment.Workspace}'.");
            }

            var deploymentSchema = GetProperty(selectedProfile, "parsing-schema") ?? GetProperty(selectedProfile, "parsingSchema") ?? GetProperty(selectedProfile, "schema");
            if (!string.IsNullOrWhiteSpace(deploymentSchema) && !string.IsNullOrWhiteSpace(environment.ParsingSchema) && !string.Equals(deploymentSchema, environment.ParsingSchema, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Deployment profile '{selectedProfile.Name}' targets schema '{deploymentSchema}', but environment '{environmentName}' targets '{environment.ParsingSchema}'.");
            }

            var deploymentApplicationId = GetProperty(selectedProfile, "application-id") ?? GetProperty(selectedProfile, "applicationId") ?? GetProperty(selectedProfile, "id");
            if (environment.ApplicationId is > 0 && int.TryParse(deploymentApplicationId, out var applicationId) && applicationId != environment.ApplicationId.Value)
            {
                warnings.Add($"Deployment profile '{selectedProfile.Name}' targets application {applicationId}, but environment '{environmentName}' targets {environment.ApplicationId.Value}.");
            }
        }

        var validation = errors.Count > 0
            ? string.Join(" ", errors)
            : selectedProfile is null && profiles.Count > 1
                ? "Multiple deployment profiles were discovered and no active deployment profile is configured."
                : selectedProfile is null
                    ? "No deployment profile selected."
                    : selectedProfile.ValidationMessage;

        return new OracleApexDeploymentProfileDiscovery
        {
            SourcePath = environment.SourcePath ?? "src/apex",
            DeploymentsRootPath = deploymentsRoot,
            ConfiguredProfileName = configuredProfile,
            ActiveProfileName = selectedProfile?.Name ?? activeProfile,
            ActiveProfilePath = selectedProfile?.RelativePath ?? string.Empty,
            Profiles = profiles,
            ValidationMessage = validation,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static OracleApexDeploymentProfile ParseProfile(string rootPath, string sourcePath, string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath).Trim();
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string firstMeaningfulLine = string.Empty;

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(firstMeaningfulLine))
            {
                firstMeaningfulLine = line;
            }

            var propertyMatch = PropertyPattern.Match(line);
            if (propertyMatch.Success)
            {
                properties[propertyMatch.Groups["key"].Value.Trim()] = propertyMatch.Groups["value"].Value.Trim().Trim('"', '\'');
            }
        }

        var relativePath = Path.GetRelativePath(sourcePath, filePath).Replace('\\', '/');
        var validRoot = firstMeaningfulLine.EndsWith("(", StringComparison.Ordinal)
            && firstMeaningfulLine.Contains("deployment", StringComparison.OrdinalIgnoreCase);
        var parsedName = validRoot
            ? firstMeaningfulLine[0..^1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).FirstOrDefault()
            : null;
        var name = string.IsNullOrWhiteSpace(parsedName) ? fileName : parsedName.Trim();

        return new OracleApexDeploymentProfile
        {
            Name = name,
            RelativePath = relativePath,
            AbsolutePath = filePath,
            Properties = properties,
            IsValid = validRoot,
            ValidationMessage = validRoot
                ? $"Deployment file '{relativePath}' is valid."
                : $"Expected a deployment root block in '{Path.GetRelativePath(rootPath, filePath).Replace('\\', '/')}'.",
        };
    }

    private static string? GetProperty(OracleApexDeploymentProfile profile, string key)
        => profile.Properties.TryGetValue(key, out var value) ? value : null;
}

internal sealed class OracleApexDeploymentProfileDiscovery
{
    public string SourcePath { get; init; } = string.Empty;
    public string DeploymentsRootPath { get; init; } = string.Empty;
    public string ConfiguredProfileName { get; init; } = string.Empty;
    public string ActiveProfileName { get; init; } = string.Empty;
    public string ActiveProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<OracleApexDeploymentProfile> Profiles { get; init; } = Array.Empty<OracleApexDeploymentProfile>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string ValidationMessage { get; init; } = string.Empty;
}

internal sealed class OracleApexDeploymentProfile
{
    public string Name { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string ValidationMessage { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
