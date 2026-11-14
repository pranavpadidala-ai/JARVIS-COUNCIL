[CmdletBinding()]
param(
    [switch]$StartNow,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "JARVIS desktop automatic startup currently requires Windows."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $PSScriptRoot "Start-JarvisDesktop.ps1"
$startupFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupFolder "JARVIS Command Center.lnk"
$powerShell = (Get-Command powershell.exe -ErrorAction Stop).Source

if (-not (Test-Path $launcher -PathType Leaf)) {
    throw "JARVIS desktop launcher was not found: $launcher"
}
if ([string]::IsNullOrWhiteSpace($startupFolder) -or -not (Test-Path $startupFolder -PathType Container)) {
    throw "The current Windows user's Startup folder could not be located."
}

if ($Uninstall) {
    if (Test-Path $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Host "Removed the JARVIS desktop automatic-start shortcut." -ForegroundColor Green
    }
    else {
        Write-Host "JARVIS desktop automatic startup is not configured."
    }
    return
}

# Build before registering a no-build logon shortcut. This avoids a slow
# NuGet restore/build at every Windows sign-in.
Write-Host "Validating and building the JARVIS desktop app before registering startup..." -ForegroundColor Cyan
& $launcher -BuildOnly
if ($LASTEXITCODE -ne 0) {
    throw "The desktop build failed. No startup shortcut was created."
}

$arguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' +
    $launcher + '" -NoBuild -StartMinimized'
$workingDirectory = $repoRoot

$wscript = New-Object -ComObject WScript.Shell
$shortcut = $wscript.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $powerShell
$shortcut.Arguments = $arguments
$shortcut.WorkingDirectory = $workingDirectory
$shortcut.Description = "Starts the JARVIS Command Center quietly in the Windows system tray."
$shortcut.IconLocation = (Join-Path $env:SystemRoot "System32\shell32.dll") + ",44"
$shortcut.WindowStyle = 7
$shortcut.Save()

Write-Host ""
Write-Host "JARVIS desktop automatic startup is configured for the current Windows user." -ForegroundColor Green
Write-Host "At sign-in, the Command Center starts hidden in the system tray."
Write-Host "The microphone and wake-word listener remain OFF; the background development agent is NOT started."
Write-Host "Shortcut: $shortcutPath"
Write-Host "To remove it, run .\scripts\Install-JarvisDesktopStartup.ps1 -Uninstall"

if ($StartNow) {
    Write-Host "Starting JARVIS now in the system tray..." -ForegroundColor Cyan
    $startNowArguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' +
        $launcher + '" -NoBuild -StartMinimized'
    Start-Process -FilePath $powerShell -ArgumentList $startNowArguments -WorkingDirectory $workingDirectory
}
