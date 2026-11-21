[CmdletBinding()]
param(
    [switch]$BuildOnly,
    # Keeps the window open after a failure so the error can be read (used by the "Update JARVIS" shortcut).
    [switch]$PauseOnError
)

$ErrorActionPreference = "Stop"
trap {
    Write-Host ""
    Write-Host ("JARVIS could not be started or updated: " + $_.Exception.Message) -ForegroundColor Red
    if ($PauseOnError) { Read-Host "Press Enter to close this window" }
    exit 1
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $repoRoot "src\Council.Desktop\Council.Desktop.csproj"
$desktopExe = Join-Path $repoRoot "src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install the .NET 8 SDK, reopen PowerShell, and run this script again."
}

$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdk.StartsWith("8.")) {
    throw "JARVIS requires the .NET 8 SDK. Detected: $sdk"
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

$running = Get-Process -Name "Council.Desktop" -ErrorAction SilentlyContinue
if ($running -and -not $BuildOnly) {
    Write-Host "JARVIS is already open. Close it first to rebuild, or use the JARVIS shortcut to switch to it." -ForegroundColor Yellow
    if ($PauseOnError) { Read-Host "Press Enter to close this window" }
    return
}
if ($running) {
    throw "Close JARVIS before rebuilding it; its files are in use."
}

Push-Location $repoRoot
try {
    # dotnet build performs the required restore and uses MSBuild's incremental
    # compilation, avoiding a separate full-solution restore on every launch.
    Write-Host "Building JARVIS Command Center..." -ForegroundColor Cyan
    dotnet build $desktopProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Desktop build failed. See the errors above." }

    if ($BuildOnly) {
        Write-Host "Desktop build completed. No GUI process was started." -ForegroundColor Green
        return
    }

    if (-not (Test-Path $desktopExe)) {
        throw "Desktop executable was not produced: $desktopExe"
    }

    Write-Host "Starting the JARVIS Command Center as a separate desktop process..." -ForegroundColor Green
    Start-Process -FilePath $desktopExe -WorkingDirectory $repoRoot
}
finally {
    Pop-Location
}
