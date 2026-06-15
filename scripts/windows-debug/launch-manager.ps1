param(
    [string]$AppPath = "src/OpenCode.Workspace.Manager/bin/Release/net10.0-windows/OpenCode.Workspace.Manager.exe",
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

function Get-ManagerProcesses {
    Get-Process OpenCode.Workspace.Manager -ErrorAction SilentlyContinue |
        Sort-Object StartTime
}

function Get-VisibleManagerProcess {
    Get-ManagerProcesses |
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

$resolvedAppPath = Resolve-AbsolutePath $AppPath
if (-not (Test-Path $resolvedAppPath)) {
    Write-Warning "App executable not found: $resolvedAppPath"
    exit 1
}

$resolvedWorkingDirectory = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    Split-Path $resolvedAppPath -Parent
} else {
    Resolve-AbsolutePath $WorkingDirectory
}

$configuration = Get-BuildConfiguration $resolvedAppPath
Write-Output "Launched: $resolvedAppPath"
Write-Output "Configuration: $configuration"

$beforeIds = @(Get-ManagerProcesses | ForEach-Object { $_.Id })
$env:OPENCODE_WORKSPACE_MANAGER_LANGUAGE = $Language
$started = Start-Process -FilePath $resolvedAppPath -WorkingDirectory $resolvedWorkingDirectory -PassThru

$deadline = (Get-Date).AddSeconds([Math]::Max(1, $TimeoutSeconds))
$selected = $null
do {
    Start-Sleep -Milliseconds 300

    $candidate = Get-ManagerProcesses |
        Where-Object { $_.Id -notin $beforeIds -and $_.MainWindowHandle -ne 0 } |
        Select-Object -Last 1

    if ($candidate) {
        $selected = $candidate
        break
    }

    $visibleExisting = Get-VisibleManagerProcess
    if ($visibleExisting) {
        $selected = $visibleExisting
        break
    }
} while ((Get-Date) -lt $deadline)

if (-not $selected) {
    Write-Warning "Manager process started but no visible window appeared within $TimeoutSeconds seconds."
    $started.Refresh()
    $started | Select-Object Id, ProcessName, StartTime | Format-List
    exit 0
}

$selected.Refresh()
$selected | Select-Object Id, ProcessName, StartTime, MainWindowTitle, MainWindowHandle | Format-List
