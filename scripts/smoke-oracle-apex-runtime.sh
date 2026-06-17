#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
workspace_root=${1:-"$(mktemp -d)"}
runner_root=$(mktemp -d)

cleanup() {
  rm -rf "$runner_root"
}
trap cleanup EXIT

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'Missing required command: %s\n' "$1" >&2
    exit 1
  }
}

require_command dotnet
require_command docker
require_command curl

printf '[smoke] Repository root: %s\n' "$repo_root"
printf '[smoke] Workspace root: %s\n' "$workspace_root"

mkdir -p "$runner_root"

cat >"$runner_root/SmokeRunner.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$repo_root/src/OpenCode.Workspace.Core/OpenCode.Workspace.Core.csproj" />
  </ItemGroup>
</Project>
EOF

cat >"$runner_root/Program.cs" <<'EOF'
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

var repositoryRoot = args[0];
var workspaceRoot = args[1];
var templateId = args[2];

var provider = new BuiltInCatalogProvider(Path.Combine(repositoryRoot, "catalog"));
var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());
var template = provider.LoadTemplates().Single(item => item.Id == templateId);
var definition = new TemplateExpander().Expand("oracle-apex-runtime-smoke", template);
var ignorePolicyService = new WorkspaceIgnorePolicyService();
var orchestrator = new WorkspaceOrchestrator(
    new WorkspaceYamlService(),
    new WorkspaceDiscoveryService(),
    new WorkspaceRepository(Path.Combine(workspaceRoot, ".appdata")),
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
    ignorePolicyService,
    new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService),
    new DockerService(new ProcessRunner()),
    new NoOpTerminalLauncher());

Directory.CreateDirectory(workspaceRoot);
var snapshot = orchestrator.CreateWorkspace(workspaceRoot, definition);
await orchestrator.ProvisionAsync(snapshot, entry => Console.WriteLine($"[{entry.Source}] {entry.Message}"));
Console.WriteLine($"WORKSPACE_ROOT={workspaceRoot}");

sealed class NoOpTerminalLauncher : ITerminalLauncher
{
    public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
EOF

printf '[smoke] Creating and provisioning oracle-apex-demo...\n'
dotnet run --project "$runner_root/SmokeRunner.csproj" -- "$repo_root" "$workspace_root" oracle-apex-demo

compose_file="$workspace_root/compose.yaml"
if [[ ! -f "$compose_file" ]]; then
  printf '[smoke] compose.yaml was not generated at %s\n' "$compose_file" >&2
  exit 1
fi

printf '[smoke] Waiting for ORDS readiness...\n'
for attempt in 1 2 3 4 5 6 7 8 9 10; do
  http_code=$(curl -k -L -s -o /dev/null -w '%{http_code}' http://localhost:8181/ords || true)
  if [[ "$http_code" == "200" || "$http_code" == "301" || "$http_code" == "302" || "$http_code" == "303" ]]; then
    break
  fi

  if [[ "$attempt" == "10" ]]; then
    printf '[smoke] ORDS endpoint did not become ready. Last HTTP code: %s\n' "$http_code" >&2
    docker compose -f "$compose_file" --profile oracle-demo --profile oracle-ords ps || true
    exit 1
  fi

  sleep 15
done

printf '[smoke] Checking APEX login URL...\n'
apex_http_code=$(curl -k -L -s -o /dev/null -w '%{http_code}' http://localhost:8181/ords/apex_admin || true)
if [[ "$apex_http_code" != "200" && "$apex_http_code" != "301" && "$apex_http_code" != "302" && "$apex_http_code" != "303" ]]; then
  printf '[smoke] APEX login URL failed. HTTP code: %s\n' "$apex_http_code" >&2
  exit 1
fi

printf '[smoke] Running SQLcl test query...\n'
docker compose -f "$compose_file" --profile oracle-demo --profile oracle-ords exec -T workspace bash -lc "sql -S \"\${ORACLE_DEMO_CONNECTION:-demo_user/demo_password@//oracle-demo:1521/FREEPDB1}\" <<'SQL'
SELECT 'Runtime smoke OK' AS status FROM dual;
EXIT
SQL"

printf '[smoke] PASS: Oracle APEX runtime smoke validation completed.\n'
