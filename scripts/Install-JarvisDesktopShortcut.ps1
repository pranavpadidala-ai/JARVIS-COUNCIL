[CmdletBinding()]
param(
    [string]$ShortcutName = "JARVIS Command Center",
    [switch]$BuildNow
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $PSScriptRoot "Start-JarvisDesktop.ps1"
$desktopExe = Join-Path $repoRoot "src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe"

if ([string]::IsNullOrWhiteSpace($ShortcutName) -or $ShortcutName -match '[\\/:*?"<>|]') {
    throw "ShortcutName must be a valid Windows file name without path characters."
}

if (-not (Test-Path $launcherPath)) {
    throw "JARVIS desktop launcher was not found: $launcherPath"
}

$powerShell = Get-Command powershell.exe -ErrorAction Stop
$desktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktopFolder) -or -not (Test-Path $desktopFolder)) {
    throw "The current Windows user's Desktop folder could not be located."
}

if ($BuildNow) {
    Write-Host "Building JARVIS before installing the shortcut..." -ForegroundColor Cyan
    & $launcherPath -BuildOnly
    if ($LASTEXITCODE -ne 0) {
        throw "JARVIS build failed; no shortcut was installed."
    }
}

$shortcutFileName = if ($ShortcutName.EndsWith(".lnk", [StringComparison]::OrdinalIgnoreCase)) {
    $ShortcutName
}
else {
    $ShortcutName + ".lnk"
}
$shortcutPath = Join-Path $desktopFolder $shortcutFileName

$windowShell = New-Object -ComObject WScript.Shell
$shortcut = $windowShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $powerShell.Source
$shortcut.Arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcherPath + '"'
$shortcut.WorkingDirectory = $repoRoot
$shortcut.Description = "Build and launch the JARVIS Command Center using the local-first provider configuration."
$shortcut.WindowStyle = 7
if (Test-Path $desktopExe) {
    $shortcut.IconLocation = "$desktopExe,0"
}
$shortcut.Save()

if (-not (Test-Path $shortcutPath)) {
    throw "Windows did not create the shortcut at: $shortcutPath"
}

Write-Host ""
Write-Host "JARVIS desktop shortcut installed:" -ForegroundColor Green
Write-Host $shortcutPath
Write-Host "Double-click it to build incrementally and open JARVIS without leaving a PowerShell window visible."
