[CmdletBinding()]
param(
    [switch]$FullLocalStack,
    [switch]$SkipModelDownload,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "The JARVIS desktop installer currently requires Windows 10 or 11."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopLauncher = Join-Path $PSScriptRoot "Start-JarvisDesktop.ps1"
$coreModel = "qwen3.5:2b-q4_K_M"

function Refresh-ProcessPath {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = @($machinePath, $userPath) -join ";"
}

function Set-UserSetting([string]$Name, [string]$Value) {
    [Environment]::SetEnvironmentVariable($Name, $Value, "User")
    Set-Item -Path ("Env:" + $Name) -Value $Value
}

function Find-OllamaExecutable {
    $command = Get-Command ollama -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"),
        (Join-Path $env:ProgramFiles "Ollama\ollama.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Test-OllamaServer {
    try {
        $null = Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/tags" -TimeoutSec 3
        return $true
    }
    catch {
        return $false
    }
}

Write-Host "JARVIS first-use setup" -ForegroundColor Cyan
Write-Host "This installs a local-first 2B chat model. The optional vision/OCR/voice stack is downloaded only when -FullLocalStack is supplied."
Write-Host ""

# .NET 8 SDK
Refresh-ProcessPath
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles "dotnet\dotnet.exe" }
if (-not (Test-Path $dotnetPath)) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw ".NET 8 SDK is missing and winget is unavailable. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0, reopen PowerShell, and rerun this script."
    }
    Write-Host "Installing .NET 8 SDK..." -ForegroundColor Cyan
    & $winget.Source install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw "The .NET 8 SDK installation failed. Install it manually and rerun this script." }
    Refresh-ProcessPath
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles "dotnet\dotnet.exe" }
}
if (-not (Test-Path $dotnetPath)) {
    throw ".NET 8 was installed but dotnet.exe is not available in this session. Close and reopen PowerShell, then rerun this script."
}
$sdkVersion = (& $dotnetPath --version).Trim()
if (-not $sdkVersion.StartsWith("8.")) {
    Write-Warning "Detected .NET SDK $sdkVersion. JARVIS targets .NET 8; install the .NET 8 SDK if the build fails."
}

# Ollama local inference runtime
Refresh-ProcessPath
$ollamaPath = Find-OllamaExecutable
if (-not $ollamaPath) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "Ollama is missing and winget is unavailable. Install Ollama from https://ollama.com/download, reopen PowerShell, and rerun this script."
    }
    Write-Host "Installing Ollama..." -ForegroundColor Cyan
    & $winget.Source install --id Ollama.Ollama -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw "Ollama installation failed. Install it from https://ollama.com/download and rerun this script." }
    Refresh-ProcessPath
    $ollamaPath = Find-OllamaExecutable
}
if (-not $ollamaPath) {
    throw "Ollama was installed but its executable is not available yet. Close and reopen PowerShell, then rerun this script."
}

# Start the local Ollama API only if it is not already available.
if (-not (Test-OllamaServer)) {
    Write-Host "Starting the local Ollama service..." -ForegroundColor Cyan
    Start-Process -FilePath $ollamaPath -ArgumentList "serve" -WindowStyle Hidden | Out-Null
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 2
        if (Test-OllamaServer) {
            $ready = $true
            break
        }
    }
    if (-not $ready) {
        throw "Ollama did not start on http://127.0.0.1:11434. Open the Ollama app, check its status, and rerun this script."
    }
}

if ($FullLocalStack) {
    Write-Host "Installing the optional local vision, engineering OCR, speech recognition and speech synthesis models..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "Install-LocalModels.ps1")
    if ($LASTEXITCODE -ne 0) { throw "The optional local model stack did not finish successfully." }
}
else {
    $tags = Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/tags" -TimeoutSec 10
    $installedModels = @($tags.models | ForEach-Object { $_.name })
    if ($installedModels -notcontains $coreModel) {
        if ($SkipModelDownload) {
            throw "The local brain model '$coreModel' is not installed. Rerun without -SkipModelDownload to download it."
        }
        Write-Host "Downloading the local JARVIS brain model ($coreModel)..." -ForegroundColor Cyan
        & $ollamaPath pull $coreModel
        if ($LASTEXITCODE -ne 0) { throw "Could not download the local brain model '$coreModel'." }
    }
}

Set-UserSetting "JARVIS_AI_PROVIDER" "ollama"
Set-UserSetting "OLLAMA_MODEL" $coreModel
Set-UserSetting "JARVIS_MEMORY_ENABLED" "true"
Set-UserSetting "JARVIS_OLLAMA_CONTEXT_TOKENS" "4096"
Set-UserSetting "OLLAMA_MAX_LOADED_MODELS" "1"
Set-UserSetting "OLLAMA_NUM_PARALLEL" "1"
Set-UserSetting "OLLAMA_FLASH_ATTENTION" "1"
Set-UserSetting "OLLAMA_KV_CACHE_TYPE" "q8_0"
Set-UserSetting "OLLAMA_KEEP_ALIVE" "30s"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Warning "Git was not found. Chat, memory and local tools can work, but autonomous development tasks require Git."
}

Write-Host ""
Write-Host "JARVIS local-first setup is ready." -ForegroundColor Green
Write-Host "Provider : Ollama"
Write-Host "Brain    : $coreModel"
Write-Host "Memory   : enabled (local encrypted desktop memory)"
Write-Host "Optional : run this script with -FullLocalStack to install vision, GLM-OCR, Whisper and Piper."
Write-Host ""

if (-not $NoLaunch) {
    Write-Host "Building and launching JARVIS Command Center..." -ForegroundColor Cyan
    & $desktopLauncher
    if ($LASTEXITCODE -ne 0) { throw "JARVIS desktop launcher reported an error." }
}
else {
    Write-Host "Setup completed without launching. Run .\scripts\Start-JarvisDesktop.ps1 when ready."
}
