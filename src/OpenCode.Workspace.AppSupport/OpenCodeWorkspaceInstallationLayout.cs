namespace OpenCode.Workspace.AppSupport;

public sealed class OpenCodeWorkspaceInstallationLayout
{
    private OpenCodeWorkspaceInstallationLayout(string applicationBasePath, string distributionRoot, string catalogRoot, string docsRoot, string localizationRoot, string configRoot)
    {
        ApplicationBasePath = applicationBasePath;
        DistributionRoot = distributionRoot;
        CatalogRoot = catalogRoot;
        DocsRoot = docsRoot;
        LocalizationRoot = localizationRoot;
        ConfigRoot = configRoot;
    }

    public string ApplicationBasePath { get; }
    public string DistributionRoot { get; }
    public string CatalogRoot { get; }
    public string DocsRoot { get; }
    public string LocalizationRoot { get; }
    public string ConfigRoot { get; }

    public string ReadmePath => Path.Combine(DistributionRoot, "README.md");
    public string LicensePath => Path.Combine(DistributionRoot, "LICENSE");
    public string ThirdPartyNoticesPath => Path.Combine(DistributionRoot, "THIRD-PARTY-NOTICES.md");

    public string GetConfigFilePath(string hostName)
        => Path.Combine(ConfigRoot, hostName, "appsettings.json");

    public static OpenCodeWorkspaceInstallationLayout Resolve(string applicationBasePath, string? explicitCatalogRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitCatalogRoot))
        {
            var fullCatalogRoot = Path.GetFullPath(explicitCatalogRoot);
            var distributionRoot = Path.GetDirectoryName(fullCatalogRoot) ?? fullCatalogRoot;
            return Create(applicationBasePath, distributionRoot, fullCatalogRoot);
        }

        foreach (var candidate in EnumerateCandidateRoots(applicationBasePath))
        {
            var catalogRoot = Path.Combine(candidate, "catalog");
            if (Directory.Exists(catalogRoot))
            {
                return Create(applicationBasePath, candidate, catalogRoot);
            }
        }

        throw new InvalidOperationException("Catalog root was not found. Run from the repository root or a package output that includes catalog/.");
    }

    private static OpenCodeWorkspaceInstallationLayout Create(string applicationBasePath, string distributionRoot, string catalogRoot)
        => new(
            Path.GetFullPath(applicationBasePath),
            Path.GetFullPath(distributionRoot),
            Path.GetFullPath(catalogRoot),
            Path.GetFullPath(Path.Combine(distributionRoot, "docs")),
            Path.GetFullPath(Path.Combine(distributionRoot, "Localization")),
            Path.GetFullPath(Path.Combine(distributionRoot, "config")));

    private static IEnumerable<string> EnumerateCandidateRoots(string applicationBasePath)
    {
        var current = Path.GetFullPath(applicationBasePath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        var workingDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        current = workingDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }
    }
}
