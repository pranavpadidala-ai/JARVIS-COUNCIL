$ErrorActionPreference = "Stop"

$repoZip = "https://github.com/pranavpadidala-ai/JARVIS-COUNCIL/archive/refs/heads/feature/powershell-install-desktop-shortcut.zip"
$installRoot = Join-Path $HOME "JARVIS"
$work = Join-Path $env:TEMP ("JARVIS-Install-" + [guid]::NewGuid().ToString("N"))
$zip = Join-Path $work "jarvis.zip"

try {
    if (Test-Path $installRoot) {
        throw "The folder '$installRoot' already exists. Rename or move it first so this installer does not overwrite your files."
    }

    New-Item -ItemType Directory -Path $work -Force | Out-Null
    Write-Host "Downloading JARVIS..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $repoZip -OutFile $zip -UseBasicParsing

    Write-Host "Extracting JARVIS to $installRoot..." -ForegroundColor Cyan
    Expand-Archive -LiteralPath $zip -DestinationPath $work -Force
    $source = Get-ChildItem -LiteralPath $work -Directory | Where-Object { $_.Name -like "JARVIS-COUNCIL-*" } | Select-Object -First 1
    if (-not $source) { throw "The downloaded JARVIS archive did not contain the expected project folder." }
    Move-Item -LiteralPath $source.FullName -Destination $installRoot

    Write-Host "Running first-time setup, build, and tests..." -ForegroundColor Cyan
    $setup = Join-Path $installRoot "Setup-JARVIS.ps1"
    $windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path $windowsPowerShell)) { throw "Windows PowerShell could not be located." }
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $setup
    if ($LASTEXITCODE -ne 0) {
        throw "JARVIS setup did not complete successfully. Open '$installRoot\jarvis-setup.log' for details."
    }

    Write-Host ""
    Write-Host "Installation finished. Use the JARVIS icon on your Desktop to start it next time." -ForegroundColor Green
}
catch {
    Write-Host "JARVIS INSTALLATION FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}