<#
.SYNOPSIS
  Connects JARVIS to your Google Drive (one time). A browser opens: sign in to Google yourself and click Allow.
  JARVIS never sees your password; rclone stores only an access token in your user profile.
.NOTES
  Needs rclone.exe in E:\JARVIS-tools\rclone (or set JARVIS_RCLONE to its path).
#>
$ErrorActionPreference = 'Stop'
$rclone = if ($env:JARVIS_RCLONE) { $env:JARVIS_RCLONE } else { Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'JARVIS-tools\rclone\rclone.exe' }
if (-not (Test-Path $rclone)) { throw "rclone was not found at $rclone" }

$existing = & $rclone listremotes 2>$null
if ($existing -contains 'gdrive:') {
    Write-Host "Google Drive is already connected. Testing it..." -ForegroundColor Cyan
} else {
    Write-Host "A browser window will open. Sign in to Google and click Allow." -ForegroundColor Cyan
    & $rclone config create gdrive drive scope=drive
    if ($LASTEXITCODE -ne 0) { throw "Google Drive was not connected." }
}
& $rclone about gdrive: 2>&1 | Select-Object -First 4
Write-Host "Done. Ask JARVIS things like 'list my Drive' or 'upload my thesis to Drive'." -ForegroundColor Green
