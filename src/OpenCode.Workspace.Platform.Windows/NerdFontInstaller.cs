using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Platform.Windows;

/// <summary>
/// Installs a supported set of Nerd Fonts from the official Nerd Fonts GitHub
/// release downloads into the current user's font scope.
/// </summary>
public sealed class NerdFontInstaller
{
    private readonly ProcessRunner _processRunner;

    public NerdFontInstaller(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task InstallAsync(string family, CancellationToken cancellationToken = default)
    {
        var definition = NerdFontCatalog.FindByDisplayName(family);
        if (definition is null)
        {
            throw new InvalidOperationException($"The selected font '{family}' is not supported by the built-in installer.");
        }

        var url = $"https://github.com/ryanoasis/nerd-fonts/releases/latest/download/{definition.ArchiveName}.zip";
        var script = string.Join("; ",
        [
            "$ErrorActionPreference='Stop'",
            "$fontRoot=Join-Path $env:LOCALAPPDATA 'Microsoft\\Windows\\Fonts'",
            "$fontRegistry='HKCU:\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts'",
            "$tempRoot=Join-Path $env:TEMP 'OpenCodeWorkspaceManagerFonts'",
            "$zipPath=Join-Path $tempRoot 'font.zip'",
            "$extractPath=Join-Path $tempRoot 'extracted'",
            "New-Item -ItemType Directory -Force -Path $fontRoot | Out-Null",
            "New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null",
            "New-Item -Path $fontRegistry -Force | Out-Null",
            "if (Test-Path $extractPath) { Remove-Item -Recurse -Force $extractPath }",
            $"Invoke-WebRequest -Uri '{url}' -OutFile $zipPath",
            "Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force",
            "$fontFiles = Get-ChildItem -Path $extractPath -Recurse -Include *.ttf,*.otf",
            "Add-Type -AssemblyName System.Drawing",
            "$pfc = New-Object System.Drawing.Text.PrivateFontCollection",
            "foreach ($fontFile in $fontFiles) {",
            "  $destination = Join-Path $fontRoot $fontFile.Name",
            "  Copy-Item $fontFile.FullName -Destination $destination -Force",
            "  $pfc.AddFontFile($destination)",
            "  $familyName = $pfc.Families[$pfc.Families.Length - 1].Name",
            "  $registryName = \"$familyName (TrueType)\"",
            "  Set-ItemProperty -Path $fontRegistry -Name $registryName -Value $destination",
            "}",
            "$signature = '[DllImport(\"user32.dll\", SetLastError=true, CharSet=CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);'",
            "Add-Type -MemberDefinition $signature -Name FontBroadcast -Namespace OpenCodeStuff -PassThru | Out-Null",
            "$result = [uintptr]::Zero",
            "[OpenCodeStuff.FontBroadcast]::SendMessageTimeout([intptr]0xffff, 0x001D, [uintptr]::Zero, $null, 0x0002, 1000, [ref]$result) | Out-Null",
            "Remove-Item -Recurse -Force $extractPath",
            "Remove-Item -Force $zipPath"
        ]);

        var result = await _processRunner.RunAsync("powershell.exe", ["-NoProfile", "-Command", script], cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Nerd Font installation failed.{Environment.NewLine}{result.StandardError}".Trim());
        }
    }

    public string? GetArchiveName(string family)
    {
        return NerdFontCatalog.FindByDisplayName(family)?.ArchiveName;
    }
}
