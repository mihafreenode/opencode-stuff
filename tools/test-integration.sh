#!/usr/bin/env bash
set -euo pipefail

suite="${1:-fast}"

run_fast() {
  dotnet build "OpenCode.Workspace.slnx" -c Release
  dotnet test "tests/OpenCode.Workspace.Core.Tests/OpenCode.Workspace.Core.Tests.csproj" -c Release --no-build
  dotnet test "tests/OpenCode.Workspace.Cli.Tests/OpenCode.Workspace.Cli.Tests.csproj" -c Release --no-build
  dotnet test "tests/OpenCode.Workspace.Avalonia.Tests/OpenCode.Workspace.Avalonia.Tests.csproj" -c Release --no-build
  dotnet test "tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj" -c Release --no-build
  dotnet test "tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj" -c Release --no-build --filter "Category=FastIntegration"
}

run_mcp() {
  dotnet test "tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj" --filter "Category=McpProtocolIntegration"
}

run_api() {
  dotnet test "tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj" --filter "Category=ApiIntegration"
}

run_live() {
  dotnet test "tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj" --filter "Category=LiveDockerIntegration"
  dotnet test "tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj" --filter "Category=LiveDockerIntegration"
}

run_non_oracle() {
  dotnet build "OpenCode.Workspace.slnx" -c Release
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run --family lightweight --format json
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run --family postgresql --format json
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run --family analytics --format json
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run --family document-processing --format json
}

run_oracle() {
  dotnet build "OpenCode.Workspace.slnx" -c Release
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run oracle-plsql-demo --format json
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run oracle-apex-demo --format json
  dotnet "src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll" smoke run oracle-apexlang-demo --format json
}

case "$suite" in
  fast) run_fast ;;
  mcp) run_mcp ;;
  api) run_api ;;
  live) run_live ;;
  non-oracle) run_non_oracle ;;
  oracle) run_oracle ;;
  all) run_fast; run_live; run_non_oracle ;;
  *)
    printf 'Unknown suite: %s\n' "$suite" >&2
    exit 2
    ;;
esac
