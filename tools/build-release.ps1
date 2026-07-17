param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/release",
    [Nullable[bool]]$RunTests,
    [Nullable[bool]]$ValidatePackage,
    [switch]$Clean,
    [switch]$NoRestore,
    [switch]$NoArchive,
    [Nullable[bool]]$SelfContained
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:CurrentStage = "prerequisites"

function Fail([string]$Message) {
    throw "[$script:CurrentStage] $Message"
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [string]$WorkingDirectory = $RepositoryRoot,
        [hashtable]$Environment = @{}
    )

    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor Cyan

    $previous = @{}
    foreach ($entry in $Environment.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            Fail "dotnet command failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
        foreach ($entry in $Environment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $previous[$entry.Key])
        }
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 300,
        [switch]$RedirectOutput
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-CommandLineArguments -Arguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $RedirectOutput.IsPresent
    $startInfo.RedirectStandardError = $RedirectOutput.IsPresent
    foreach ($entry in $Environment.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            [void]$startInfo.Environment.Remove($entry.Key)
        }
        else {
            $startInfo.Environment[$entry.Key] = [string]$entry.Value
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        Fail "Failed to start '$FilePath'."
    }

    $stdoutTask = $null
    $stderrTask = $null
    if ($RedirectOutput.IsPresent) {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
        }
        catch {
        }

        $process.Dispose()
        Fail "Timed out waiting for '$FilePath' to exit within $TimeoutSeconds seconds."
    }

    $stdout = if ($stdoutTask) { $stdoutTask.GetAwaiter().GetResult() } else { "" }
    $stderr = if ($stderrTask) { $stderrTask.GetAwaiter().GetResult() } else { "" }
    $exitCode = $process.ExitCode
    $process.Dispose()

    [pscustomobject]@{
        ExitCode = $exitCode
        StandardOutput = $stdout
        StandardError = $stderr
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Wait-ForHttpSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [int]$TimeoutSeconds = 60
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 250
    }

    Fail "Timed out waiting for HTTP success from $Uri."
}

function Start-BackgroundProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-CommandLineArguments -Arguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($entry in $Environment.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            [void]$startInfo.Environment.Remove($entry.Key)
        }
        else {
            $startInfo.Environment[$entry.Key] = [string]$entry.Value
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        Fail "Failed to start background process '$FilePath'."
    }

    return $process
}

function Join-CommandLineArguments {
    param([string[]]$Arguments)

    return ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"{0}"' -f ($_.Replace('"', '\"'))
        }
        else {
            $_
        }
    }) -join ' '
}

function Resolve-Version {
    param([string]$ExplicitVersion)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitVersion)) {
        return $ExplicitVersion.Trim()
    }

    $gitLookup = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $gitLookup) {
        Fail "git is required to derive the local release version when -Version is not supplied."
    }

    $tagResult = Invoke-Native -FilePath "git.exe" -Arguments @("tag", "--points-at", "HEAD") -WorkingDirectory $RepositoryRoot -RedirectOutput -TimeoutSeconds 30
    if ($tagResult.ExitCode -eq 0) {
        $tag = ($tagResult.StandardOutput -split "`r?`n" | Where-Object { $_ -match '^v' } | Select-Object -First 1)
        if (-not [string]::IsNullOrWhiteSpace($tag)) {
            return $tag.Trim().TrimStart('v')
        }
    }

    return "0.1.0-local.{0}" -f ([DateTimeOffset]::UtcNow.ToString("yyyyMMddHHmmss"))
}

function Stop-RepoScopedTestHosts {
    param([string]$RepositoryRootPath)

    $escapedRepositoryRoot = [Regex]::Escape($RepositoryRootPath)
    $processes = Get-CimInstance Win32_Process -Filter "Name = 'testhost.exe' OR Name = 'dotnet.exe'" |
        Where-Object {
            $_.CommandLine -and $_.CommandLine -match $escapedRepositoryRoot -and $_.CommandLine -match 'testhost|OpenCode\.Workspace\.(Mcp|Api)\.Tests'
        }

    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
        }
        catch {
        }
    }
}

function Get-BooleanDefault {
    param([Nullable[bool]]$Value, [bool]$Default)
    if ($null -eq $Value) { return $Default }
    return [bool]$Value
}

$RepositoryRoot = Split-Path $PSScriptRoot -Parent
$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$ArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot "artifacts"))
$ReleaseToolDll = Join-Path $RepositoryRoot "tools\OpenCode.Workspace.ReleaseTool\bin\$Configuration\net10.0\OpenCode.Workspace.ReleaseTool.dll"

$RunTestsEnabled = Get-BooleanDefault -Value $RunTests -Default $true
$ValidatePackageEnabled = Get-BooleanDefault -Value $ValidatePackage -Default $true
$SelfContainedEnabled = Get-BooleanDefault -Value $SelfContained -Default $false

try {
    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        Fail "This script must run under Windows PowerShell or PowerShell on Windows. From WSL, invoke it through powershell.exe."
    }

    $requiredPaths = @(
        "OpenCode.Workspace.slnx",
        "catalog",
        "src",
        "tools/OpenCode.Workspace.ReleaseTool"
    )
    foreach ($requiredPath in $requiredPaths) {
        if (-not (Test-Path (Join-Path $RepositoryRoot $requiredPath))) {
            Fail "Repository root '$RepositoryRoot' is missing expected path '$requiredPath'."
        }
    }

    if ($RuntimeIdentifier -ne "win-x64") {
        Fail "Local release packaging currently supports win-x64 only. Use GitHub Actions/native platform builds for Linux and macOS packages."
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    if (-not $dotnetCommand.Source.EndsWith("dotnet.exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail "Expected Windows dotnet.exe, but resolved '$($dotnetCommand.Source)'."
    }

    $sdkList = (& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) {
        Fail "Unable to enumerate installed .NET SDKs."
    }
    if (-not ($sdkList -match '^10\.0\.')) {
        Fail "A .NET 10.0 SDK is required for local Windows release packaging."
    }

    $SelectedVersion = Resolve-Version -ExplicitVersion $Version
    $SelectedVersion = $SelectedVersion -replace '[^0-9A-Za-z._-]', '-'

    $ResolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
        [System.IO.Path]::GetFullPath($OutputRoot)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $OutputRoot))
    }

    if (-not $ResolvedOutputRoot.StartsWith($ArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail "Output root must stay under the repository artifacts directory. Resolved output root: '$ResolvedOutputRoot'."
    }

    $RidRoot = Join-Path $ResolvedOutputRoot $RuntimeIdentifier
    $PublishRoot = Join-Path $RidRoot "publish"
    $PackageOutputRoot = Join-Path $RidRoot "package"
    $ValidationRoot = Join-Path $RidRoot "validation"
    $PackageName = "opencode-workspace-$SelectedVersion-$RuntimeIdentifier"
    $PackageDirectory = Join-Path $PackageOutputRoot $PackageName
    $ArchivePath = Join-Path $ResolvedOutputRoot "$PackageName.zip"
    $ChecksumPath = "$ArchivePath.sha256"

    Write-Host "OpenCode Workspace local release" -ForegroundColor Green
    Write-Host "Version:       $SelectedVersion"
    Write-Host "RID:           $RuntimeIdentifier"
    Write-Host "Configuration: $Configuration"
    Write-Host "Output root:   $ResolvedOutputRoot"
    Write-Host "dotnet:        $($dotnetCommand.Source)"
    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    Write-Host "git:           $($(if ($gitCommand) { $gitCommand.Source } else { 'not required' }))"

    if ($Clean) {
        $script:CurrentStage = "clean"
        Stop-RepoScopedTestHosts -RepositoryRootPath $RepositoryRoot
        if (Test-Path $RidRoot) {
            $resolvedRidRoot = [System.IO.Path]::GetFullPath($RidRoot)
            if (-not $resolvedRidRoot.StartsWith($ArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                Fail "Refusing to clean path outside repository artifacts root: '$resolvedRidRoot'."
            }

            Remove-Item -LiteralPath $resolvedRidRoot -Recurse -Force
        }
    }

    Stop-RepoScopedTestHosts -RepositoryRootPath $RepositoryRoot

    New-Item -ItemType Directory -Force -Path $PublishRoot, $PackageOutputRoot, $ValidationRoot | Out-Null

    if (-not $NoRestore) {
        $script:CurrentStage = "restore"
        Invoke-DotNet -Arguments @("restore", "OpenCode.Workspace.slnx")
    }

    $script:CurrentStage = "build"
    $buildArguments = @("build", "OpenCode.Workspace.slnx", "-c", $Configuration)
    if ($NoRestore) {
        $buildArguments += "--no-restore"
    }
    else {
        $buildArguments += "--no-restore"
    }
    Invoke-DotNet -Arguments $buildArguments

    if ($RunTestsEnabled) {
        $script:CurrentStage = "tests"
        Invoke-DotNet -Arguments @("test", "tests/OpenCode.Workspace.Core.Tests/OpenCode.Workspace.Core.Tests.csproj", "-c", $Configuration, "--no-build", "-m:1")
        Invoke-DotNet -Arguments @("test", "tests/OpenCode.Workspace.Cli.Tests/OpenCode.Workspace.Cli.Tests.csproj", "-c", $Configuration, "--no-build")
        Invoke-DotNet -Arguments @("test", "tests/OpenCode.Workspace.Avalonia.Tests/OpenCode.Workspace.Avalonia.Tests.csproj", "-c", $Configuration, "--no-build")
        Invoke-DotNet -Arguments @("test", "tests/OpenCode.Workspace.Api.IntegrationTests/OpenCode.Workspace.Api.IntegrationTests.csproj", "-c", $Configuration, "--no-build", "--filter", "Category=FastIntegration")
        Invoke-DotNet -Arguments @(
            "test", "tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj", "-c", $Configuration, "--no-build",
            "--filter", "FullyQualifiedName~McpToolAdapterTests.ToolRegistration_ExposesStableToolNames_OverStdio|FullyQualifiedName~McpToolAdapterTests.OperationStore_BoundsRecentEvents_AndReportsTruncation|FullyQualifiedName~McpToolAdapterTests.OperationStore_WritesStructuredProgressLog_AndSanitizesSecrets|FullyQualifiedName~McpProtocolIntegrationTests.ProtocolDiscovery_ExposesStableToolsAndSchemas|FullyQualifiedName~McpProtocolIntegrationTests.ProtocolErrors_AreStable_And_DoNotCrashServer|FullyQualifiedName~McpProtocolIntegrationTests.ProtocolWorkspaceAndTemplateResources_AreReadable"
        )
    }

    $script:CurrentStage = "publish"
    $selfContainedValue = if ($SelfContainedEnabled) { "true" } else { "false" }
    $publishCommon = @(
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", $selfContainedValue,
        "-p:DebugSymbols=false",
        "-p:DebugType=None"
    )

    Invoke-DotNet -Arguments (@("publish", "src/OpenCode.Workspace.Avalonia/OpenCode.Workspace.Avalonia.csproj") + $publishCommon + @("-o", (Join-Path $PublishRoot "desktop")))
    Invoke-DotNet -Arguments (@("publish", "src/OpenCode.Workspace.Cli/OpenCode.Workspace.Cli.csproj") + $publishCommon + @("-o", (Join-Path $PublishRoot "cli")))
    Invoke-DotNet -Arguments (@("publish", "src/OpenCode.Workspace.Api/OpenCode.Workspace.Api.csproj") + $publishCommon + @("-o", (Join-Path $PublishRoot "api")))
    Invoke-DotNet -Arguments (@("publish", "src/OpenCode.Workspace.Mcp/OpenCode.Workspace.Mcp.csproj") + $publishCommon + @("-o", (Join-Path $PublishRoot "mcp")))

    $script:CurrentStage = "assemble"
    if (-not (Test-Path $ReleaseToolDll)) {
        Fail "ReleaseTool build output was not found: '$ReleaseToolDll'."
    }
    Invoke-DotNet -Arguments @(
        $ReleaseToolDll,
        "assemble",
        "--source-root", $RepositoryRoot,
        "--output-root", $PackageOutputRoot,
        "--runtime", $RuntimeIdentifier,
        "--version", $SelectedVersion,
        "--desktop-publish-dir", (Join-Path $PublishRoot "desktop"),
        "--cli-publish-dir", (Join-Path $PublishRoot "cli"),
        "--api-publish-dir", (Join-Path $PublishRoot "api"),
        "--mcp-publish-dir", (Join-Path $PublishRoot "mcp")
    )

    if (-not (Test-Path $PackageDirectory)) {
        Fail "Expected assembled package directory '$PackageDirectory' was not created."
    }

    $packagedExecutables = @(
        (Join-Path $PackageDirectory "bin\desktop\opencode-workspace.exe"),
        (Join-Path $PackageDirectory "bin\cli\opencode-workspace-cli.exe"),
        (Join-Path $PackageDirectory "bin\api\opencode-workspace-api.exe"),
        (Join-Path $PackageDirectory "bin\mcp\opencode-workspace-mcp.exe")
    )
    foreach ($packagedExecutable in $packagedExecutables) {
        if (-not (Test-Path $packagedExecutable)) {
            Fail "Expected packaged executable '$packagedExecutable' was not found after assembly."
        }
    }

    if ($ValidatePackageEnabled) {
        $script:CurrentStage = "package validation"
        $outsideRepoWorkingRoot = Join-Path $ValidationRoot "outside-repo"
        $apiStateRoot = Join-Path $ValidationRoot "api-state"
        $apiArtifactsRoot = Join-Path $ValidationRoot "api-artifacts"
        $packageTestArtifactsRoot = Join-Path $ValidationRoot "package-test-artifacts"
        New-Item -ItemType Directory -Force -Path $outsideRepoWorkingRoot, $apiStateRoot, $apiArtifactsRoot, $packageTestArtifactsRoot | Out-Null

        $cliExecutable = Join-Path $PackageDirectory "bin\cli\opencode-workspace-cli.exe"
        $apiExecutable = Join-Path $PackageDirectory "bin\api\opencode-workspace-api.exe"
        $mcpExecutable = Join-Path $PackageDirectory "bin\mcp\opencode-workspace-mcp.exe"

        foreach ($requiredExecutable in @($cliExecutable, $apiExecutable, $mcpExecutable)) {
            if (-not (Test-Path $requiredExecutable)) {
                Fail "Missing packaged executable '$requiredExecutable'."
            }
        }

        $cliSmoke = Invoke-Native -FilePath $cliExecutable -Arguments @("smoke", "list", "--format", "json") -WorkingDirectory $outsideRepoWorkingRoot -RedirectOutput -TimeoutSeconds 60
        if ($cliSmoke.ExitCode -ne 0) {
            Fail "Packaged CLI smoke list failed.`n$($cliSmoke.StandardOutput)`n$($cliSmoke.StandardError)"
        }
        $null = $cliSmoke.StandardOutput | ConvertFrom-Json
        if (-not ($cliSmoke.StandardOutput -match 'empty-workspace')) {
            Fail "Packaged CLI smoke list did not return the expected templates."
        }

        $cliRuntime = Invoke-Native -FilePath $cliExecutable -Arguments @("runtime", "list", "--format", "json") -WorkingDirectory $outsideRepoWorkingRoot -RedirectOutput -TimeoutSeconds 60
        if ($cliRuntime.ExitCode -ne 0) {
            Fail "Packaged CLI runtime list failed.`n$($cliRuntime.StandardOutput)`n$($cliRuntime.StandardError)"
        }
        $null = $cliRuntime.StandardOutput | ConvertFrom-Json

        $port = Get-FreeTcpPort
        $apiProcess = Start-BackgroundProcess -FilePath $apiExecutable -WorkingDirectory $outsideRepoWorkingRoot -Environment @{
            ASPNETCORE_URLS = "http://127.0.0.1:$port"
            mcp__workspaceStateRoot = $apiStateRoot
            mcp__smokeArtifactsRoot = $apiArtifactsRoot
        }

        try {
            Wait-ForHttpSuccess -Uri "http://127.0.0.1:$port/api/v1/health/live" -TimeoutSeconds 60
            $null = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/v1/health/ready" -TimeoutSec 15
            $templatesResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/v1/templates" -UseBasicParsing -TimeoutSec 15
            if ($templatesResponse.Content -notmatch 'empty-workspace') {
                Fail "Packaged API template listing did not include empty-workspace."
            }
            $smokeDefinitionsResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/v1/smoke/definitions" -UseBasicParsing -TimeoutSec 15
            if ($smokeDefinitionsResponse.Content -notmatch 'empty-workspace') {
                Fail "Packaged API smoke definitions did not include empty-workspace."
            }
        }
        finally {
            try {
                $apiProcess.StandardInput.Close()
            }
            catch {
            }
        }

        if (-not $apiProcess.WaitForExit(30000)) {
            try {
                $apiProcess.Kill($true)
            }
            catch {
            }
            Fail "Packaged API process did not exit after validation."
        }
        $apiProcess.Dispose()

        Invoke-DotNet -Arguments @(
            "test", "tests/OpenCode.Workspace.Mcp.Tests/OpenCode.Workspace.Mcp.Tests.csproj", "-c", $Configuration, "--no-build",
            "--filter", "FullyQualifiedName~PackagedDistributionTests.ExtractedDistribution_ResolvesPackagedContent_AndHostsExitGracefully|FullyQualifiedName~PackagedDistributionTests.PackagedMcp_RunSmoke_ReportsIncrementalProgress_AndShutsDownCleanly"
        ) -Environment @{
            OPENCODE_EXISTING_PACKAGE_ROOT = $PackageDirectory
            OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT = $packageTestArtifactsRoot
        }
    }

    if (-not $NoArchive) {
        $script:CurrentStage = "archive"
        if (Test-Path $ArchivePath) {
            Remove-Item -LiteralPath $ArchivePath -Force
        }
        if (Test-Path $ChecksumPath) {
            Remove-Item -LiteralPath $ChecksumPath -Force
        }

        Compress-Archive -LiteralPath $PackageDirectory -DestinationPath $ArchivePath -Force
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath).Hash.ToLowerInvariant()
        Set-Content -LiteralPath $ChecksumPath -Value ("{0}  {1}" -f $hash, [System.IO.Path]::GetFileName($ArchivePath)) -NoNewline
    }

    Write-Host ""
    Write-Host "OpenCode Workspace local release completed" -ForegroundColor Green
    Write-Host ""
    Write-Host ("Version:       {0}" -f $SelectedVersion)
    Write-Host ("RID:           {0}" -f $RuntimeIdentifier)
    Write-Host ("Configuration: {0}" -f $Configuration)
    Write-Host ("Package:       {0}" -f $PackageDirectory)
    $archiveSummary = if ($NoArchive) { 'skipped' } else { $ArchivePath }
    $checksumSummary = if ($NoArchive) { 'skipped' } else { $ChecksumPath }
    $testsSummary = if ($RunTestsEnabled) { 'passed' } else { 'skipped' }
    $validationSummary = if ($ValidatePackageEnabled) { 'passed' } else { 'skipped' }
    Write-Host ("Archive:       {0}" -f $archiveSummary)
    Write-Host ("Checksum:      {0}" -f $checksumSummary)
    Write-Host ("Tests:         {0}" -f $testsSummary)
    Write-Host ("Validation:    {0}" -f $validationSummary)
    Write-Host ""
    Write-Host "MCP executable:"
    Write-Host (Join-Path $PackageDirectory "bin\mcp\opencode-workspace-mcp.exe")
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
