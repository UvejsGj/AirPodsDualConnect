<#
.SYNOPSIS
    Creates two desktop shortcuts with keyboard shortcuts for one-key switching:
      "AirPods to laptop"  Ctrl+Alt+L  runs DualConnectW.exe take
      "AirPods to iPhone"  Ctrl+Alt+P  runs DualConnectW.exe give
    Run with -Remove to delete them again. Creates or deletes two .lnk files on your
    desktop and nothing else: no registry edit, no install, no admin rights.

    UNTESTED: written on 8 Oct 2026 without a Windows machine.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Shortcuts.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Shortcuts.ps1 -Remove
#>
param(
    [switch]$Remove,
    [string]$Name = 'AirPods',
    [string]$LaptopKey = 'CTRL+ALT+L',
    [string]$PhoneKey = 'CTRL+ALT+P'
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$desktop = [Environment]::GetFolderPath('Desktop')
$links = @(
    @{ File = 'AirPods to laptop.lnk'; Verb = 'take'; Key = $LaptopKey; Text = 'Move the AirPods sound to this laptop' },
    @{ File = 'AirPods to iPhone.lnk'; Verb = 'give'; Key = $PhoneKey;  Text = 'Disconnect the AirPods from this laptop so the iPhone can take them' }
)

if ($Remove) {
    foreach ($l in $links) {
        $path = Join-Path $desktop $l.File
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path; Write-Host "Removed $path" }
    }
    return
}

$exe = Join-Path $here 'DualConnectW.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "DualConnectW.exe is missing. Run Build.cmd in this folder first." -ForegroundColor Yellow
    exit 1
}

$shell = New-Object -ComObject WScript.Shell
foreach ($l in $links) {
    $path = Join-Path $desktop $l.File
    $s = $shell.CreateShortcut($path)
    $s.TargetPath = $exe
    $s.Arguments = '{0} --name "{1}"' -f $l.Verb, $Name
    $s.WorkingDirectory = $here
    $s.Hotkey = $l.Key
    $s.Description = $l.Text
    $s.WindowStyle = 7
    $s.Save()
    Write-Host ("Created {0}  ({1})" -f $path, $l.Key)
}
Write-Host "Press the keys once to test. Each press adds a row to actions.csv in $here."
