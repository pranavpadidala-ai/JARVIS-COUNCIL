[CmdletBinding()]
param(
    [string]$ShortcutName = "JARVIS",
    [string]$UpdateShortcutName = "Update JARVIS",
    [switch]$BuildNow
)

# Creates two desktop shortcuts:
#   "JARVIS"        - opens the already-built desktop app directly (starts in seconds).
#   "Update JARVIS" - rebuilds JARVIS in a visible window, shows any errors, then opens it.
# An older "JARVIS Command Center" shortcut that rebuilt JARVIS in a hidden window on every
# launch is re-pointed to the fast app shortcut target. No shortcut or file is deleted.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $PSScriptRoot "Start-JarvisDesktop.ps1"
$desktopExe = Join-Path $repoRoot "src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe"

foreach ($name in @($ShortcutName, $UpdateShortcutName)) {
    if ([string]::IsNullOrWhiteSpace($name) -or $name -match '[\\/:*?"<>|]') {
        throw "Shortcut names must be valid Windows file names without path characters."
    }
}

if (-not (Test-Path $launcherPath)) {
    throw "JARVIS desktop launcher was not found: $launcherPath"
}

if ($BuildNow -or -not (Test-Path $desktopExe)) {
    Write-Host "Building JARVIS before installing the shortcuts..." -ForegroundColor Cyan
    & $launcherPath -BuildOnly
    if (-not (Test-Path $desktopExe)) {
        throw "JARVIS build did not produce $desktopExe; no shortcut was installed."
    }
}

$powerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
$desktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktopFolder) -or -not (Test-Path $desktopFolder)) {
    throw "The current Windows user's Desktop folder could not be located."
}

function Get-ShortcutPath([string]$name) {
    $fileName = if ($name.EndsWith(".lnk", [StringComparison]::OrdinalIgnoreCase)) { $name } else { $name + ".lnk" }
    return Join-Path $desktopFolder $fileName
}

$shell = New-Object -ComObject WScript.Shell

function Set-AppShortcut([string]$path) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $desktopExe
    $shortcut.Arguments = ""
    $shortcut.WorkingDirectory = $repoRoot
    $shortcut.Description = "Open JARVIS"
    $shortcut.IconLocation = "$desktopExe,0"
    $shortcut.WindowStyle = 1
    $shortcut.Save()
}

# 1. Fast shortcut: opens the built app directly, no build and no PowerShell window.
$appShortcutPath = Get-ShortcutPath $ShortcutName
Set-AppShortcut $appShortcutPath

# 2. Update shortcut: visible window, rebuilds, keeps the window open if anything fails.
$updateShortcutPath = Get-ShortcutPath $UpdateShortcutName
$update = $shell.CreateShortcut($updateShortcutPath)
$update.TargetPath = $powerShell
$update.Arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $launcherPath + '" -PauseOnError'
$update.WorkingDirectory = $repoRoot
$update.Description = "Rebuild JARVIS after an update, then open it"
$update.IconLocation = "$desktopExe,0"
$update.WindowStyle = 1
$update.Save()

# 3. Re-point the older hidden-rebuild shortcut, if present, to the fast app target.
$legacyPath = Get-ShortcutPath "JARVIS Command Center"
if ((Test-Path $legacyPath) -and ($legacyPath -ne $appShortcutPath)) {
    $legacy = $shell.CreateShortcut($legacyPath)
    if ($legacy.Arguments -match 'Start-JarvisDesktop\.ps1') {
        Set-AppShortcut $legacyPath
        Write-Host "Updated the older 'JARVIS Command Center' shortcut to open JARVIS directly." -ForegroundColor Yellow
    }
}

foreach ($path in @($appShortcutPath, $updateShortcutPath)) {
    if (-not (Test-Path $path)) { throw "Windows did not create the shortcut at: $path" }
}

Write-Host ""
Write-Host "JARVIS desktop shortcuts installed:" -ForegroundColor Green
Write-Host "  $appShortcutPath  (opens JARVIS in a few seconds)"
Write-Host "  $updateShortcutPath  (rebuilds after an update, shows any errors)"
