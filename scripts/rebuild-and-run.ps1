param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Language = "en",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$delegate = Join-Path $scriptRoot "windows-debug\rebuild-and-launch-manager.ps1"

& $delegate -Configuration $Configuration -Language $Language -TimeoutSeconds $TimeoutSeconds
