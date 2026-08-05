param(
    [string]$AppPath = "",
    [string]$WorkingDirectory = "",
    [string]$Language = "en",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

function Resolve-AbsolutePath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Get-DesktopShellProcesses {
    Get-Process opencode-workspace -ErrorAction SilentlyContinue |
        Sort-Object StartTime
}

function Get-VisibleDesktopShellProcess {
    Get-DesktopShellProcesses |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Select-Object -Last 1
}

function Get-BuildConfiguration {
    param([string]$ResolvedAppPath)

    $segments = $ResolvedAppPath -split '[\\/]'
    foreach ($segment in $segments) {
        if ($segment -ieq 'Debug' -or $segment -ieq 'Release') {
            return $segment
        }
    }

    return 'Unknown'
}

function Resolve-DefaultAppPath {
    $debugPath = Resolve-AbsolutePath "src/OpenCode.Workspace.Avalonia/bin/Debug/net10.0/OpenCode.Workspace.exe"
    $releasePath = Resolve-AbsolutePath "src/OpenCode.Workspace.Avalonia/bin/Release/net10.0/OpenCode.Workspace.exe"

    $candidates = @()
    if (Test-Path $debugPath) {
        $candidates += Get-Item $debugPath
    }

    if (Test-Path $releasePath) {
        $candidates += Get-Item $releasePath
    }

    if ($candidates.Count -eq 0) {
        return $debugPath
    }

    return ($candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Resolve-RepositoryRoot {
    $current = Get-Location
    while ($null -ne $current) {
        if (Test-Path (Join-Path $current.Path "OpenCode.Workspace.slnx")) {
            return $current.Path
        }

        $current = $current.Parent
    }

    return $null
}

function Get-GitCommitSha {
    param([string]$RepositoryRoot)

    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        return "unavailable"
    }

    try {
        $sha = git -C $RepositoryRoot rev-parse --short HEAD 2>$null
        if ([string]::IsNullOrWhiteSpace($sha)) {
            return "unavailable"
        }

        return $sha.Trim()
    }
    catch {
        return "unavailable"
    }
}

$resolvedAppPath = if ([string]::IsNullOrWhiteSpace($AppPath)) {
    Resolve-DefaultAppPath
} else {
    Resolve-AbsolutePath $AppPath
}
if (-not (Test-Path $resolvedAppPath)) {
    Write-Warning "App executable not found: $resolvedAppPath"
    exit 1
}

$resolvedWorkingDirectory = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    Split-Path $resolvedAppPath -Parent
} else {
    Resolve-AbsolutePath $WorkingDirectory
}

$appFile = Get-Item $resolvedAppPath
$fileVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedAppPath)
$repositoryRoot = Resolve-RepositoryRoot
$gitCommitSha = Get-GitCommitSha $repositoryRoot
$configuration = Get-BuildConfiguration $resolvedAppPath
Write-Output "Launched: $resolvedAppPath"
Write-Output "Configuration: $configuration"
Write-Output "AssemblyVersion: $($fileVersionInfo.FileVersion)"
Write-Output "InformationalVersion: $($fileVersionInfo.ProductVersion)"
Write-Output "GitCommitSha: $gitCommitSha"
Write-Output "BuildTimestamp: $($appFile.LastWriteTime.ToString('O'))"

$beforeIds = @(Get-DesktopShellProcesses | ForEach-Object { $_.Id })
$env:OPENCODE_WORKSPACE_MANAGER_LANGUAGE = $Language
$started = Start-Process -FilePath $resolvedAppPath -WorkingDirectory $resolvedWorkingDirectory -PassThru

$deadline = (Get-Date).AddSeconds([Math]::Max(1, $TimeoutSeconds))
$selected = $null
do {
    Start-Sleep -Milliseconds 300

    $candidate = Get-DesktopShellProcesses |
        Where-Object { $_.Id -notin $beforeIds -and $_.MainWindowHandle -ne 0 } |
        Select-Object -Last 1

    if ($candidate) {
        $selected = $candidate
        break
    }

    $visibleExisting = Get-VisibleDesktopShellProcess
    if ($visibleExisting) {
        $selected = $visibleExisting
        break
    }
} while ((Get-Date) -lt $deadline)

if (-not $selected) {
    Write-Warning "Desktop shell process started but no visible window appeared within $TimeoutSeconds seconds."
    $started.Refresh()
    $started | Select-Object Id, ProcessName, StartTime | Format-List
    exit 0
}

$selected.Refresh()
$selected | Select-Object Id, ProcessName, StartTime, MainWindowTitle, MainWindowHandle | Format-List
