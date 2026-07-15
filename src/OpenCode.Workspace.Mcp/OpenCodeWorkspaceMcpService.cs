using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenCode.Workspace.Mcp;

public interface IOpenCodeWorkspaceMcpService
{
    ServerHealthModel GetServerHealth();
    Task<IReadOnlyList<WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default);
    Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default);
    Task<WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<string>? progress = null, CancellationToken cancellationToken = default);
    Task<WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default);
    Task<WorkspaceSmokeResult> RunSmokeAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default);
    Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default);
    Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default);
    Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default);
    Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtifactListItem>> ListWorkspaceArtifactsAsync(string workspaceId, string? relativePath, bool recursive, CancellationToken cancellationToken = default);
    Task<ArtifactReadModel> GetWorkspaceArtifactAsync(string workspaceId, string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtifactListItem>> ListSmokeArtifactsAsync(string runId, string? relativePath, bool recursive, CancellationToken cancellationToken = default);
    Task<ArtifactReadModel> GetSmokeArtifactAsync(string runId, string relativePath, CancellationToken cancellationToken = default);
    Task<ArtifactReadModel> ReadArtifactByResourceUriAsync(string resourceUri, CancellationToken cancellationToken = default);
    Task<ArtifactResourceReadModel> ReadArtifactResourceAsync(string resourceUri, CancellationToken cancellationToken = default);
    Task<ExcelProcessResultModel> ProcessExcelArtifactAsync(string sourcePath, string? destinationWorkspaceId, string? processingTemplateId, string? outputLogicalName, CancellationToken cancellationToken = default);
}

public sealed class OpenCodeWorkspaceMcpService : IOpenCodeWorkspaceMcpService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".md", ".markdown", ".txt", ".log", ".yaml", ".yml", ".csv", ".html", ".sql", ".sh", ".ps1"
    };

    private readonly ILogger<OpenCodeWorkspaceMcpService> _logger;
    private readonly OpenCodeWorkspaceMcpOptions _options;
    private readonly string _catalogRoot;
    private readonly string _workspaceStateRoot;
    private readonly string _smokeArtifactsRoot;
    private readonly ProcessRunner _processRunner = new();
    private readonly WorkspaceYamlService _yamlService = new();
    private readonly WorkspaceDiscoveryService _discoveryService = new();
    private readonly WorkspaceRuntimeStateService _runtimeStateService = new();
    private readonly WorkspaceAppliedStateService _appliedStateService = new();
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceResolver _workspaceResolver;
    private readonly BuiltInCatalogProvider _catalogProvider;
    private readonly RuntimeOwnershipService _runtimeOwnershipService;
    private readonly SmokeRuntimeOwnershipService _smokeRuntimeOwnershipService;
    private readonly WorkspaceDoctorService _workspaceDoctorService;
    private readonly WorkspaceOrchestrator _workspaceOrchestrator;
    private readonly WorkspaceSmokeApplicationService _workspaceSmokeApplicationService;
    private readonly TemplateExpander _templateExpander = new();

    public OpenCodeWorkspaceMcpService(OpenCodeWorkspaceMcpOptions options, ILogger<OpenCodeWorkspaceMcpService> logger, string? catalogRoot = null, string? workspaceStateRoot = null, string? smokeArtifactsRoot = null)
    {
        _options = options;
        _logger = logger;
        _catalogRoot = catalogRoot
            ?? NormalizeOptionalPath(options.CatalogRoot)
            ?? ResolveCatalogRoot(Environment.CurrentDirectory)
            ?? ResolveCatalogRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Catalog root was not found. Run from the repository root or a package output that includes catalog/.");
        _workspaceStateRoot = workspaceStateRoot
            ?? NormalizeOptionalPath(options.WorkspaceStateRoot)
            ?? WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot();
        _smokeArtifactsRoot = smokeArtifactsRoot
            ?? NormalizeOptionalPath(options.SmokeArtifactsRoot)
            ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "template-smoke");
        Directory.CreateDirectory(_workspaceStateRoot);
        Directory.CreateDirectory(_smokeArtifactsRoot);

        _catalogProvider = new BuiltInCatalogProvider(_catalogRoot);
        _workspaceResolver = new WorkspaceResolver(_catalogProvider.LoadFeatures(), _catalogProvider.LoadServices(), _catalogProvider.LoadCapabilities(), _catalogProvider.LoadKnowledgePacks());
        _workspaceRepository = new WorkspaceRepository(_workspaceStateRoot);
        var containerRuntime = new DockerContainerRuntime(new DockerService(_processRunner));
        _runtimeOwnershipService = new RuntimeOwnershipService(containerRuntime);
        _smokeRuntimeOwnershipService = new SmokeRuntimeOwnershipService(containerRuntime);
        _workspaceDoctorService = new WorkspaceDoctorService(new PlatformDetector(_processRunner), new RuntimeResolver(), _discoveryService, _yamlService, _runtimeStateService);
        _workspaceOrchestrator = new WorkspaceOrchestrator(
            _yamlService,
            _discoveryService,
            _workspaceRepository,
            _workspaceResolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            _appliedStateService,
            new WorkspaceCheckpointService(),
            new WorkspaceTimelineService(),
            new WorkspaceSafetyService(),
            new WorkspaceIgnorePolicyService(),
            _runtimeStateService,
            new GitWorkspaceProvider(_processRunner, new WorkspaceIgnorePolicyService()),
            containerRuntime,
            new PlatformDetector(_processRunner),
            new RuntimeResolver(),
            new NullTerminalLauncher());
        _workspaceSmokeApplicationService = new WorkspaceSmokeApplicationService(_catalogRoot, Path.Combine(Path.GetTempPath(), "opencode-workspace-smoke-state"), containerRuntime);
    }

    public ServerHealthModel GetServerHealth()
        => new()
        {
            Transport = _options.Transport,
            CatalogRoot = _catalogRoot,
            WorkspaceStateRoot = _workspaceStateRoot,
            SmokeArtifactsRoot = _smokeArtifactsRoot,
            HttpEnabled = _options.Http.Enabled,
            HttpBinding = _options.Http.Enabled ? $"{_options.Http.Host}:{_options.Http.Port}" : string.Empty,
        };

    public Task<IReadOnlyList<WorkspaceTemplateSummaryModel>> ListWorkspaceTemplatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var smokeDefinitions = WorkspaceSmokeCatalog.BuildDefinitions(_catalogProvider.LoadTemplates())
            .ToDictionary(item => item.TemplateId, StringComparer.OrdinalIgnoreCase);
        var templates = _catalogProvider.LoadTemplates()
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(template => ToTemplateSummary(template, smokeDefinitions[template.Id]))
            .ToArray();
        return Task.FromResult<IReadOnlyList<WorkspaceTemplateSummaryModel>>(templates);
    }

    public Task<WorkspaceTemplateDetailModel> GetWorkspaceTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = _catalogProvider.LoadTemplates().SingleOrDefault(item => string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new OpenCodeWorkspaceMcpException("unknown_template", $"Unknown template '{templateId}'.", "Use list_workspace_templates to discover stable template ids.");
        var smoke = WorkspaceSmokeCatalog.BuildDefinition(template);
        var resolved = _workspaceResolver.Resolve(_templateExpander.Expand(template.DisplayName, template));
        return Task.FromResult(new WorkspaceTemplateDetailModel
        {
            Summary = ToTemplateSummary(template, smoke),
            WorkspaceImage = template.WorkspaceImage ?? "ubuntu:24.04",
            Services = template.Services,
            Skills = template.Skills,
            McpModules = template.Mcp,
            Template = template,
            ResolvedFeatures = resolved.Features,
            ResolvedCapabilities = resolved.Capabilities,
            ResolvedServices = resolved.Services,
        });
    }

    public Task<WorkspaceSmokeDefinitionCatalogResult> ListSmokeDefinitionsAsync(CancellationToken cancellationToken = default)
        => _workspaceSmokeApplicationService.ListDefinitionsAsync(null, cancellationToken);

    public async Task<IReadOnlyList<WorkspaceRecordModel>> ListWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var records = _workspaceRepository.LoadAll();
        var results = new List<WorkspaceRecordModel>(records.Count);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await GetWorkspaceInternalAsync(record, cancellationToken));
            }
            catch (Exception exception)
            {
                results.Add(new WorkspaceRecordModel
                {
                    WorkspaceId = record.Name,
                    Name = record.Name,
                    WorkspaceRoot = record.RootPath,
                    Template = record.Name,
                    Status = WorkspaceHealthStatus.Unavailable.ToString(),
                    Readiness = WorkspaceReadinessStatus.Unavailable.ToString(),
                    RuntimeState = WorkspaceRuntimeState.Unknown.ToString(),
                    Warnings = [exception.Message],
                    Snapshot = new WorkspaceSnapshot
                    {
                        Record = record,
                        Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = WorkspacePathBuilder.Slugify(record.Name), Name = record.Name, Image = "ubuntu:24.04" } },
                        Paths = WorkspacePathBuilder.Build(record.RootPath, record.ConfigurationPath),
                        ConfigurationPath = record.ConfigurationPath,
                        RuntimeState = WorkspaceRuntimeState.Unknown,
                        Safety = new WorkspaceSafetySnapshot
                        {
                            OverallStatus = WorkspaceSafetyLevel.NeedsReview,
                            Headline = "Workspace snapshot could not be loaded.",
                            Message = exception.Message,
                            LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                            Backup = new WorkspaceBackupSnapshot(),
                            IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                            AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
                        },
                        Session = new WorkspaceSessionSnapshot(),
                    },
                });
            }
        }

        return results.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<WorkspaceRecordModel> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var record = ResolveWorkspaceRecord(workspaceId);
        return await GetWorkspaceInternalAsync(record, cancellationToken);
    }

    public async Task<WorkspaceRecordModel> CreateWorkspaceAsync(string templateId, string workspaceName, string destinationRoot, CancellationToken cancellationToken = default)
    {
        var template = _catalogProvider.LoadTemplates().SingleOrDefault(item => string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new OpenCodeWorkspaceMcpException("unknown_template", $"Unknown template '{templateId}'.", "Use list_workspace_templates to discover stable template ids.");
        var workspaceRoot = Path.Combine(Path.GetFullPath(destinationRoot), workspaceName.Trim());
        var definition = BuildWorkspaceDefinition(workspaceName, template);
        var snapshot = await _workspaceOrchestrator.CreateWorkspaceAsync(workspaceRoot, definition, cancellationToken: cancellationToken, includeRuntimeInspection: true);
        return ToWorkspaceRecordModel(snapshot);
    }

    private static WorkspaceDefinition BuildWorkspaceDefinition(string workspaceName, TemplateManifest template)
        => new()
        {
            Workspace = new WorkspaceMetadata
            {
                Name = workspaceName,
                Id = WorkspacePathBuilder.Slugify(workspaceName),
                Image = string.IsNullOrWhiteSpace(template.WorkspaceImage) ? "ubuntu:24.04" : template.WorkspaceImage,
            },
            Provider = new WorkspaceProviderDefinition { Type = "git" },
            Runtime = new WorkspaceRuntimeDefinition { Default = "default", Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion },
            Features = template.Features.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Services = template.Services.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Skills = template.Skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Mcp = template.Mcp.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };

    public async Task<WorkspaceRecordModel> ProvisionWorkspaceAsync(string workspaceId, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var snapshot = (await GetWorkspaceAsync(workspaceId, cancellationToken)).Snapshot;
        await _workspaceOrchestrator.ProvisionAsync(snapshot, entry => progress?.Invoke(entry.Message), cancellationToken);
        return await GetWorkspaceAsync(workspaceId, cancellationToken);
    }

    public async Task<WorkspaceRecordModel> ValidateWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
        => await GetWorkspaceAsync(workspaceId, cancellationToken);

    public async Task<WorkspaceRecordModel> StopWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var snapshot = (await GetWorkspaceAsync(workspaceId, cancellationToken)).Snapshot;
        await _workspaceOrchestrator.StopAsync(snapshot, cancellationToken: cancellationToken);
        return await GetWorkspaceAsync(workspaceId, cancellationToken);
    }

    public async Task<WorkspaceRecordModel> RemoveWorkspaceRuntimeAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var snapshot = (await GetWorkspaceAsync(workspaceId, cancellationToken)).Snapshot;
        await _workspaceOrchestrator.RemoveDockerResourcesAsync(snapshot, cancellationToken: cancellationToken);
        return await GetWorkspaceAsync(workspaceId, cancellationToken);
    }

    public Task<IReadOnlyList<WorkspaceSmokeDefinition>> SelectSmokeDefinitionsAsync(WorkspaceSmokeDefinitionSelectionRequest request, CancellationToken cancellationToken = default)
        => _workspaceSmokeApplicationService.SelectDefinitionsAsync(request, cancellationToken);

    public Task<WorkspaceSmokeResult> RunSmokeAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken = default)
    {
        var safeRoot = string.IsNullOrWhiteSpace(request.ArtifactsRoot) ? _smokeArtifactsRoot : Path.GetFullPath(request.ArtifactsRoot);
        return _workspaceSmokeApplicationService.RunAsync(new WorkspaceSmokeSingleRunRequest
        {
            TemplateId = request.TemplateId,
            ArtifactsRoot = safeRoot,
            KeepRuntimeOnFailure = request.KeepRuntimeOnFailure,
            KeepWorkspace = request.KeepWorkspace,
            Timeout = request.Timeout,
            DryRun = request.DryRun,
            WorkspaceRoot = request.WorkspaceRoot,
            Progress = request.Progress,
        }, cancellationToken);
    }

    public Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken = default)
    {
        var safeRoot = string.IsNullOrWhiteSpace(request.ArtifactsRoot) ? _smokeArtifactsRoot : Path.GetFullPath(request.ArtifactsRoot);
        return _workspaceSmokeApplicationService.RunMatrixAsync(new WorkspaceSmokeMatrixRunRequest
        {
            TemplateIds = request.TemplateIds,
            ArtifactsRoot = safeRoot,
            ParallelCount = request.ParallelCount,
            KeepRuntimeOnFailure = request.KeepRuntimeOnFailure,
            KeepWorkspace = request.KeepWorkspace,
            MatrixTimeout = request.MatrixTimeout,
            RunTimeoutOverride = request.RunTimeoutOverride,
            Progress = request.Progress,
        }, cancellationToken);
    }

    public Task<RuntimeResourceInventory> ListRuntimeResourcesAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default)
        => _runtimeOwnershipService.BuildInventoryAsync(query, cancellationToken);

    public Task<RuntimeResourceInventory> RunRuntimeDoctorAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken = default)
        => _runtimeOwnershipService.BuildInventoryAsync(query, cancellationToken);

    public Task<SmokeCleanupResult> CleanupSmokeResourcesAsync(SmokeCleanupOptions options, CancellationToken cancellationToken = default)
        => _smokeRuntimeOwnershipService.CleanupAsync(options, cancellationToken);

    public Task<IReadOnlyList<ArtifactListItem>> ListWorkspaceArtifactsAsync(string workspaceId, string? relativePath, bool recursive, CancellationToken cancellationToken = default)
    {
        var snapshot = ResolveWorkspaceSnapshot(workspaceId);
        var root = ResolveSubdirectory(snapshot.Paths.ArtifactsPath, relativePath);
        return Task.FromResult<IReadOnlyList<ArtifactListItem>>(EnumerateArtifacts(root, recursive).ToArray());
    }

    public Task<ArtifactReadModel> GetWorkspaceArtifactAsync(string workspaceId, string relativePath, CancellationToken cancellationToken = default)
    {
        var snapshot = ResolveWorkspaceSnapshot(workspaceId);
        return ReadArtifactAsync(snapshot.Paths.ArtifactsPath, relativePath, cancellationToken);
    }

    public Task<IReadOnlyList<ArtifactListItem>> ListSmokeArtifactsAsync(string runId, string? relativePath, bool recursive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = ResolveSmokeArtifactDirectory(runId);
        var path = ResolveSubdirectory(root, relativePath);
        return Task.FromResult<IReadOnlyList<ArtifactListItem>>(EnumerateArtifacts(path, recursive).ToArray());
    }

    public Task<ArtifactReadModel> GetSmokeArtifactAsync(string runId, string relativePath, CancellationToken cancellationToken = default)
        => ReadArtifactAsync(ResolveSmokeArtifactDirectory(runId), relativePath, cancellationToken);

    public Task<ArtifactReadModel> ReadArtifactByResourceUriAsync(string resourceUri, CancellationToken cancellationToken = default)
    {
        ArtifactResourceDescriptor descriptor;
        try
        {
            var encoded = new Uri(resourceUri).Segments.LastOrDefault()?.Trim('/');
            if (string.IsNullOrWhiteSpace(encoded))
            {
                throw new FormatException();
            }

            descriptor = JsonSerializer.Deserialize<ArtifactResourceDescriptor>(Encoding.UTF8.GetString(Base64UrlDecode(encoded)))
                ?? throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException or UriFormatException or ArgumentException)
        {
            throw new OpenCodeWorkspaceMcpException("invalid_artifact_resource_id", "Artifact resource was not found.", "Use artifact resource URIs returned by the MCP host.");
        }

        return descriptor.Kind switch
        {
            "workspace" => GetWorkspaceArtifactAsync(descriptor.OwnerId, descriptor.RelativePath, cancellationToken),
            "smoke" => GetSmokeArtifactAsync(descriptor.OwnerId, descriptor.RelativePath, cancellationToken),
            "smokeRoot" => ReadArtifactAsync(_smokeArtifactsRoot, descriptor.RelativePath, cancellationToken),
            _ => throw new OpenCodeWorkspaceMcpException("invalid_artifact_resource_id", "Artifact resource was not found.", "Use artifact resource URIs returned by the MCP host."),
        };
    }

    public async Task<ArtifactResourceReadModel> ReadArtifactResourceAsync(string resourceUri, CancellationToken cancellationToken = default)
    {
        var artifact = await ReadArtifactByResourceUriAsync(resourceUri, cancellationToken);
        var encoded = new Uri(resourceUri).Segments.LastOrDefault()?.Trim('/');
        var descriptor = JsonSerializer.Deserialize<ArtifactResourceDescriptor>(Encoding.UTF8.GetString(Base64UrlDecode(encoded ?? string.Empty)))
            ?? throw new InvalidOperationException("Artifact resource was not found.");
        var root = descriptor.Kind == "workspace"
            ? ResolveWorkspaceSnapshot(descriptor.OwnerId).Paths.ArtifactsPath
            : descriptor.Kind == "smoke"
                ? ResolveSmokeArtifactDirectory(descriptor.OwnerId)
                : descriptor.Kind == "smokeRoot"
                    ? _smokeArtifactsRoot
                    : throw new OpenCodeWorkspaceMcpException("invalid_artifact_resource_id", "Artifact resource was not found.", "Use artifact resource URIs returned by the MCP host.");
        var bytes = await File.ReadAllBytesAsync(ResolveFile(root, descriptor.RelativePath), cancellationToken);
        return new ArtifactResourceReadModel { Artifact = artifact, Bytes = bytes };
    }

    public async Task<ExcelProcessResultModel> ProcessExcelArtifactAsync(string sourcePath, string? destinationWorkspaceId, string? processingTemplateId, string? outputLogicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationWorkspaceId) && string.IsNullOrWhiteSpace(processingTemplateId))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_request", "Provide either a destination workspace id or a processing template id.");
        }

        var validatedSource = ValidateAllowedPath(sourcePath);
        if (!File.Exists(validatedSource) || !string.Equals(Path.GetExtension(validatedSource), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("invalid_workbook", "Invalid workbook. Provide an existing .xlsx file under an allowed root.", "Use a valid .xlsx artifact under a workspace or smoke artifact root.");
        }

        using (SpreadsheetDocument.Open(validatedSource, false))
        {
        }

        var sourceChecksum = ToHex(SHA256.HashData(await File.ReadAllBytesAsync(validatedSource, cancellationToken)));
        var destinationRoot = !string.IsNullOrWhiteSpace(destinationWorkspaceId)
            ? Path.Combine(ResolveWorkspaceSnapshot(destinationWorkspaceId).Paths.ArtifactRunsPath, "mcp")
            : Path.Combine(_smokeArtifactsRoot, "excel-results", processingTemplateId!.Trim());
        Directory.CreateDirectory(destinationRoot);
        var outputName = string.IsNullOrWhiteSpace(outputLogicalName) ? $"{Path.GetFileNameWithoutExtension(validatedSource)}-opencode-result.xlsx" : $"{outputLogicalName.Trim()}.xlsx";
        var outputPath = Path.Combine(destinationRoot, outputName);
        File.Copy(validatedSource, outputPath, overwrite: true);

        using (var document = SpreadsheetDocument.Open(outputPath, true))
        {
            var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Invalid workbook. Missing workbook part.");
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            sheetData.Append(RowOf("Processed UTC", DateTimeOffset.UtcNow.ToString("O")));
            sheetData.Append(RowOf("Source filename", Path.GetFileName(validatedSource)));
            sheetData.Append(RowOf("Source checksum", sourceChecksum));
            sheetData.Append(RowOf("Selected workspace/template", destinationWorkspaceId ?? processingTemplateId ?? string.Empty));
            sheetData.Append(RowOf("Operation status", "processed"));
            var sheets = workbookPart.Workbook.GetFirstChild<Sheets>() ?? workbookPart.Workbook.AppendChild(new Sheets());
            var newSheetId = sheets.Elements<Sheet>().Select(item => item.SheetId?.Value ?? 0U).DefaultIfEmpty().Max() + 1;
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = newSheetId,
                Name = "OpenCode Result",
            });
            workbookPart.Workbook.Save();
        }

        var outputChecksum = ToHex(SHA256.HashData(await File.ReadAllBytesAsync(outputPath, cancellationToken)));
        return new ExcelProcessResultModel
        {
            OutputPath = outputPath,
            ResourceUri = !string.IsNullOrWhiteSpace(destinationWorkspaceId)
                ? CreateArtifactResourceUri("workspace", destinationWorkspaceId!, Path.GetRelativePath(destinationRoot, outputPath))
                : CreateArtifactResourceUri("smokeRoot", "smoke", Path.GetRelativePath(_smokeArtifactsRoot, outputPath)),
            OutputChecksumSha256 = outputChecksum,
            SourceChecksumSha256 = sourceChecksum,
            ProcessedUtc = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                $"validated={validatedSource}",
                $"output={outputPath}",
            ],
        };
    }

    private WorkspaceTemplateSummaryModel ToTemplateSummary(TemplateManifest template, WorkspaceSmokeDefinition smoke)
    {
        var resolved = _workspaceResolver.Resolve(_templateExpander.Expand(template.DisplayName, template));
        return new WorkspaceTemplateSummaryModel
        {
            TemplateId = template.Id,
            DisplayName = template.DisplayName,
            Description = template.Description,
            Family = smoke.Family,
            Features = template.Features,
            Capabilities = resolved.Capabilities.Select(item => item.Id).ToArray(),
            Provisionable = true,
            SmokeSupported = smoke.Supported,
            ResourceClass = smoke.ResourceClass.ToString(),
            ExpectedServices = smoke.ExpectedServices,
        };
    }

    private async Task<WorkspaceRecordModel> GetWorkspaceInternalAsync(WorkspaceRecord record, CancellationToken cancellationToken)
    {
        var snapshot = await _workspaceOrchestrator.LoadSnapshotAsync(record.RootPath, cancellationToken, includeRuntimeInspection: true);
        return ToWorkspaceRecordModel(snapshot);
    }

    private WorkspaceRecord ResolveWorkspaceRecord(string workspaceId)
    {
        var record = _workspaceRepository.LoadAll().FirstOrDefault(item => string.Equals(item.RootPath, workspaceId, StringComparison.OrdinalIgnoreCase));
        if (record is not null)
        {
            return record;
        }

        foreach (var candidate in _workspaceRepository.LoadAll())
        {
            var configurationPath = WorkspaceRecordPathResolver.GetWorkspaceConfigurationPath(candidate);
            if (!File.Exists(configurationPath))
            {
                continue;
            }

            try
            {
                var definition = _yamlService.Read(configurationPath);
                if (string.Equals(definition.Workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        throw new OpenCodeWorkspaceMcpException("workspace_not_found", $"Workspace '{workspaceId}' was not found.", "Refresh the local workspace list and retry.");
    }

    private WorkspaceSnapshot ResolveWorkspaceSnapshot(string workspaceId)
        => GetWorkspaceAsync(workspaceId).GetAwaiter().GetResult().Snapshot;

    private WorkspaceRecordModel ToWorkspaceRecordModel(WorkspaceSnapshot snapshot)
    {
        var warnings = snapshot.Health.OverallStatus != WorkspaceHealthStatus.Healthy
            ? new[] { snapshot.Health.Summary }
            : Array.Empty<string>();
        return new WorkspaceRecordModel
        {
            WorkspaceId = snapshot.Definition.Workspace.Id,
            Name = snapshot.Definition.Workspace.Name,
            Template = InferTemplate(snapshot.Definition),
            WorkspaceRoot = snapshot.Paths.RootPath,
            Status = snapshot.Health.OverallStatus.ToString(),
            Readiness = snapshot.Readiness.Status.ToString(),
            RuntimeState = snapshot.RuntimeState.ToString(),
            AvailableServices = snapshot.AvailableServices.Select(item => item.ServiceId).ToArray(),
            DocumentationPaths = BuildDocumentationPaths(snapshot.Paths.RootPath),
            Warnings = warnings,
            Snapshot = snapshot,
        };
    }

    private static string InferTemplate(WorkspaceDefinition definition)
    {
        var templates = new[]
        {
            "oracle-apexlang-demo", "oracle-apex-demo", "oracle-plsql-demo", "documentation-analysis", "education-stem-demo", "data-processing", "web-testing", "general-development", "empty-workspace"
        };
        return templates.FirstOrDefault(templateId => definition.Features.Any(feature => feature.IndexOf(templateId.Replace("-demo", string.Empty), StringComparison.OrdinalIgnoreCase) >= 0))
            ?? definition.Workspace.Id;
    }

    private static IReadOnlyList<string> BuildDocumentationPaths(string rootPath)
    {
        var docsRoot = Path.Combine(rootPath, "docs");
        if (!Directory.Exists(docsRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)
            .Take(20)
            .Select(path => Path.GetRelativePath(rootPath, path))
            .ToArray();
    }

    private IEnumerable<ArtifactListItem> EnumerateArtifacts(string root, bool recursive)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<ArtifactListItem>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFileSystemEntries(root, "*", option)
            .Select(path => CreateArtifactListItem(root, path))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ArtifactListItem CreateArtifactListItem(string root, string path)
    {
        var isDirectory = Directory.Exists(path);
        var info = isDirectory ? null : new FileInfo(path);
        var relativePath = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        return new ArtifactListItem
        {
            RelativePath = relativePath,
            DisplayName = Path.GetFileName(path),
            IsDirectory = isDirectory,
            SizeBytes = info?.Length ?? 0,
            LastModifiedUtc = info?.LastWriteTimeUtc ?? Directory.GetLastWriteTimeUtc(path),
            MimeType = isDirectory ? "inode/directory" : GetMimeType(path),
            ResourceUri = CreateArtifactResourceUri(root.StartsWith(_smokeArtifactsRoot, StringComparison.OrdinalIgnoreCase) ? "smoke" : "workspace", root.StartsWith(_smokeArtifactsRoot, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(root) : ResolveWorkspaceIdFromPath(root), relativePath),
        };
    }

    private async Task<ArtifactReadModel> ReadArtifactAsync(string root, string relativePath, CancellationToken cancellationToken)
    {
        var filePath = ResolveFile(root, relativePath);
        var metadata = CreateArtifactListItem(root, filePath);
        var checksum = ToHex(SHA256.HashData(await File.ReadAllBytesAsync(filePath, cancellationToken)));
        var isText = !metadata.IsDirectory && TextExtensions.Contains(Path.GetExtension(filePath));
        var tooLarge = new FileInfo(filePath).Length > _options.Artifacts.MaxReadBytes;
        var text = isText && !tooLarge ? await File.ReadAllTextAsync(filePath, cancellationToken) : string.Empty;
        return new ArtifactReadModel
        {
            Metadata = metadata,
            IsTextInline = isText && !tooLarge,
            Text = text,
            TooLarge = tooLarge,
            ChecksumSha256 = checksum,
        };
    }

    private string ResolveSmokeArtifactDirectory(string runId)
    {
        if (!Directory.Exists(_smokeArtifactsRoot))
        {
            throw new OpenCodeWorkspaceMcpException("artifact_not_found", $"Smoke run '{runId}' was not found.", "List smoke artifacts or inspect the completed operation result first.");
        }

        var match = Directory.EnumerateDirectories(_smokeArtifactsRoot, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), runId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new OpenCodeWorkspaceMcpException("artifact_not_found", $"Smoke run '{runId}' was not found.", "List smoke artifacts or inspect the completed operation result first.");
        }

        return match;
    }

    private static string ResolveSubdirectory(string root, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return root;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenCodeWorkspaceMcpException("artifact_outside_allowed_root", "Artifact path is outside the allowed root.", "Use workspace or smoke artifact paths only.");
        }

        return candidate;
    }

    private string ResolveFile(string root, string relativePath)
    {
        var file = ResolveSubdirectory(root, relativePath);
        if (!File.Exists(file))
        {
            throw new OpenCodeWorkspaceMcpException("artifact_not_found", "Artifact was not found.", "List artifacts first and retry with a valid relative path.");
        }

        return file;
    }

    private string ValidateAllowedPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var allowedRoots = _workspaceRepository.LoadAll().Select(item => item.RootPath)
            .Append(_smokeArtifactsRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .ToArray();
        if (!allowedRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OpenCodeWorkspaceMcpException("artifact_outside_allowed_root", "Artifact path is outside the allowed root.", "Use workspace or smoke artifact paths only.");
        }

        return fullPath;
    }

    private string ResolveWorkspaceIdFromPath(string root)
    {
        var match = _workspaceRepository.LoadAll().FirstOrDefault(item => root.StartsWith(item.RootPath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return string.Empty;
        }

        var configuration = WorkspaceRecordPathResolver.GetWorkspaceConfigurationPath(match);
        if (!File.Exists(configuration))
        {
            return match.Name;
        }

        return _yamlService.Read(configuration).Workspace.Id;
    }

    private static string GetMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            ".yaml" or ".yml" => "application/yaml",
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };

    private string CreateArtifactResourceUri(string kind, string ownerId, string relativePath)
    {
        var descriptor = new ArtifactResourceDescriptor { Kind = kind, OwnerId = ownerId, RelativePath = relativePath };
        return $"opencode://artifacts/{Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(descriptor)))}";
    }

    internal static string? ResolveCatalogRoot(string workspacePath)
    {
        var roots = new[] { workspacePath, Environment.CurrentDirectory, AppContext.BaseDirectory }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var start in roots)
        {
            var current = start;
            while (!string.IsNullOrWhiteSpace(current))
            {
                var candidate = Path.Combine(current, "catalog");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        return null;
    }

    private static string? NormalizeOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static Row RowOf(string name, string value)
        => new(
            new Cell { DataType = CellValues.String, CellValue = new CellValue(name) },
            new Cell { DataType = CellValues.String, CellValue = new CellValue(value) });

    private static string ToHex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class NullTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ArtifactResourceDescriptor
    {
        public string Kind { get; init; } = string.Empty;
        public string OwnerId { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
    }
}
