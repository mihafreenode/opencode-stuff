param(
    [ValidateSet('fast','mcp','api','live','non-oracle','oracle','all')]
    [string]$Suite = 'fast'
)

$ErrorActionPreference = 'Stop'

function Run-Fast {
    dotnet build OpenCode.Workspace.slnx -c Release
    dotnet test tests/OpenCode.Workspace.Core.Tests/OpenCode.Workspace.Core.Tests.csproj -c Release --no-build
    dotnet test tests/OpenCode.Workspace.Cli.Tests/OpenCode.Workspace.Cli.Tests.csproj -c Release --no-build
    dotnet test tests/OpenCode.Workspace.Avalonia.Tests/OpenCode.Workspace.Avalonia.Tests.csproj -c Release --no-build
    dotnet test tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj -c Release --no-build
    dotnet test tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj -c Release --no-build --filter Category=FastIntegration
}

function Run-Mcp {
    dotnet test tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj --filter Category=McpProtocolIntegration
}

function Run-Api {
    dotnet test tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj --filter Category=ApiIntegration
}

function Run-Live {
    dotnet test tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj --filter Category=LiveDockerIntegration
    dotnet test tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj --filter Category=LiveDockerIntegration
}

function Run-NonOracle {
    dotnet build OpenCode.Workspace.slnx -c Release
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run --family lightweight --format json
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run --family postgresql --format json
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run --family analytics --format json
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run --family document-processing --format json
}

function Run-Oracle {
    dotnet build OpenCode.Workspace.slnx -c Release
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run oracle-plsql-demo --format json
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run oracle-apex-demo --format json
    dotnet src/OpenCode.Workspace.Cli/bin/Release/net10.0/opencode.dll smoke run oracle-apexlang-demo --format json
}

switch ($Suite) {
    'fast' { Run-Fast }
    'mcp' { Run-Mcp }
    'api' { Run-Api }
    'live' { Run-Live }
    'non-oracle' { Run-NonOracle }
    'oracle' { Run-Oracle }
    'all' { Run-Fast; Run-Live; Run-NonOracle }
}
