<#
.SYNOPSIS
    Installs FileDiff as a real Windows application: copies the published build into a
    per-user programs folder and creates Start Menu (and optional Desktop) shortcuts.

.DESCRIPTION
    No administrator rights are needed and nothing is written outside your user profile.
    A matching "Uninstall FileDiff.ps1" is placed in the install folder.

.EXAMPLE
    .\Install-FileDiff.ps1
    Installs the standalone build (works on any Windows 10/11 machine, ~134 MB).

.EXAMPLE
    .\Install-FileDiff.ps1 -Variant Light
    Installs the small build (0.5 MB) — requires the .NET 10 Desktop Runtime on that machine.

.EXAMPLE
    .\Install-FileDiff.ps1 -NoDesktop
    Installs without creating a Desktop shortcut.
#>
[CmdletBinding()]
param(
    [ValidateSet('Standalone', 'Light')]
    [string]$Variant = 'Standalone',

    [switch]$NoDesktop
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $repoRoot "publish\FileDiff$(if ($Variant -eq 'Light') { '' } else { '-standalone' })"
$exeName = 'FileDiff.App.exe'

if (-not (Test-Path (Join-Path $sourceDir $exeName))) {
    throw "No published build found in '$sourceDir'. Run the publish step first (see README)."
}

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FileDiff'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'

Write-Host "Installing FileDiff ($Variant build)..." -ForegroundColor Cyan
Write-Host "  from: $sourceDir"
Write-Host "  to:   $installDir"

if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $sourceDir '*') $installDir -Recurse -Force

$installedExe = Join-Path $installDir $exeName
$iconPath = Join-Path $installDir 'FileDiff.ico'
$icon = if (Test-Path $iconPath) { $iconPath } else { $installedExe }

# ---- Start Menu + Desktop shortcuts -------------------------------------------
$shell = New-Object -ComObject WScript.Shell

function New-FileDiffShortcut([string]$path) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $installedExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = $icon
    $shortcut.Description = 'Remember a folder, then see exactly what changed.'
    $shortcut.Save()
    Write-Host "  shortcut: $path"
}

New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
New-FileDiffShortcut (Join-Path $startMenuDir 'FileDiff.lnk')

if (-not $NoDesktop) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if ($desktop) { New-FileDiffShortcut (Join-Path $desktop 'FileDiff.lnk') }
}

# ---- uninstaller ---------------------------------------------------------------
$uninstaller = @'
# Removes FileDiff. Start Menu / Desktop shortcuts and the installed files.
# Your snapshots live in %APPDATA%\FileDiff and are NOT deleted by this script.
$ErrorActionPreference = 'SilentlyContinue'

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FileDiff'
$startMenu  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\FileDiff.lnk'
$desktop    = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FileDiff.lnk'

foreach ($file in @($startMenu, $desktop)) {
    if (Test-Path $file) { Remove-Item $file -Force; "removed shortcut: $file" }
}

Get-Process -Name 'FileDiff.App' | ForEach-Object { $_.CloseMainWindow() | Out-Null }
Start-Sleep -Milliseconds 800
Get-Process -Name 'FileDiff.App' | Stop-Process -Force

if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force; "removed: $installDir" }
'FileDiff uninstalled. Your snapshot history in %APPDATA%\FileDiff was left untouched.'
'@

Set-Content -Path (Join-Path $installDir 'Uninstall FileDiff.ps1') -Value $uninstaller -Encoding UTF8

$sizeMb = [math]::Round((Get-ChildItem $installDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)

Write-Host ''
Write-Host "FileDiff installed ($sizeMb MB)." -ForegroundColor Green
Write-Host "Launch it from the Start Menu, or run:" -ForegroundColor Green
Write-Host "  $installedExe"
Write-Host 'To uninstall: run "Uninstall FileDiff.ps1" inside the install folder.' -ForegroundColor DarkGray
