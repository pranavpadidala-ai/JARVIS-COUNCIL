<#
.SYNOPSIS
  Connects JARVIS to your Google Drive (one time). A browser opens: sign in to Google yourself and click Allow.
  JARVIS never sees your password; rclone stores only an access token in your user profile.
.NOTES
  Needs rclone.exe in E:\JARVIS-tools\rclone (or set JARVIS_RCLONE to its path).
  rclone is run through cmd.exe because Windows PowerShell 5.1 turns its harmless stderr notices
  (such as "config file not found") into errors.
#>
$rclone = if ($env:JARVIS_RCLONE) { $env:JARVIS_RCLONE } else { Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'JARVIS-tools\rclone\rclone.exe' }
if (-not (Test-Path $rclone)) {
    Write-Host "rclone was not found at $rclone" -ForegroundColor Red
    exit 1
}

$existing = cmd /c "`"$rclone`" listremotes 2>nul"
if ($existing -contains 'gdrive:') {
    Write-Host "Google Drive is already connected. Testing it..." -ForegroundColor Cyan
} else {
    Write-Host "A browser window will open. Sign in to Google and click Allow." -ForegroundColor Cyan
    cmd /c "`"$rclone`" config create gdrive drive scope=drive"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Google Drive was not connected." -ForegroundColor Red
        exit 1
    }
}
cmd /c "`"$rclone`" about gdrive: 2>nul"
if ($LASTEXITCODE -eq 0) {
    Write-Host "Done. Ask JARVIS things like 'list my Drive' or 'upload my thesis to Drive'." -ForegroundColor Green
} else {
    Write-Host "Drive is set up but did not answer. Check the internet connection and run this again." -ForegroundColor Yellow
}
