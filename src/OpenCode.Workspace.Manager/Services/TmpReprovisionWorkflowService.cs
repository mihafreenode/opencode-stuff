using System.IO;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Manager.Services;

public sealed class TmpReprovisionWorkflowService
{
    private readonly string _applicationBasePath;
    private readonly ProcessRunner _processRunner;

    public TmpReprovisionWorkflowService(string applicationBasePath, ProcessRunner processRunner)
    {
        _applicationBasePath = applicationBasePath;
        _processRunner = processRunner;
    }

    public async Task RunAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var repositoryRoot = ResolveRepositoryRoot(_applicationBasePath);
        var projectPath = EnsureProjectGenerated(repositoryRoot, log);
        var projectRoot = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("Tmp reprovision project directory could not be resolved.");

        await RunCommandAsync(
            "dotnet",
            ["build-server", "shutdown"],
            repositoryRoot,
            "tmp-build",
            log,
            cancellationToken,
            TimeSpan.FromMinutes(2));

        log?.Invoke(new CommandLogEntry { Source = "dev", Message = $"Building tmp reprovision project '{projectPath}'." });
        await RunCommandAsync(
            "dotnet",
            ["build", projectPath],
            repositoryRoot,
            "tmp-build",
            log,
            cancellationToken,
            TimeSpan.FromMinutes(20));

        var builtExecutablePath = Path.Combine(projectRoot, "bin", "Debug", "net10.0", "ReprovisionWorkspace.exe");
        if (!File.Exists(builtExecutablePath))
        {
            throw new InvalidOperationException($"Tmp reprovision executable was not produced at '{builtExecutablePath}'.");
        }

        log?.Invoke(new CommandLogEntry { Source = "dev", Message = $"Running tmp reprovision executable for '{workspaceRootPath}'." });
        await RunCommandAsync(
            builtExecutablePath,
            [workspaceRootPath],
            repositoryRoot,
            "tmp-run",
            log,
            cancellationToken,
            TimeSpan.FromHours(2));
    }

    internal static string ResolveRepositoryRoot(string applicationBasePath)
    {
        var current = new DirectoryInfo(applicationBasePath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OpenCode.Workspace.Manager.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("The tmp reprovision workflow is a developer helper and only works from a repository checkout.");
    }

    internal static string EnsureProjectGenerated(string repositoryRoot, Action<CommandLogEntry>? log = null)
    {
        var projectRoot = Path.Combine(repositoryRoot, ".tmp", "ReprovisionWorkspace");
        Directory.CreateDirectory(projectRoot);

        log?.Invoke(new CommandLogEntry { Source = "dev", Message = $"Regenerating tmp reprovision workflow in '{projectRoot}'." });
        var projectPath = Path.Combine(projectRoot, "ReprovisionWorkspace.csproj");
        File.WriteAllText(projectPath, ReprovisionProjectFile());
        File.WriteAllText(Path.Combine(projectRoot, "Program.cs"), ReprovisionProgramFile());
        return projectPath;
    }

    private async Task RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string source,
        Action<CommandLogEntry>? log,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var result = await _processRunner.RunAsync(
            fileName,
            arguments,
            workingDirectory,
            (isError, line) => log?.Invoke(new CommandLogEntry { Source = isError ? $"{source}:err" : source, Message = line }),
            cancellationToken,
            timeout,
            diagnostic => log?.Invoke(new CommandLogEntry { Source = source, Message = diagnostic }));

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Tmp reprovision workflow step failed with exit code {result.ExitCode}: {result.Command}");
        }
    }

    private static string ReprovisionProjectFile() => """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/OpenCode.Workspace.AppSupport/OpenCode.Workspace.AppSupport.csproj" />
    <ProjectReference Include="../../src/OpenCode.Workspace.Core/OpenCode.Workspace.Core.csproj" />
  </ItemGroup>

</Project>
""";

    private static string ReprovisionProgramFile() => """
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: ReprovisionWorkspace <workspace-root>");
    return 1;
}

var workspaceRoot = args[0];
var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var catalogRoot = Path.Combine(repositoryRoot, "catalog");
var appDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot();

var provider = new BuiltInCatalogProvider(catalogRoot);
var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
var ignorePolicy = new WorkspaceIgnorePolicyService();
var orchestrator = new WorkspaceOrchestrator(
    new WorkspaceYamlService(),
    new WorkspaceDiscoveryService(),
    new WorkspaceRepository(appDataRoot),
    resolver,
    new ComposeGenerator(),
    new EnvironmentFileGenerator(),
    new ProvisioningScriptGenerator(),
    new TerminalArtifactsGenerator(),
    new AttachArtifactsGenerator(),
    new WorkspaceContentGenerator(),
    new WorkspaceAppliedStateService(),
    new WorkspaceCheckpointService(),
    new WorkspaceTimelineService(),
    new WorkspaceSafetyService(),
    ignorePolicy,
    new GitWorkspaceProvider(new ProcessRunner(), ignorePolicy),
    new DockerService(new ProcessRunner()),
    new NoOpTerminalLauncher());

var snapshot = await orchestrator.LoadSnapshotAsync(workspaceRoot);
await orchestrator.ProvisionAsync(snapshot, entry => Console.WriteLine($"[{entry.Source}] {entry.Message}"));

return 0;

sealed class NoOpTerminalLauncher : ITerminalLauncher
{
    public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
""";
}
