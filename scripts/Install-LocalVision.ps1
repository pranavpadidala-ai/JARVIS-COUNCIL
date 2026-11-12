# Install Ollama (if needed) and pull JARVIS's default local vision model.
# Run from the repository root in PowerShell:
#   Set-ExecutionPolicy -Scope Process Bypass
#   .\scripts\Install-LocalVision.ps1

$ErrorActionPreference = "Stop"
$model = if ($env:JARVIS_VISION_MODEL) { $env:JARVIS_VISION_MODEL } else { "moondream" }
$ollamaCommand = Get-Command ollama -ErrorAction SilentlyContinue

if (-not $ollamaCommand) {
    $candidatePaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"),
        (Join-Path $env:ProgramFiles "Ollama\ollama.exe")
    )
    $installedPath = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($installedPath) {
        $ollamaPath = $installedPath
    }
    else {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw "Ollama is not installed and winget is unavailable. Install Ollama for Windows from https://ollama.com/download, reopen PowerShell, then rerun this script."
        }
        Write-Host "Installing Ollama for Windows..."
        winget install --id Ollama.Ollama -e --accept-package-agreements --accept-source-agreements
        $candidatePaths = @(
            (Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"),
            (Join-Path $env:ProgramFiles "Ollama\ollama.exe")
        )
        $ollamaPath = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $ollamaPath) {
            $ollamaCommand = Get-Command ollama -ErrorAction SilentlyContinue
            if (-not $ollamaCommand) {
                throw "Ollama was installed. Close and reopen PowerShell, then rerun this script."
            }
            $ollamaPath = $ollamaCommand.Source
        }
    }
}
else {
    $ollamaPath = $ollamaCommand.Source
}

Write-Host "Ollama executable: $ollamaPath"
$serverReady = $false
try {
    Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 3 | Out-Null
    $serverReady = $true
}
catch {
    Write-Host "Starting the local Ollama service..."
    Start-Process -FilePath $ollamaPath -ArgumentList "serve" -WindowStyle Hidden
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 2
        try {
            Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 3 | Out-Null
            $serverReady = $true
            break
        }
        catch { }
    }
}

if (-not $serverReady) {
    throw "Ollama did not become available at http://localhost:11434. Start Ollama from the Start menu and rerun this script."
}

Write-Host "Pulling local vision model '$model' (the default Moondream 2 download is about 1.7 GB)..."
& $ollamaPath pull $model
if ($LASTEXITCODE -ne 0) {
    throw "Ollama could not pull model '$model'. Check your internet connection and available disk space."
}

Write-Host ""
Write-Host "Local vision model installed."
Write-Host "Model: $model"
Write-Host "Endpoint: http://localhost:11434"
Write-Host "JARVIS will use it for embedded PDF images by default. Image analysis stays local."
Write-Host "To disable it, set JARVIS_LOCAL_VISION_ENABLED=false."
