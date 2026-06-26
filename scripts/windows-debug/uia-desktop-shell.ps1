param(
    [ValidateSet("dump", "click", "set-text", "set-edit", "read-edit", "select-window")]
    [string]$Action,
    [string]$WindowTitle = "opencode stuff",
    [string]$ControlName = "",
    [string]$TextValue = "",
    [int]$EditIndex = -1,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NativeDesktopShellUi
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
}
"@

function Get-WindowElement {
    param([string]$Title, [int]$Timeout)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Title)

    $deadline = (Get-Date).AddSeconds([Math]::Max(1, $Timeout))
    do {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)

        foreach ($window in $windows) {
            if ($window.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
                return $window
            }
        }

        if ($windows.Count -gt 0) {
            return $windows[0]
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Window '$Title' was not found."
}

function Get-ControlByName {
    param([System.Windows.Automation.AutomationElement]$Root, [string]$Name)

    $automationIdCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $Name)

    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    $matches = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $automationIdCondition)
    if ($matches.Count -eq 0) {
        $matches = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    }

    if ($matches.Count -eq 0) {
        throw "Control '$Name' was not found under '$($Root.Current.Name)'."
    }

    $preferred = $matches | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button } | Select-Object -First 1
    if ($null -ne $preferred) {
        return $preferred
    }

    return $matches[0]
}

function Invoke-ControlClick {
    param([System.Windows.Automation.AutomationElement]$Control)

    try {
        $pattern = $Control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        return
    }
    catch {
        try {
            $point = $Control.GetClickablePoint()
            $x = [int]$point.X
            $y = [int]$point.Y
        }
        catch {
            $rect = $Control.Current.BoundingRectangle
            if ($rect.Width -le 0 -or $rect.Height -le 0) {
                throw
            }

            $x = [int]($rect.Left + ($rect.Width / 2))
            $y = [int]($rect.Top + ($rect.Height / 2))
        }
    }

    [NativeDesktopShellUi]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [NativeDesktopShellUi]::mouse_event([NativeDesktopShellUi]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    [NativeDesktopShellUi]::mouse_event([NativeDesktopShellUi]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
}

function Set-ControlText {
    param([System.Windows.Automation.AutomationElement]$Control, [string]$Value)

    $pattern = $Control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
}

function Get-EditByIndex {
    param([System.Windows.Automation.AutomationElement]$Root, [int]$Index)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)

    $matches = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($Index -lt 0 -or $Index -ge $matches.Count) {
        throw "Edit index $Index is out of range. Found $($matches.Count) edit controls under '$($Root.Current.Name)'."
    }

    return $matches[$Index]
}

switch ($Action) {
    "dump" {
        $window = Get-WindowElement -Title $WindowTitle -Timeout $TimeoutSeconds
        $items = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output ("WINDOW=" + $window.Current.Name)
        foreach ($item in $items) {
            $name = $item.Current.Name
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            Write-Output ($item.Current.ControlType.ProgrammaticName + " | " + $name)
        }
    }
    "click" {
        $window = Get-WindowElement -Title $WindowTitle -Timeout $TimeoutSeconds
        $control = Get-ControlByName -Root $window -Name $ControlName
        Invoke-ControlClick -Control $control
    }
    "set-text" {
        $window = Get-WindowElement -Title $WindowTitle -Timeout $TimeoutSeconds
        $control = Get-ControlByName -Root $window -Name $ControlName
        Set-ControlText -Control $control -Value $TextValue
    }
    "set-edit" {
        $window = Get-WindowElement -Title $WindowTitle -Timeout $TimeoutSeconds
        $control = Get-EditByIndex -Root $window -Index $EditIndex
        Set-ControlText -Control $control -Value $TextValue
    }
    "read-edit" {
        $window = Get-WindowElement -Title $WindowTitle -Timeout $TimeoutSeconds
        $control = Get-EditByIndex -Root $window -Index $EditIndex
        $pattern = $control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        Write-Output ($pattern.Current.Value)
    }
    "select-window" {
        $window = Get-WindowElement -Title $WindowTitle -Timeout $TimeoutSeconds
        Write-Output ("WINDOW=" + $window.Current.Name)
    }
    default {
        throw "Unsupported action '$Action'."
    }
}
