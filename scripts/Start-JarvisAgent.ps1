[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Council.App\Council.App.csproj"

if ($env:OS -ne "Windows_NT") {
    throw "The JARVIS background development agent currently requires Windows."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install .NET 8 SDK, reopen PowerShell, and retry."
}
$sdk = (& dotnet --version).Trim()
if (-not $sdk.StartsWith("8.")) {
    throw "JARVIS requires the .NET 8 SDK; detected $sdk."
}
if (-not (Test-Path $project)) {
    throw "JARVIS application project not found: $project"
}

if ([string]::IsNullOrWhiteSpace($env:JARVIS_AI_PROVIDER)) {
    $env:JARVIS_AI_PROVIDER = "ollama"
}
if ([string]::IsNullOrWhiteSpace($env:JARVIS_WORKSPACE_ROOT)) {
    $env:JARVIS_WORKSPACE_ROOT = $repoRoot
}

$provider = $env:JARVIS_AI_PROVIDER.Trim().ToLowerInvariant()
switch ($provider) {
    "ollama" {
        if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
            throw "Ollama is not installed or not on PATH. Run .\scripts\Install-LocalModels.ps1 first."
        }
        if ([string]::IsNullOrWhiteSpace($env:OLLAMA_MODEL)) {
            $env:OLLAMA_MODEL = "qwen3.5:2b-q4_K_M"
        }
        $installed = (& ollama list 2>$null | Out-String)
        if ($LASTEXITCODE -ne 0 -or $installed -notmatch [regex]::Escape($env:OLLAMA_MODEL)) {
            throw "Local model '$env:OLLAMA_MODEL' is not installed. Run .\scripts\Install-LocalModels.ps1 or set OLLAMA_MODEL to an installed model."
        }
    }
    "gemini" {
        if ([string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) { throw "GEMINI_API_KEY is required for the Gemini background agent." }
    }
    "openai" {
        if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) { throw "OPENAI_API_KEY is required for the OpenAI background agent." }
    }
    "claude" {
        if ([string]::IsNullOrWhiteSpace($env:ANTHROPIC_API_KEY)) { throw "ANTHROPIC_API_KEY is required for the Claude background agent." }
    }
    "omniroute" {
        if ([string]::IsNullOrWhiteSpace($env:OMNIROUTE_API_KEY) -or
            [string]::IsNullOrWhiteSpace($env:OMNIROUTE_MODEL) -or
            [string]::IsNullOrWhiteSpace($env:OMNIROUTE_FREE_MODELS) -or
            $env:OMNIROUTE_MODEL -eq "auto") {
            throw "OmniRoute requires an API key, explicit model ID and exact approved model allowlist."
        }
    }
    default { throw "Unsupported or non-functional provider '$provider'. Configure Ollama, Gemini, OpenAI, Claude or allowlisted OmniRoute." }
}

Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        Write-Host "Building the JARVIS background agent..." -ForegroundColor Cyan
        dotnet build $project --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "JARVIS build failed; background agent was not started." }
    }

    Write-Host "Starting the low-CPU JARVIS development agent." -ForegroundColor Green
    Write-Host "It executes only tasks you explicitly approve inside JARVIS." -ForegroundColor Yellow
    Write-Host "Log: $env:LOCALAPPDATA\JARVIS\Agent\agent.log"
    dotnet run --project $project --configuration Release --no-build -- --agent
    if ($LASTEXITCODE -ne 0) { throw "JARVIS background agent exited with code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
