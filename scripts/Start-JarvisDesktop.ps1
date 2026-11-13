[CmdletBinding()]
param(
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $repoRoot "src\Council.Desktop\Council.Desktop.csproj"
$solution = Join-Path $repoRoot "JARVIS.Council.sln"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install the .NET 8 SDK, reopen PowerShell, and run this script again."
}

$sdk = (& dotnet --version).Trim()
if (-not $sdk.StartsWith("8.")) {
    Write-Warning "JARVIS targets .NET 8; detected SDK $sdk. Install .NET 8 SDK if restore/build fails."
}

if (-not (Test-Path $desktopProject)) {
    throw "Desktop project not found: $desktopProject"
}

if ([string]::IsNullOrWhiteSpace($env:JARVIS_AI_PROVIDER)) {
    $env:JARVIS_AI_PROVIDER = "ollama"
    Write-Host "Provider defaulted to local Ollama. No cloud API key is required." -ForegroundColor Cyan
}

if ($env:JARVIS_AI_PROVIDER -eq "ollama") {
    if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
        Write-Warning "Ollama is not installed or is not on PATH. Install it, then run .\scripts\Install-LocalModels.ps1."
    }
    else {
        $model = if ([string]::IsNullOrWhiteSpace($env:OLLAMA_MODEL)) { "qwen3.5:2b-q4_K_M" } else { $env:OLLAMA_MODEL }
        $env:OLLAMA_MODEL = $model
        $available = (& ollama list 2>$null | Out-String)
        if ($LASTEXITCODE -ne 0 -or $available -notmatch [regex]::Escape($model)) {
            Write-Warning "Local model '$model' was not found. Run .\scripts\Install-LocalModels.ps1 or pull the configured model."
        }
    }
}

Push-Location $repoRoot
try {
    Write-Host "Restoring JARVIS..." -ForegroundColor Cyan
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    Write-Host "Building desktop JARVIS..." -ForegroundColor Cyan
    dotnet build $desktopProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Desktop build failed. See the errors above." }

    if (-not $BuildOnly) {
        Write-Host "Starting JARVIS Command Center..." -ForegroundColor Green
        dotnet run --project $desktopProject --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "JARVIS exited with an error." }
    }
}
finally {
    Pop-Location
}
